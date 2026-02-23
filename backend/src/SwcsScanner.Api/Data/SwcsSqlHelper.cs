using System.Text.RegularExpressions;

namespace SwcsScanner.Api.Data;

internal static class SwcsSqlHelper
{
    public const string ProductCodeFieldName = "pusercode";
    public const string ProductShortCodeFieldName = "pnamepy";

    private static readonly Regex IdentifierRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public static string BuildOptionalTextFieldExpression(
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

    public static string BuildPreferredBarcodeExpression(string productAlias, IReadOnlyList<string> barcodeFields)
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

    public static string BuildSpecificationExpression(string? specificationField)
    {
        if (string.IsNullOrWhiteSpace(specificationField))
        {
            return "CAST(NULL AS NVARCHAR(200))";
        }

        return $"CAST(p.{QuoteIdentifier(specificationField)} AS NVARCHAR(200))";
    }

    public static string BuildPriceExpression(SwcsRepositoryContext context, string? priceField)
    {
        if (context.QuotedPriceTable is not null && context.QuotedPriceColumn is not null)
        {
            return $"CAST(pr.{context.QuotedPriceColumn} AS DECIMAL(18, 2))";
        }

        if (string.IsNullOrWhiteSpace(priceField))
        {
            throw new InvalidOperationException("未指定可用价格字段。");
        }

        return $"CAST(p.{QuoteIdentifier(priceField)} AS DECIMAL(18, 2))";
    }

    public static string BuildPriceJoin(SwcsRepositoryContext context, string productAlias)
    {
        if (context.QuotedPriceTable is null || context.QuotedPriceColumn is null)
        {
            return string.Empty;
        }

        return $"INNER JOIN {context.QuotedPriceTable} AS pr ON {productAlias}.ptypeid = pr.PTypeId";
    }

    public static string BuildBarcodeTablePriceExpression(SwcsRepositoryContext context, string? priceField)
    {
        if (context.UseUnitScopedBarcodePrice && context.QuotedPriceTable is not null && context.QuotedPriceColumn is not null)
        {
            return "CAST(ISNULL(pr.Price, 0) AS DECIMAL(18, 2))";
        }

        return BuildPriceExpression(context, priceField);
    }

    public static string BuildBarcodeTablePriceJoin(
        SwcsRepositoryContext context,
        string productAlias,
        string barcodeAlias)
    {
        if (context.UseUnitScopedBarcodePrice && context.QuotedPriceTable is not null && context.QuotedPriceColumn is not null)
        {
            var orderBy = string.IsNullOrWhiteSpace(context.PreferredPriceTypeId)
                ? "CASE WHEN prRaw.PRTypeId = '0001' THEN 0 ELSE 1 END, prRaw.PRTypeId"
                : "CASE WHEN prRaw.PRTypeId = @PreferredPriceTypeId THEN 0 WHEN prRaw.PRTypeId = '0001' THEN 1 ELSE 2 END, prRaw.PRTypeId";

            return $"""
                OUTER APPLY (
                    SELECT TOP (1)
                        prRaw.{context.QuotedPriceColumn} AS Price
                    FROM {context.QuotedPriceTable} AS prRaw
                    WHERE prRaw.PTypeId = {productAlias}.ptypeid
                      AND prRaw.UnitID = {barcodeAlias}.UnitID
                    ORDER BY {orderBy}
                ) AS pr
                """;
        }

        return BuildPriceJoin(context, productAlias);
    }

    public static bool IsXwPtypePriceTable(string? tableName)
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

    public static string? BuildCompositeKeywordExpression(SwcsSchemaSnapshot schema)
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

    public static string BuildContainsPattern(string keyword)
    {
        return $"%{EscapeLikeValue(keyword)}%";
    }

    public static string BuildPrefixPattern(string keyword)
    {
        return $"{EscapeLikeValue(keyword)}%";
    }

    public static string QuoteTableName(string tableName)
    {
        var parts = tableName.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 2)
        {
            throw new InvalidOperationException($"表名配置不合法: {tableName}");
        }

        return string.Join(".", parts.Select(QuoteIdentifier));
    }

    public static string QuoteIdentifier(string identifier)
    {
        if (!IdentifierRegex.IsMatch(identifier))
        {
            throw new InvalidOperationException($"字段名不合法: {identifier}");
        }

        return $"[{identifier}]";
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

    private static string EscapeLikeValue(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal);
    }
}
