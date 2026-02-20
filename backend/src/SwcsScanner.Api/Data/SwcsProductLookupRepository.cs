using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SwcsScanner.Api.Options;

namespace SwcsScanner.Api.Data;

public sealed class SwcsProductLookupRepository : ISwcsProductLookupRepository
{
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
        _quotedBarcodeTable = string.IsNullOrEmpty(_options.BarcodeTable) ? null : QuoteTableName(_options.BarcodeTable);
        _quotedBarcodeColumn = string.IsNullOrEmpty(_options.BarcodeColumn) ? null : QuoteIdentifier(_options.BarcodeColumn);
        _quotedPriceTable = string.IsNullOrEmpty(_options.PriceTable) ? null : QuoteTableName(_options.PriceTable);
        _quotedPriceColumn = string.IsNullOrEmpty(_options.PriceColumn) ? null : QuoteIdentifier(_options.PriceColumn);
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

            _schemaExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, _options.SchemaCacheMinutes));
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
        string priceField,
        string specificationField,
        CancellationToken cancellationToken)
    {
        if (_quotedBarcodeTable is not null && _quotedBarcodeColumn is not null)
        {
            return await LookupFromBarcodeTableAsync(barcode, priceField, specificationField, cancellationToken);
        }

        if (string.IsNullOrEmpty(barcodeField))
        {
            return null;
        }

        var barcodeColumn = QuoteIdentifier(barcodeField);
        var priceColumn = QuoteIdentifier(priceField);
        var specificationColumn = QuoteIdentifier(specificationField);

        var sql = $"""
            SELECT TOP (1)
                p.{_productNameField} AS ProductName,
                CAST(p.{specificationColumn} AS NVARCHAR(200)) AS Specification,
                CAST(p.{priceColumn} AS DECIMAL(18, 2)) AS Price
            FROM {_quotedProductTable} AS p
            WHERE p.{barcodeColumn} = @Barcode;
            """;

        await using var connection = new SqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<DbProductLookupRow>(new CommandDefinition(
            sql,
            new { Barcode = barcode },
            cancellationToken: cancellationToken));
    }

    private async Task<DbProductLookupRow?> LookupFromBarcodeTableAsync(
        string barcode,
        string priceField,
        string specificationField,
        CancellationToken cancellationToken)
    {
        var specificationColumn = QuoteIdentifier(specificationField);

        string sql;
        if (_quotedPriceTable is not null && _quotedPriceColumn is not null)
        {
            sql = $"""
                SELECT TOP (1)
                    p.{_productNameField} AS ProductName,
                    CAST(p.{specificationColumn} AS NVARCHAR(200)) AS Specification,
                    CAST(pr.{_quotedPriceColumn} AS DECIMAL(18, 2)) AS Price
                FROM {_quotedBarcodeTable} AS bc
                INNER JOIN {_quotedProductTable} AS p ON bc.PTypeId = p.ptypeid
                INNER JOIN {_quotedPriceTable} AS pr ON p.ptypeid = pr.PTypeId
                WHERE bc.{_quotedBarcodeColumn} = @Barcode;
                """;
        }
        else
        {
            var priceColumn = QuoteIdentifier(priceField);
            sql = $"""
                SELECT TOP (1)
                    p.{_productNameField} AS ProductName,
                    CAST(p.{specificationColumn} AS NVARCHAR(200)) AS Specification,
                    CAST(p.{priceColumn} AS DECIMAL(18, 2)) AS Price
                FROM {_quotedBarcodeTable} AS bc
                INNER JOIN {_quotedProductTable} AS p ON bc.PTypeId = p.ptypeid
                WHERE bc.{_quotedBarcodeColumn} = @Barcode;
                """;
        }

        await using var connection = new SqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<DbProductLookupRow>(new CommandDefinition(
            sql,
            new { Barcode = barcode },
            cancellationToken: cancellationToken));
    }

    public async Task<DbProductLookupRow?> LookupByFunctionAsync(
        string barcode,
        string priceField,
        string specificationField,
        CancellationToken cancellationToken)
    {
        var priceColumn = QuoteIdentifier(priceField);
        var specificationColumn = QuoteIdentifier(specificationField);

        var sql = $"""
            SELECT TOP (1)
                p.{_productNameField} AS ProductName,
                CAST(p.{specificationColumn} AS NVARCHAR(200)) AS Specification,
                CAST(p.{priceColumn} AS DECIMAL(18, 2)) AS Price
            FROM {_quotedProductTable} AS p
            WHERE dbo.fn_strunitptype('B', p.ptypeid, 0) = @Barcode;
            """;

        await using var connection = new SqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<DbProductLookupRow>(new CommandDefinition(
            sql,
            new { Barcode = barcode },
            cancellationToken: cancellationToken));
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
