using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SwcsScanner.Api.Options;

namespace SwcsScanner.Api.Data;

public sealed class SwcsProductLookupRepository : ISwcsProductLookupRepository
{
    private const int DefaultSchemaCacheMinutes = 10;
    private static readonly Regex IdentifierRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private readonly string _connectionString;
    private readonly string _quotedProductTable;
    private readonly string? _quotedBarcodeTable;
    private readonly string? _quotedBarcodeColumn;
    private readonly string? _quotedPriceTable;
    private readonly string? _quotedPriceColumn;
    private readonly string _productNameField;
    private readonly SwcsOptions _options;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);

    private SwcsSchemaSnapshot? _cachedSchema;
    private DateTimeOffset _schemaExpiresAtUtc = DateTimeOffset.MinValue;

    public SwcsProductLookupRepository(
        IConfiguration configuration,
        IOptions<SwcsOptions> options)
    {
        _options = options.Value;
        _connectionString = configuration.GetConnectionString("SwcsReadonly")
                            ?? throw new InvalidOperationException("缺少连接字符串 ConnectionStrings:SwcsReadonly。");
        _quotedProductTable = QuoteTableName(_options.ProductTable);
        _quotedBarcodeTable = string.IsNullOrWhiteSpace(_options.BarcodeTable) ? null : QuoteTableName(_options.BarcodeTable);
        _quotedBarcodeColumn = string.IsNullOrWhiteSpace(_options.BarcodeColumn) ? null : QuoteIdentifier(_options.BarcodeColumn);
        _quotedPriceTable = string.IsNullOrWhiteSpace(_options.PriceTable) ? null : QuoteTableName(_options.PriceTable);
        _quotedPriceColumn = string.IsNullOrWhiteSpace(_options.PriceColumn) ? null : QuoteIdentifier(_options.PriceColumn);
        _productNameField = QuoteIdentifier(_options.ProductNameField);
    }

    public async Task<SwcsSchemaSnapshot> GetSchemaSnapshotAsync(CancellationToken cancellationToken)
    {
        if (_cachedSchema is not null && _schemaExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return _cachedSchema;
        }

        await _schemaLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedSchema is not null && _schemaExpiresAtUtc > DateTimeOffset.UtcNow)
            {
                return _cachedSchema;
            }

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string columnSql = """
                SELECT c.name
                FROM sys.columns c
                INNER JOIN sys.objects o ON c.object_id = o.object_id
                WHERE o.object_id = OBJECT_ID(@ObjectName) AND o.type = 'U';
                """;

            var columns = (await connection.QueryAsync<string>(new CommandDefinition(
                columnSql,
                new { ObjectName = _options.ProductTable },
                cancellationToken: cancellationToken)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            const string functionSql = """
                SELECT CASE
                    WHEN OBJECT_ID('dbo.fn_strunitptype', 'FN') IS NOT NULL THEN 1
                    ELSE 0
                END;
                """;

            var hasFunction = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                functionSql,
                cancellationToken: cancellationToken)) == 1;

            _cachedSchema = new SwcsSchemaSnapshot
            {
                Columns = columns,
                HasBarcodeFunction = hasFunction
            };

            var cacheMinutes = _options.SchemaCacheMinutes <= 0
                ? DefaultSchemaCacheMinutes
                : _options.SchemaCacheMinutes;
            _schemaExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(cacheMinutes);
            return _cachedSchema;
        }
        finally
        {
            _schemaLock.Release();
        }
    }

    public async Task<DbProductLookupRow?> LookupByFieldAsync(
        string barcode,
        string barcodeField,
        string? priceField,
        string? specificationField,
        CancellationToken cancellationToken)
    {
        if (_quotedBarcodeTable is not null && _quotedBarcodeColumn is not null)
        {
            return await LookupFromBarcodeTableAsync(barcode, priceField, specificationField, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(barcodeField))
        {
            return null;
        }

        var barcodeColumn = QuoteIdentifier(barcodeField);
        var specificationExpression = BuildSpecificationExpression(specificationField);
        var priceExpression = BuildPriceExpression(priceField);
        var priceJoin = BuildPriceJoin("p");

        var sql = $"""
            SELECT TOP (1)
                p.{_productNameField} AS ProductName,
                {specificationExpression} AS Specification,
                {priceExpression} AS Price
            FROM {_quotedProductTable} AS p
            {priceJoin}
            WHERE p.{barcodeColumn} = @Barcode;
            """;

        await using var connection = new SqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<DbProductLookupRow>(new CommandDefinition(
            sql,
            new { Barcode = barcode },
            cancellationToken: cancellationToken));
    }

    public async Task<DbProductLookupRow?> LookupByFunctionAsync(
        string barcode,
        string? priceField,
        string? specificationField,
        CancellationToken cancellationToken)
    {
        var specificationExpression = BuildSpecificationExpression(specificationField);
        var priceExpression = BuildPriceExpression(priceField);
        var priceJoin = BuildPriceJoin("p");

        var sql = $"""
            SELECT TOP (1)
                p.{_productNameField} AS ProductName,
                {specificationExpression} AS Specification,
                {priceExpression} AS Price
            FROM {_quotedProductTable} AS p
            {priceJoin}
            WHERE dbo.fn_strunitptype('B', p.ptypeid, 0) = @Barcode;
            """;

        await using var connection = new SqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<DbProductLookupRow>(new CommandDefinition(
            sql,
            new { Barcode = barcode },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<DbProductSearchRow>> SearchByBarcodeFragmentAsync(
        string keyword,
        IReadOnlyList<string> barcodeFields,
        string? priceField,
        string? specificationField,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(keyword) || limit <= 0)
        {
            return [];
        }

        if (_quotedBarcodeTable is not null && _quotedBarcodeColumn is not null)
        {
            return await SearchFromBarcodeTableAsync(keyword, priceField, specificationField, limit, cancellationToken);
        }

        if (barcodeFields.Count == 0)
        {
            return [];
        }

        return await SearchFromProductTableAsync(keyword, barcodeFields, priceField, specificationField, limit, cancellationToken);
    }

    private async Task<DbProductLookupRow?> LookupFromBarcodeTableAsync(
        string barcode,
        string? priceField,
        string? specificationField,
        CancellationToken cancellationToken)
    {
        var specificationExpression = BuildSpecificationExpression(specificationField);
        var priceExpression = BuildPriceExpression(priceField);
        var priceJoin = BuildPriceJoin("p");

        var sql = $"""
            SELECT TOP (1)
                p.{_productNameField} AS ProductName,
                {specificationExpression} AS Specification,
                {priceExpression} AS Price
            FROM {_quotedBarcodeTable} AS bc
            INNER JOIN {_quotedProductTable} AS p ON bc.PTypeId = p.ptypeid
            {priceJoin}
            WHERE bc.{_quotedBarcodeColumn} = @Barcode;
            """;

        await using var connection = new SqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<DbProductLookupRow>(new CommandDefinition(
            sql,
            new { Barcode = barcode },
            cancellationToken: cancellationToken));
    }

    private async Task<IReadOnlyList<DbProductSearchRow>> SearchFromBarcodeTableAsync(
        string keyword,
        string? priceField,
        string? specificationField,
        int limit,
        CancellationToken cancellationToken)
    {
        var specificationExpression = BuildSpecificationExpression(specificationField);
        var priceExpression = BuildPriceExpression(priceField);
        var priceJoin = BuildPriceJoin("p");
        var containsPattern = BuildContainsPattern(keyword);
        var prefixPattern = BuildPrefixPattern(keyword);
        var matchedBy = string.IsNullOrWhiteSpace(_options.BarcodeColumn)
            ? "BarcodeTable"
            : _options.BarcodeColumn!;

        var sql = $"""
            WITH candidates AS (
                SELECT
                    p.ptypeid AS ProductId,
                    p.{_productNameField} AS ProductName,
                    {specificationExpression} AS Specification,
                    {priceExpression} AS Price,
                    CAST(bc.{_quotedBarcodeColumn} AS NVARCHAR(100)) AS Barcode,
                    @MatchedBy AS BarcodeMatchedBy,
                    CASE
                        WHEN CAST(bc.{_quotedBarcodeColumn} AS NVARCHAR(100)) LIKE @PrefixPattern ESCAPE '\' THEN 0
                        ELSE 1
                    END AS MatchRank,
                    ROW_NUMBER() OVER (
                        PARTITION BY p.ptypeid
                        ORDER BY
                            CASE
                                WHEN CAST(bc.{_quotedBarcodeColumn} AS NVARCHAR(100)) LIKE @PrefixPattern ESCAPE '\' THEN 0
                                ELSE 1
                            END,
                            CAST(bc.{_quotedBarcodeColumn} AS NVARCHAR(100))
                    ) AS DedupRank
                FROM {_quotedBarcodeTable} AS bc
                INNER JOIN {_quotedProductTable} AS p ON bc.PTypeId = p.ptypeid
                {priceJoin}
                WHERE CAST(bc.{_quotedBarcodeColumn} AS NVARCHAR(100)) LIKE @ContainsPattern ESCAPE '\'
            )
            SELECT TOP (@Limit)
                ProductName,
                Specification,
                Price,
                Barcode,
                BarcodeMatchedBy
            FROM candidates
            WHERE DedupRank = 1
            ORDER BY MatchRank, Barcode;
            """;

        await using var connection = new SqlConnection(_connectionString);
        var rows = await connection.QueryAsync<DbProductSearchRow>(new CommandDefinition(
            sql,
            new
            {
                Limit = limit,
                ContainsPattern = containsPattern,
                PrefixPattern = prefixPattern,
                MatchedBy = matchedBy
            },
            cancellationToken: cancellationToken));
        return rows.ToList();
    }

    private async Task<IReadOnlyList<DbProductSearchRow>> SearchFromProductTableAsync(
        string keyword,
        IReadOnlyList<string> barcodeFields,
        string? priceField,
        string? specificationField,
        int limit,
        CancellationToken cancellationToken)
    {
        var specificationExpression = BuildSpecificationExpression(specificationField);
        var priceExpression = BuildPriceExpression(priceField);
        var priceJoin = BuildPriceJoin("p");
        var containsPattern = BuildContainsPattern(keyword);
        var prefixPattern = BuildPrefixPattern(keyword);
        var parameters = new DynamicParameters();
        parameters.Add("Limit", limit);
        parameters.Add("ContainsPattern", containsPattern);
        parameters.Add("PrefixPattern", prefixPattern);

        var unionSql = new StringBuilder();
        var hasAnyField = false;

        for (var index = 0; index < barcodeFields.Count; index++)
        {
            var field = barcodeFields[index];
            if (string.IsNullOrWhiteSpace(field))
            {
                continue;
            }

            var quotedBarcodeField = QuoteIdentifier(field);
            if (hasAnyField)
            {
                unionSql.AppendLine("UNION ALL");
            }

            unionSql.AppendLine($"""
                SELECT
                    p.ptypeid AS ProductId,
                    p.{_productNameField} AS ProductName,
                    {specificationExpression} AS Specification,
                    {priceExpression} AS Price,
                    CAST(p.{quotedBarcodeField} AS NVARCHAR(100)) AS Barcode,
                    @MatchedBy{index} AS BarcodeMatchedBy,
                    {index} AS FieldRank,
                    CASE
                        WHEN CAST(p.{quotedBarcodeField} AS NVARCHAR(100)) LIKE @PrefixPattern ESCAPE '\' THEN 0
                        ELSE 1
                    END AS MatchRank
                FROM {_quotedProductTable} AS p
                {priceJoin}
                WHERE CAST(p.{quotedBarcodeField} AS NVARCHAR(100)) LIKE @ContainsPattern ESCAPE '\'
                """);

            parameters.Add($"MatchedBy{index}", field);
            hasAnyField = true;
        }

        if (!hasAnyField)
        {
            return [];
        }

        var sql = $"""
            WITH raw AS (
            {unionSql}
            ),
            dedup AS (
                SELECT
                    ProductId,
                    ProductName,
                    Specification,
                    Price,
                    Barcode,
                    BarcodeMatchedBy,
                    FieldRank,
                    MatchRank,
                    ROW_NUMBER() OVER (
                        PARTITION BY ProductId
                        ORDER BY MatchRank, FieldRank, Barcode
                    ) AS DedupRank
                FROM raw
            )
            SELECT TOP (@Limit)
                ProductName,
                Specification,
                Price,
                Barcode,
                BarcodeMatchedBy
            FROM dedup
            WHERE DedupRank = 1
            ORDER BY MatchRank, FieldRank, Barcode;
            """;

        await using var connection = new SqlConnection(_connectionString);
        var rows = await connection.QueryAsync<DbProductSearchRow>(new CommandDefinition(
            sql,
            parameters,
            cancellationToken: cancellationToken));
        return rows.ToList();
    }

    private string BuildSpecificationExpression(string? specificationField)
    {
        if (string.IsNullOrWhiteSpace(specificationField))
        {
            return "CAST(NULL AS NVARCHAR(200))";
        }

        return $"CAST(p.{QuoteIdentifier(specificationField)} AS NVARCHAR(200))";
    }

    private string BuildPriceExpression(string? priceField)
    {
        if (_quotedPriceTable is not null && _quotedPriceColumn is not null)
        {
            return $"CAST(pr.{_quotedPriceColumn} AS DECIMAL(18, 2))";
        }

        if (string.IsNullOrWhiteSpace(priceField))
        {
            throw new InvalidOperationException("未指定可用价格字段。");
        }

        return $"CAST(p.{QuoteIdentifier(priceField)} AS DECIMAL(18, 2))";
    }

    private string BuildPriceJoin(string productAlias)
    {
        if (_quotedPriceTable is null || _quotedPriceColumn is null)
        {
            return string.Empty;
        }

        return $"INNER JOIN {_quotedPriceTable} AS pr ON {productAlias}.ptypeid = pr.PTypeId";
    }

    private static string BuildContainsPattern(string keyword)
    {
        return $"%{EscapeLikeValue(keyword)}%";
    }

    private static string BuildPrefixPattern(string keyword)
    {
        return $"{EscapeLikeValue(keyword)}%";
    }

    private static string EscapeLikeValue(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal);
    }

    private static string QuoteTableName(string tableName)
    {
        var parts = tableName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2)
        {
            throw new InvalidOperationException($"表名配置不合法: {tableName}");
        }

        return string.Join(".", parts.Select(QuoteIdentifier));
    }

    private static string QuoteIdentifier(string identifier)
    {
        if (!IdentifierRegex.IsMatch(identifier))
        {
            throw new InvalidOperationException($"字段名不合法: {identifier}");
        }

        return $"[{identifier}]";
    }
}
