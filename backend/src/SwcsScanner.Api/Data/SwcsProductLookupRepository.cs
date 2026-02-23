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
    private const string ProductCodeFieldName = "pusercode";
    private const string ProductShortCodeFieldName = "pnamepy";
    private static readonly Regex IdentifierRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private readonly string _connectionString;
    private readonly string _quotedProductTable;
    private readonly string? _quotedBarcodeTable;
    private readonly string? _quotedBarcodeColumn;
    private readonly string? _quotedPriceTable;
    private readonly string? _quotedPriceColumn;
    private readonly string _productNameField;
    private readonly string? _preferredPriceTypeId;
    private readonly bool _useUnitScopedBarcodePrice;
    private readonly int _queryTimeoutSeconds;
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
        _preferredPriceTypeId = string.IsNullOrWhiteSpace(_options.PriceTypeId) ? null : _options.PriceTypeId.Trim();
        _useUnitScopedBarcodePrice = IsXwPtypePriceTable(_options.PriceTable);
        _queryTimeoutSeconds = _options.QueryTimeoutSeconds;
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

            var columns = (await connection.QueryAsync<string>(CreateCommand(
                columnSql,
                new { ObjectName = _options.ProductTable },
                cancellationToken)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            const string functionSql = """
                SELECT CASE
                    WHEN OBJECT_ID('dbo.fn_strunitptype', 'FN') IS NOT NULL THEN 1
                    ELSE 0
                END;
                """;

            var hasFunction = await connection.ExecuteScalarAsync<int>(CreateCommand(
                functionSql,
                null,
                cancellationToken)) == 1;

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

        var schema = await GetSchemaSnapshotAsync(cancellationToken);
        var barcodeColumn = QuoteIdentifier(barcodeField);
        var specificationExpression = BuildSpecificationExpression(specificationField);
        var priceExpression = BuildPriceExpression(priceField);
        var priceJoin = BuildPriceJoin("p");
        var productCodeExpression = BuildOptionalTextFieldExpression(schema.Columns, "p", ProductCodeFieldName, 100);
        var productShortCodeExpression = BuildOptionalTextFieldExpression(schema.Columns, "p", ProductShortCodeFieldName, 100);

        var sql = $"""
            SELECT TOP (1)
                p.ptypeid AS ProductId,
                p.{_productNameField} AS ProductName,
                {productCodeExpression} AS ProductCode,
                {productShortCodeExpression} AS ProductShortCode,
                {specificationExpression} AS Specification,
                {priceExpression} AS Price,
                CAST(NULL AS NVARCHAR(50)) AS MatchedUnitId,
                CAST(@Barcode AS NVARCHAR(100)) AS MatchedBarcode
            FROM {_quotedProductTable} AS p
            {priceJoin}
            WHERE p.{barcodeColumn} = @Barcode;
            """;

        await using var connection = new SqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<DbProductLookupRow>(CreateCommand(
            sql,
            new { Barcode = barcode },
            cancellationToken));
    }

    public async Task<DbProductLookupRow?> LookupByFunctionAsync(
        string barcode,
        string? priceField,
        string? specificationField,
        CancellationToken cancellationToken)
    {
        var schema = await GetSchemaSnapshotAsync(cancellationToken);
        var specificationExpression = BuildSpecificationExpression(specificationField);
        var priceExpression = BuildPriceExpression(priceField);
        var priceJoin = BuildPriceJoin("p");
        var productCodeExpression = BuildOptionalTextFieldExpression(schema.Columns, "p", ProductCodeFieldName, 100);
        var productShortCodeExpression = BuildOptionalTextFieldExpression(schema.Columns, "p", ProductShortCodeFieldName, 100);

        var sql = $"""
            SELECT TOP (1)
                p.ptypeid AS ProductId,
                p.{_productNameField} AS ProductName,
                {productCodeExpression} AS ProductCode,
                {productShortCodeExpression} AS ProductShortCode,
                {specificationExpression} AS Specification,
                {priceExpression} AS Price,
                CAST(NULL AS NVARCHAR(50)) AS MatchedUnitId,
                CAST(@Barcode AS NVARCHAR(100)) AS MatchedBarcode
            FROM {_quotedProductTable} AS p
            {priceJoin}
            WHERE dbo.fn_strunitptype('B', p.ptypeid, 0) = @Barcode;
            """;

        await using var connection = new SqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<DbProductLookupRow>(CreateCommand(
            sql,
            new { Barcode = barcode },
            cancellationToken));
    }

    public async Task<DbProductLookupRow?> LookupByCompositeKeywordAsync(
        string keyword,
        string? priceField,
        string? specificationField,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return null;
        }

        var schema = await GetSchemaSnapshotAsync(cancellationToken);
        var compositeExpression = BuildCompositeKeywordExpression(schema);
        if (compositeExpression is null)
        {
            return null;
        }

        var containsPattern = BuildContainsPattern(keyword);
        var prefixPattern = BuildPrefixPattern(keyword);
        var specificationExpression = BuildSpecificationExpression(specificationField);
        var priceExpression = BuildPriceExpression(priceField);
        var priceJoin = BuildPriceJoin("p");
        var productCodeExpression = BuildOptionalTextFieldExpression(schema.Columns, "p", ProductCodeFieldName, 100);
        var productShortCodeExpression = BuildOptionalTextFieldExpression(schema.Columns, "p", ProductShortCodeFieldName, 100);
        var orderByExpression = schema.Columns.Contains("ptypeid")
            ? "p.[ptypeid]"
            : $"p.{_productNameField}";

        var sql = $"""
            SELECT TOP (1)
                p.ptypeid AS ProductId,
                p.{_productNameField} AS ProductName,
                {productCodeExpression} AS ProductCode,
                {productShortCodeExpression} AS ProductShortCode,
                {specificationExpression} AS Specification,
                {priceExpression} AS Price,
                CAST(NULL AS NVARCHAR(50)) AS MatchedUnitId,
                CAST(@ContainsKeyword AS NVARCHAR(100)) AS MatchedBarcode
            FROM {_quotedProductTable} AS p
            {priceJoin}
            WHERE ({compositeExpression}) LIKE @ContainsPattern ESCAPE '\'
            ORDER BY
                CASE
                    WHEN ({compositeExpression}) LIKE @PrefixPattern ESCAPE '\' THEN 0
                    ELSE 1
                END,
                {orderByExpression};
            """;

        await using var connection = new SqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<DbProductLookupRow>(CreateCommand(
            sql,
            new
            {
                ContainsKeyword = keyword,
                ContainsPattern = containsPattern,
                PrefixPattern = prefixPattern
            },
            cancellationToken));
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

    public async Task<IReadOnlyList<DbProductUnitRow>> GetUnitsByProductIdAsync(
        string productId,
        string? matchedBarcode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(productId))
        {
            return [];
        }

        if (_quotedBarcodeTable is null || _quotedBarcodeColumn is null)
        {
            return [];
        }

        if (_quotedPriceTable is null || _quotedPriceColumn is null)
        {
            return [];
        }

        var orderBy = string.IsNullOrWhiteSpace(_preferredPriceTypeId)
            ? "CASE WHEN prRaw.PRTypeId = '0001' THEN 0 ELSE 1 END, prRaw.PRTypeId"
            : "CASE WHEN prRaw.PRTypeId = @PreferredPriceTypeId THEN 0 WHEN prRaw.PRTypeId = '0001' THEN 1 ELSE 2 END, prRaw.PRTypeId";

        var sql = $"""
            SET ARITHABORT ON;
            SELECT
                CAST(u.Ordid AS NVARCHAR(50)) AS UnitId,
                CAST(ISNULL(u.Unit1, N'') AS NVARCHAR(100)) AS UnitName,
                CAST(ISNULL(u.URate, 0) AS NVARCHAR(100)) AS UnitRate,
                CAST(ISNULL(pr.Price, 0) AS DECIMAL(18, 2)) AS Price,
                CAST(ISNULL(bc.BarcodeList, N'') AS NVARCHAR(1000)) AS BarcodeList,
                CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM {_quotedBarcodeTable} AS m
                        WHERE m.PTypeId = u.PTypeId
                          AND m.UnitID = u.Ordid
                          AND m.{_quotedBarcodeColumn} = @MatchedBarcode
                    ) THEN CAST(1 AS BIT)
                    ELSE CAST(0 AS BIT)
                END AS IsMatchedUnit
            FROM dbo.xw_PtypeUnit AS u
            OUTER APPLY (
                SELECT TOP (1)
                    prRaw.{_quotedPriceColumn} AS Price
                FROM {_quotedPriceTable} AS prRaw
                WHERE prRaw.PTypeId = u.PTypeId
                  AND prRaw.UnitID = u.Ordid
                ORDER BY {orderBy}
            ) AS pr
            OUTER APPLY (
                SELECT
                    STUFF((
                        SELECT N',' + CAST(bcRaw.{_quotedBarcodeColumn} AS NVARCHAR(100))
                        FROM {_quotedBarcodeTable} AS bcRaw
                        WHERE bcRaw.PTypeId = u.PTypeId
                          AND bcRaw.UnitID = u.Ordid
                        ORDER BY
                            CASE WHEN ISNUMERIC(bcRaw.Ordid) = 1 THEN CAST(bcRaw.Ordid AS INT) ELSE 2147483647 END,
                            bcRaw.Ordid
                        FOR XML PATH(''), TYPE
                    ).value('.', 'NVARCHAR(MAX)'), 1, 1, N'') AS BarcodeList
            ) AS bc
            WHERE u.PTypeId = @ProductId
            ORDER BY
                CASE WHEN ISNUMERIC(u.Ordid) = 1 THEN CAST(u.Ordid AS INT) ELSE 2147483647 END,
                u.Ordid;
            """;

        await using var connection = new SqlConnection(_connectionString);
        var rows = await connection.QueryAsync<DbProductUnitRow>(CreateCommand(
            sql,
            new
            {
                ProductId = productId,
                MatchedBarcode = matchedBarcode,
                PreferredPriceTypeId = _preferredPriceTypeId
            },
            cancellationToken));
        return rows.ToList();
    }

    private async Task<DbProductLookupRow?> LookupFromBarcodeTableAsync(
        string barcode,
        string? priceField,
        string? specificationField,
        CancellationToken cancellationToken)
    {
        var schema = await GetSchemaSnapshotAsync(cancellationToken);
        var specificationExpression = BuildSpecificationExpression(specificationField);
        var priceExpression = BuildBarcodeTablePriceExpression(priceField);
        var priceJoin = BuildBarcodeTablePriceJoin("p", "bc", priceField);
        var productCodeExpression = BuildOptionalTextFieldExpression(schema.Columns, "p", ProductCodeFieldName, 100);
        var productShortCodeExpression = BuildOptionalTextFieldExpression(schema.Columns, "p", ProductShortCodeFieldName, 100);

        var sql = $"""
            SELECT TOP (1)
                p.ptypeid AS ProductId,
                p.{_productNameField} AS ProductName,
                {productCodeExpression} AS ProductCode,
                {productShortCodeExpression} AS ProductShortCode,
                {specificationExpression} AS Specification,
                {priceExpression} AS Price,
                CAST(bc.UnitID AS NVARCHAR(50)) AS MatchedUnitId,
                CAST(bc.{_quotedBarcodeColumn} AS NVARCHAR(100)) AS MatchedBarcode
            FROM {_quotedBarcodeTable} AS bc
            INNER JOIN {_quotedProductTable} AS p ON bc.PTypeId = p.ptypeid
            {priceJoin}
            WHERE bc.{_quotedBarcodeColumn} = @Barcode;
            """;

        await using var connection = new SqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<DbProductLookupRow>(CreateCommand(
            sql,
            new
            {
                Barcode = barcode,
                PreferredPriceTypeId = _preferredPriceTypeId
            },
            cancellationToken));
    }

    private async Task<IReadOnlyList<DbProductSearchRow>> SearchFromBarcodeTableAsync(
        string keyword,
        string? priceField,
        string? specificationField,
        int limit,
        CancellationToken cancellationToken)
    {
        var schema = await GetSchemaSnapshotAsync(cancellationToken);
        var specificationExpression = BuildSpecificationExpression(specificationField);
        var priceExpression = BuildBarcodeTablePriceExpression(priceField);
        var priceJoin = BuildBarcodeTablePriceJoin("p", "bc", priceField);
        var containsPattern = BuildContainsPattern(keyword);
        var prefixPattern = BuildPrefixPattern(keyword);
        var productCodeExpression = BuildOptionalTextFieldExpression(schema.Columns, "p", ProductCodeFieldName, 100);
        var productShortCodeExpression = BuildOptionalTextFieldExpression(schema.Columns, "p", ProductShortCodeFieldName, 100);
        var barcodeExpression = $"CAST(bc.{_quotedBarcodeColumn} AS NVARCHAR(100))";
        var productNameExpression = $"CAST(p.{_productNameField} AS NVARCHAR(200))";

        var searchTargets = new List<(string Expression, string MatchedBy)>
        {
            (
                barcodeExpression,
                string.IsNullOrWhiteSpace(_options.BarcodeColumn)
                    ? "BarcodeTable"
                    : _options.BarcodeColumn!
            )
        };

        if (schema.Columns.Contains(ProductShortCodeFieldName))
        {
            searchTargets.Add(($"CAST(p.{QuoteIdentifier(ProductShortCodeFieldName)} AS NVARCHAR(100))", ProductShortCodeFieldName));
        }

        if (schema.Columns.Contains(ProductCodeFieldName))
        {
            searchTargets.Add(($"CAST(p.{QuoteIdentifier(ProductCodeFieldName)} AS NVARCHAR(100))", ProductCodeFieldName));
        }

        searchTargets.Add((productNameExpression, _options.ProductNameField));

        var whereClause = string.Join(
            $"{Environment.NewLine}                   OR ",
            searchTargets.Select(target => $"{target.Expression} LIKE @ContainsPattern ESCAPE '\\'"));

        var matchRankExpressionBuilder = new StringBuilder();
        matchRankExpressionBuilder.AppendLine("CASE");
        for (var index = 0; index < searchTargets.Count; index++)
        {
            matchRankExpressionBuilder.AppendLine($"                        WHEN {searchTargets[index].Expression} LIKE @PrefixPattern ESCAPE '\\' THEN {index}");
        }

        matchRankExpressionBuilder.AppendLine($"                        ELSE {searchTargets.Count}");
        matchRankExpressionBuilder.Append("                    END");
        var matchRankExpression = matchRankExpressionBuilder.ToString();

        var matchedByExpressionBuilder = new StringBuilder();
        matchedByExpressionBuilder.AppendLine("CASE");
        for (var index = 0; index < searchTargets.Count; index++)
        {
            matchedByExpressionBuilder.AppendLine($"                        WHEN {searchTargets[index].Expression} LIKE @PrefixPattern ESCAPE '\\' THEN @MatchedBy{index}");
        }

        matchedByExpressionBuilder.AppendLine("                        ELSE @MatchedBy0");
        matchedByExpressionBuilder.Append("                    END");
        var matchedByExpression = matchedByExpressionBuilder.ToString();

        var parameters = new DynamicParameters();
        parameters.Add("Limit", limit);
        parameters.Add("ContainsPattern", containsPattern);
        parameters.Add("PrefixPattern", prefixPattern);
        parameters.Add("PreferredPriceTypeId", _preferredPriceTypeId);

        for (var index = 0; index < searchTargets.Count; index++)
        {
            parameters.Add($"MatchedBy{index}", searchTargets[index].MatchedBy);
        }

        var sql = $"""
            WITH candidates AS (
                SELECT
                    p.ptypeid AS ProductId,
                    p.{_productNameField} AS ProductName,
                    {productCodeExpression} AS ProductCode,
                    {productShortCodeExpression} AS ProductShortCode,
                    {specificationExpression} AS Specification,
                    {priceExpression} AS Price,
                    {barcodeExpression} AS Barcode,
                    {matchedByExpression} AS BarcodeMatchedBy,
                    {matchRankExpression} AS MatchRank,
                    ROW_NUMBER() OVER (
                        PARTITION BY p.ptypeid
                        ORDER BY
                            {matchRankExpression},
                            {barcodeExpression}
                    ) AS DedupRank
                FROM {_quotedBarcodeTable} AS bc
                INNER JOIN {_quotedProductTable} AS p ON bc.PTypeId = p.ptypeid
                {priceJoin}
                WHERE {whereClause}
            )
            SELECT TOP (@Limit)
                ProductName,
                ProductCode,
                ProductShortCode,
                Specification,
                Price,
                Barcode,
                BarcodeMatchedBy
            FROM candidates
            WHERE DedupRank = 1
            ORDER BY MatchRank, Barcode;
            """;

        await using var connection = new SqlConnection(_connectionString);
        var rows = await connection.QueryAsync<DbProductSearchRow>(CreateCommand(
            sql,
            parameters,
            cancellationToken));
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
        var schema = await GetSchemaSnapshotAsync(cancellationToken);
        var specificationExpression = BuildSpecificationExpression(specificationField);
        var priceExpression = BuildPriceExpression(priceField);
        var priceJoin = BuildPriceJoin("p");
        var containsPattern = BuildContainsPattern(keyword);
        var prefixPattern = BuildPrefixPattern(keyword);
        var productCodeExpression = BuildOptionalTextFieldExpression(schema.Columns, "p", ProductCodeFieldName, 100);
        var productShortCodeExpression = BuildOptionalTextFieldExpression(schema.Columns, "p", ProductShortCodeFieldName, 100);

        var normalizedBarcodeFields = barcodeFields
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedBarcodeFields.Count == 0)
        {
            return [];
        }

        var preferredBarcodeExpression = BuildPreferredBarcodeExpression("p", normalizedBarcodeFields);
        var searchableFields = new List<string>(normalizedBarcodeFields);

        if (schema.Columns.Contains(ProductShortCodeFieldName))
        {
            searchableFields.Add(ProductShortCodeFieldName);
        }

        if (schema.Columns.Contains(ProductCodeFieldName))
        {
            searchableFields.Add(ProductCodeFieldName);
        }

        if (schema.Columns.Contains(_options.ProductNameField))
        {
            searchableFields.Add(_options.ProductNameField);
        }

        searchableFields = searchableFields
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var parameters = new DynamicParameters();
        parameters.Add("Limit", limit);
        parameters.Add("ContainsPattern", containsPattern);
        parameters.Add("PrefixPattern", prefixPattern);

        var unionSql = new StringBuilder();
        var hasAnyField = false;

        for (var index = 0; index < searchableFields.Count; index++)
        {
            var field = searchableFields[index];
            if (string.IsNullOrWhiteSpace(field))
            {
                continue;
            }

            var quotedBarcodeField = QuoteIdentifier(field);
            var isBarcodeField = normalizedBarcodeFields.Contains(field, StringComparer.OrdinalIgnoreCase);
            var candidateBarcodeExpression = isBarcodeField
                ? $"CAST(p.{quotedBarcodeField} AS NVARCHAR(100))"
                : preferredBarcodeExpression;

            if (hasAnyField)
            {
                unionSql.AppendLine("UNION ALL");
            }

            unionSql.AppendLine($"""
                SELECT
                    p.ptypeid AS ProductId,
                    p.{_productNameField} AS ProductName,
                    {productCodeExpression} AS ProductCode,
                    {productShortCodeExpression} AS ProductShortCode,
                    {specificationExpression} AS Specification,
                    {priceExpression} AS Price,
                    {candidateBarcodeExpression} AS Barcode,
                    @MatchedBy{index} AS BarcodeMatchedBy,
                    {index} AS FieldRank,
                    CASE
                        WHEN CAST(p.{quotedBarcodeField} AS NVARCHAR(100)) LIKE @PrefixPattern ESCAPE '\' THEN 0
                        ELSE 1
                    END AS MatchRank
                FROM {_quotedProductTable} AS p
                {priceJoin}
                WHERE CAST(p.{quotedBarcodeField} AS NVARCHAR(100)) LIKE @ContainsPattern ESCAPE '\'
                  AND NULLIF(LTRIM(RTRIM({candidateBarcodeExpression})), N'') IS NOT NULL
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
                    ProductCode,
                    ProductShortCode,
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
                ProductCode,
                ProductShortCode,
                Specification,
                Price,
                Barcode,
                BarcodeMatchedBy
            FROM dedup
            WHERE DedupRank = 1
            ORDER BY MatchRank, FieldRank, Barcode;
            """;

        await using var connection = new SqlConnection(_connectionString);
        var rows = await connection.QueryAsync<DbProductSearchRow>(CreateCommand(
            sql,
            parameters,
            cancellationToken));
        return rows.ToList();
    }

    private CommandDefinition CreateCommand(string sql, object? parameters, CancellationToken cancellationToken)
    {
        return new CommandDefinition(
            sql,
            parameters,
            commandTimeout: _queryTimeoutSeconds,
            cancellationToken: cancellationToken);
    }

    private static string BuildOptionalTextFieldExpression(
        IReadOnlySet<string> columns,
        string productAlias,
        string fieldName,
        int length)
    {
        if (!columns.Contains(fieldName))
        {
            return $"CAST(N'' AS NVARCHAR({length}))";
        }

        return $"CAST(ISNULL({productAlias}.{QuoteIdentifier(fieldName)}, N'') AS NVARCHAR({length}))";
    }

    private static string BuildPreferredBarcodeExpression(string productAlias, IReadOnlyList<string> barcodeFields)
    {
        if (barcodeFields.Count == 0)
        {
            return "CAST(N'' AS NVARCHAR(100))";
        }

        var candidates = barcodeFields
            .Select(field => $"NULLIF(CAST({productAlias}.{QuoteIdentifier(field)} AS NVARCHAR(100)), N'')")
            .ToList();

        return $"CAST(COALESCE({string.Join(", ", candidates)}, N'') AS NVARCHAR(100))";
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

    private string BuildBarcodeTablePriceExpression(string? priceField)
    {
        if (_useUnitScopedBarcodePrice && _quotedPriceTable is not null && _quotedPriceColumn is not null)
        {
            return "CAST(ISNULL(pr.Price, 0) AS DECIMAL(18, 2))";
        }

        return BuildPriceExpression(priceField);
    }

    private string BuildBarcodeTablePriceJoin(string productAlias, string barcodeAlias, string? priceField)
    {
        if (_useUnitScopedBarcodePrice && _quotedPriceTable is not null && _quotedPriceColumn is not null)
        {
            var orderBy = string.IsNullOrWhiteSpace(_preferredPriceTypeId)
                ? "CASE WHEN prRaw.PRTypeId = '0001' THEN 0 ELSE 1 END, prRaw.PRTypeId"
                : "CASE WHEN prRaw.PRTypeId = @PreferredPriceTypeId THEN 0 WHEN prRaw.PRTypeId = '0001' THEN 1 ELSE 2 END, prRaw.PRTypeId";

            return $"""
                OUTER APPLY (
                    SELECT TOP (1)
                        prRaw.{_quotedPriceColumn} AS Price
                    FROM {_quotedPriceTable} AS prRaw
                    WHERE prRaw.PTypeId = {productAlias}.ptypeid
                      AND prRaw.UnitID = {barcodeAlias}.UnitID
                    ORDER BY {orderBy}
                ) AS pr
                """;
        }

        return BuildPriceJoin(productAlias);
    }

    private static bool IsXwPtypePriceTable(string? tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            return false;
        }

        var normalized = tableName.Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal)
            .Trim();

        return normalized.Equals("xw_P_PtypePrice", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("dbo.xw_P_PtypePrice", StringComparison.OrdinalIgnoreCase);
    }

    private static string? BuildCompositeKeywordExpression(SwcsSchemaSnapshot schema)
    {
        var parts = new List<string>();
        AddCompositeField(parts, schema.Columns, "pusercode");
        AddCompositeField(parts, schema.Columns, "pfullname");
        AddCompositeField(parts, schema.Columns, "pnamepy");
        AddCompositeField(parts, schema.Columns, "Standard");
        AddCompositeField(parts, schema.Columns, "Type");
        AddCompositeField(parts, schema.Columns, "Area");

        if (schema.HasBarcodeFunction && schema.Columns.Contains("ptypeid"))
        {
            parts.Add("ISNULL(CAST(dbo.fn_strunitptype('B', p.[ptypeid], 0) AS NVARCHAR(4000)), N'')");
        }

        if (parts.Count == 0)
        {
            return null;
        }

        return string.Join(" + N'^^^' + ", parts);
    }

    private static void AddCompositeField(
        ICollection<string> parts,
        IReadOnlySet<string> columns,
        string fieldName)
    {
        if (!columns.Contains(fieldName))
        {
            return;
        }

        var quotedField = QuoteIdentifier(fieldName);
        parts.Add($"ISNULL(CAST(p.{quotedField} AS NVARCHAR(4000)), N'')");
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
