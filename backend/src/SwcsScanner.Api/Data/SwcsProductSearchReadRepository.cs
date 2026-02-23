using System.Text;
using Dapper;

namespace SwcsScanner.Api.Data;

public sealed class SwcsProductSearchReadRepository : IProductSearchReadRepository
{
    private readonly SwcsRepositoryContext _context;
    private readonly ISchemaRepository _schemaRepository;

    internal SwcsProductSearchReadRepository(
        SwcsRepositoryContext context,
        ISchemaRepository schemaRepository)
    {
        _context = context;
        _schemaRepository = schemaRepository;
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

        if (_context.QuotedBarcodeTable is not null && _context.QuotedBarcodeColumn is not null)
        {
            return await SearchFromBarcodeTableAsync(keyword, priceField, specificationField, limit, cancellationToken);
        }

        if (barcodeFields.Count == 0)
        {
            return [];
        }

        return await SearchFromProductTableAsync(keyword, barcodeFields, priceField, specificationField, limit, cancellationToken);
    }

    private async Task<IReadOnlyList<DbProductSearchRow>> SearchFromBarcodeTableAsync(
        string keyword,
        string? priceField,
        string? specificationField,
        int limit,
        CancellationToken cancellationToken)
    {
        var schema = await _schemaRepository.GetSchemaSnapshotAsync(cancellationToken);
        var specificationExpression = SwcsSqlHelper.BuildSpecificationExpression(specificationField);
        var priceExpression = SwcsSqlHelper.BuildBarcodeTablePriceExpression(_context, priceField);
        var priceJoin = SwcsSqlHelper.BuildBarcodeTablePriceJoin(_context, "p", "bc");
        var containsPattern = SwcsSqlHelper.BuildContainsPattern(keyword);
        var prefixPattern = SwcsSqlHelper.BuildPrefixPattern(keyword);
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
        var barcodeExpression = $"CAST(bc.{_context.QuotedBarcodeColumn} AS NVARCHAR(100))";
        var productNameExpression = $"CAST(p.{_context.ProductNameField} AS NVARCHAR(200))";

        var searchTargets = new List<(string Expression, string MatchedBy)>
        {
            (
                barcodeExpression,
                string.IsNullOrWhiteSpace(_context.Options.BarcodeColumn)
                    ? "BarcodeTable"
                    : _context.Options.BarcodeColumn!
            )
        };

        if (schema.Columns.Contains(SwcsSqlHelper.ProductShortCodeFieldName))
        {
            searchTargets.Add((
                $"CAST(p.{SwcsSqlHelper.QuoteIdentifier(SwcsSqlHelper.ProductShortCodeFieldName)} AS NVARCHAR(100))",
                SwcsSqlHelper.ProductShortCodeFieldName));
        }

        if (schema.Columns.Contains(SwcsSqlHelper.ProductCodeFieldName))
        {
            searchTargets.Add((
                $"CAST(p.{SwcsSqlHelper.QuoteIdentifier(SwcsSqlHelper.ProductCodeFieldName)} AS NVARCHAR(100))",
                SwcsSqlHelper.ProductCodeFieldName));
        }

        searchTargets.Add((productNameExpression, _context.Options.ProductNameField));

        var whereClause = string.Join(
            $"{Environment.NewLine}                   OR ",
            searchTargets.Select(target => $"{target.Expression} LIKE @ContainsPattern ESCAPE '\\'"));

        var matchRankExpressionBuilder = new StringBuilder();
        matchRankExpressionBuilder.AppendLine("CASE");
        for (var index = 0; index < searchTargets.Count; index++)
        {
            matchRankExpressionBuilder.AppendLine(
                $"                        WHEN {searchTargets[index].Expression} LIKE @PrefixPattern ESCAPE '\\' THEN {index}");
        }

        matchRankExpressionBuilder.AppendLine($"                        ELSE {searchTargets.Count}");
        matchRankExpressionBuilder.Append("                    END");
        var matchRankExpression = matchRankExpressionBuilder.ToString();

        var matchedByExpressionBuilder = new StringBuilder();
        matchedByExpressionBuilder.AppendLine("CASE");
        for (var index = 0; index < searchTargets.Count; index++)
        {
            matchedByExpressionBuilder.AppendLine(
                $"                        WHEN {searchTargets[index].Expression} LIKE @PrefixPattern ESCAPE '\\' THEN @MatchedBy{index}");
        }

        matchedByExpressionBuilder.AppendLine("                        ELSE @MatchedBy0");
        matchedByExpressionBuilder.Append("                    END");
        var matchedByExpression = matchedByExpressionBuilder.ToString();

        var parameters = new DynamicParameters();
        parameters.Add("Limit", limit);
        parameters.Add("ContainsPattern", containsPattern);
        parameters.Add("PrefixPattern", prefixPattern);
        parameters.Add("PreferredPriceTypeId", _context.PreferredPriceTypeId);

        for (var index = 0; index < searchTargets.Count; index++)
        {
            parameters.Add($"MatchedBy{index}", searchTargets[index].MatchedBy);
        }

        var sql = $"""
            WITH candidates AS (
                SELECT
                    p.ptypeid AS ProductId,
                    p.{_context.ProductNameField} AS ProductName,
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
                FROM {_context.QuotedBarcodeTable} AS bc
                INNER JOIN {_context.QuotedProductTable} AS p ON bc.PTypeId = p.ptypeid
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

        await using var connection = _context.CreateConnection();
        var rows = await connection.QueryAsync<DbProductSearchRow>(_context.CreateCommand(
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
        var schema = await _schemaRepository.GetSchemaSnapshotAsync(cancellationToken);
        var specificationExpression = SwcsSqlHelper.BuildSpecificationExpression(specificationField);
        var priceExpression = SwcsSqlHelper.BuildPriceExpression(_context, priceField);
        var priceJoin = SwcsSqlHelper.BuildPriceJoin(_context, "p");
        var containsPattern = SwcsSqlHelper.BuildContainsPattern(keyword);
        var prefixPattern = SwcsSqlHelper.BuildPrefixPattern(keyword);
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

        var normalizedBarcodeFields = barcodeFields
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedBarcodeFields.Count == 0)
        {
            return [];
        }

        var preferredBarcodeExpression = SwcsSqlHelper.BuildPreferredBarcodeExpression("p", normalizedBarcodeFields);
        var searchableFields = new List<string>(normalizedBarcodeFields);

        if (schema.Columns.Contains(SwcsSqlHelper.ProductShortCodeFieldName))
        {
            searchableFields.Add(SwcsSqlHelper.ProductShortCodeFieldName);
        }

        if (schema.Columns.Contains(SwcsSqlHelper.ProductCodeFieldName))
        {
            searchableFields.Add(SwcsSqlHelper.ProductCodeFieldName);
        }

        if (schema.Columns.Contains(_context.Options.ProductNameField))
        {
            searchableFields.Add(_context.Options.ProductNameField);
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

            var quotedBarcodeField = SwcsSqlHelper.QuoteIdentifier(field);
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
                    p.{_context.ProductNameField} AS ProductName,
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
                FROM {_context.QuotedProductTable} AS p
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

        await using var connection = _context.CreateConnection();
        var rows = await connection.QueryAsync<DbProductSearchRow>(_context.CreateCommand(
            sql,
            parameters,
            cancellationToken));
        return rows.ToList();
    }
}
