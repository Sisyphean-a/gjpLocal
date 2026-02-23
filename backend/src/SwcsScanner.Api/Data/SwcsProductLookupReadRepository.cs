using Dapper;

namespace SwcsScanner.Api.Data;

public sealed class SwcsProductLookupReadRepository : IProductLookupReadRepository
{
    private readonly SwcsRepositoryContext _context;
    private readonly ISchemaRepository _schemaRepository;

    internal SwcsProductLookupReadRepository(
        SwcsRepositoryContext context,
        ISchemaRepository schemaRepository)
    {
        _context = context;
        _schemaRepository = schemaRepository;
    }

    public async Task<DbProductLookupRow?> LookupByFieldAsync(
        string barcode,
        string barcodeField,
        string? priceField,
        string? specificationField,
        CancellationToken cancellationToken)
    {
        if (_context.QuotedBarcodeTable is not null && _context.QuotedBarcodeColumn is not null)
        {
            return await LookupFromBarcodeTableAsync(barcode, priceField, specificationField, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(barcodeField))
        {
            return null;
        }

        var schema = await _schemaRepository.GetSchemaSnapshotAsync(cancellationToken);
        var barcodeColumn = SwcsSqlHelper.QuoteIdentifier(barcodeField);
        var specificationExpression = SwcsSqlHelper.BuildSpecificationExpression(specificationField);
        var priceExpression = SwcsSqlHelper.BuildPriceExpression(_context, priceField);
        var priceJoin = SwcsSqlHelper.BuildPriceJoin(_context, "p");
        var productCodeExpression = SwcsSqlHelper.BuildOptionalTextFieldExpression(
            schema.Columns,
            "p",
            SwcsSqlHelper.ProductCodeFieldName,
            100);
        var productShortCodeExpression = SwcsSqlHelper.BuildOptionalTextFieldExpression(
            schema.Columns,
            "p",
            SwcsSqlHelper.ProductShortCodeFieldName,
            100);

        var sql = $"""
            SELECT TOP (1)
                p.ptypeid AS ProductId,
                p.{_context.ProductNameField} AS ProductName,
                {productCodeExpression} AS ProductCode,
                {productShortCodeExpression} AS ProductShortCode,
                {specificationExpression} AS Specification,
                {priceExpression} AS Price,
                CAST(NULL AS NVARCHAR(50)) AS MatchedUnitId,
                CAST(@Barcode AS NVARCHAR(100)) AS MatchedBarcode
            FROM {_context.QuotedProductTable} AS p
            {priceJoin}
            WHERE p.{barcodeColumn} = @Barcode;
            """;

        await using var connection = _context.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<DbProductLookupRow>(_context.CreateCommand(
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
        var schema = await _schemaRepository.GetSchemaSnapshotAsync(cancellationToken);
        var specificationExpression = SwcsSqlHelper.BuildSpecificationExpression(specificationField);
        var priceExpression = SwcsSqlHelper.BuildPriceExpression(_context, priceField);
        var priceJoin = SwcsSqlHelper.BuildPriceJoin(_context, "p");
        var productCodeExpression = SwcsSqlHelper.BuildOptionalTextFieldExpression(
            schema.Columns,
            "p",
            SwcsSqlHelper.ProductCodeFieldName,
            100);
        var productShortCodeExpression = SwcsSqlHelper.BuildOptionalTextFieldExpression(
            schema.Columns,
            "p",
            SwcsSqlHelper.ProductShortCodeFieldName,
            100);

        var sql = $"""
            SELECT TOP (1)
                p.ptypeid AS ProductId,
                p.{_context.ProductNameField} AS ProductName,
                {productCodeExpression} AS ProductCode,
                {productShortCodeExpression} AS ProductShortCode,
                {specificationExpression} AS Specification,
                {priceExpression} AS Price,
                CAST(NULL AS NVARCHAR(50)) AS MatchedUnitId,
                CAST(@Barcode AS NVARCHAR(100)) AS MatchedBarcode
            FROM {_context.QuotedProductTable} AS p
            {priceJoin}
            WHERE dbo.fn_strunitptype('B', p.ptypeid, 0) = @Barcode;
            """;

        await using var connection = _context.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<DbProductLookupRow>(_context.CreateCommand(
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

        var schema = await _schemaRepository.GetSchemaSnapshotAsync(cancellationToken);
        var compositeExpression = SwcsSqlHelper.BuildCompositeKeywordExpression(schema);
        if (compositeExpression is null)
        {
            return null;
        }

        var containsPattern = SwcsSqlHelper.BuildContainsPattern(keyword);
        var prefixPattern = SwcsSqlHelper.BuildPrefixPattern(keyword);
        var specificationExpression = SwcsSqlHelper.BuildSpecificationExpression(specificationField);
        var priceExpression = SwcsSqlHelper.BuildPriceExpression(_context, priceField);
        var priceJoin = SwcsSqlHelper.BuildPriceJoin(_context, "p");
        var productCodeExpression = SwcsSqlHelper.BuildOptionalTextFieldExpression(
            schema.Columns,
            "p",
            SwcsSqlHelper.ProductCodeFieldName,
            100);
        var productShortCodeExpression = SwcsSqlHelper.BuildOptionalTextFieldExpression(
            schema.Columns,
            "p",
            SwcsSqlHelper.ProductShortCodeFieldName,
            100);
        var orderByExpression = schema.Columns.Contains("ptypeid")
            ? "p.[ptypeid]"
            : $"p.{_context.ProductNameField}";

        var sql = $"""
            SELECT TOP (1)
                p.ptypeid AS ProductId,
                p.{_context.ProductNameField} AS ProductName,
                {productCodeExpression} AS ProductCode,
                {productShortCodeExpression} AS ProductShortCode,
                {specificationExpression} AS Specification,
                {priceExpression} AS Price,
                CAST(NULL AS NVARCHAR(50)) AS MatchedUnitId,
                CAST(@ContainsKeyword AS NVARCHAR(100)) AS MatchedBarcode
            FROM {_context.QuotedProductTable} AS p
            {priceJoin}
            WHERE ({compositeExpression}) LIKE @ContainsPattern ESCAPE '\'
            ORDER BY
                CASE
                    WHEN ({compositeExpression}) LIKE @PrefixPattern ESCAPE '\' THEN 0
                    ELSE 1
                END,
                {orderByExpression};
            """;

        await using var connection = _context.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<DbProductLookupRow>(_context.CreateCommand(
            sql,
            new
            {
                ContainsKeyword = keyword,
                ContainsPattern = containsPattern,
                PrefixPattern = prefixPattern
            },
            cancellationToken));
    }

    private async Task<DbProductLookupRow?> LookupFromBarcodeTableAsync(
        string barcode,
        string? priceField,
        string? specificationField,
        CancellationToken cancellationToken)
    {
        var schema = await _schemaRepository.GetSchemaSnapshotAsync(cancellationToken);
        var specificationExpression = SwcsSqlHelper.BuildSpecificationExpression(specificationField);
        var priceExpression = SwcsSqlHelper.BuildBarcodeTablePriceExpression(_context, priceField);
        var priceJoin = SwcsSqlHelper.BuildBarcodeTablePriceJoin(_context, "p", "bc");
        var productCodeExpression = SwcsSqlHelper.BuildOptionalTextFieldExpression(
            schema.Columns,
            "p",
            SwcsSqlHelper.ProductCodeFieldName,
            100);
        var productShortCodeExpression = SwcsSqlHelper.BuildOptionalTextFieldExpression(
            schema.Columns,
            "p",
            SwcsSqlHelper.ProductShortCodeFieldName,
            100);

        var sql = $"""
            SELECT TOP (1)
                p.ptypeid AS ProductId,
                p.{_context.ProductNameField} AS ProductName,
                {productCodeExpression} AS ProductCode,
                {productShortCodeExpression} AS ProductShortCode,
                {specificationExpression} AS Specification,
                {priceExpression} AS Price,
                CAST(bc.UnitID AS NVARCHAR(50)) AS MatchedUnitId,
                CAST(bc.{_context.QuotedBarcodeColumn} AS NVARCHAR(100)) AS MatchedBarcode
            FROM {_context.QuotedBarcodeTable} AS bc
            INNER JOIN {_context.QuotedProductTable} AS p ON bc.PTypeId = p.ptypeid
            {priceJoin}
            WHERE bc.{_context.QuotedBarcodeColumn} = @Barcode;
            """;

        await using var connection = _context.CreateConnection();
        return await connection.QueryFirstOrDefaultAsync<DbProductLookupRow>(_context.CreateCommand(
            sql,
            new
            {
                Barcode = barcode,
                PreferredPriceTypeId = _context.PreferredPriceTypeId
            },
            cancellationToken));
    }
}
