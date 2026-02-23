using Dapper;

namespace SwcsScanner.Api.Data;

public sealed class SwcsProductUnitReadRepository : IProductUnitReadRepository
{
    private readonly SwcsRepositoryContext _context;

    internal SwcsProductUnitReadRepository(SwcsRepositoryContext context)
    {
        _context = context;
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

        if (_context.QuotedBarcodeTable is null || _context.QuotedBarcodeColumn is null)
        {
            return [];
        }

        if (_context.QuotedPriceTable is null || _context.QuotedPriceColumn is null)
        {
            return [];
        }

        var orderBy = string.IsNullOrWhiteSpace(_context.PreferredPriceTypeId)
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
                        FROM {_context.QuotedBarcodeTable} AS m
                        WHERE m.PTypeId = u.PTypeId
                          AND m.UnitID = u.Ordid
                          AND m.{_context.QuotedBarcodeColumn} = @MatchedBarcode
                    ) THEN CAST(1 AS BIT)
                    ELSE CAST(0 AS BIT)
                END AS IsMatchedUnit
            FROM dbo.xw_PtypeUnit AS u
            OUTER APPLY (
                SELECT TOP (1)
                    prRaw.{_context.QuotedPriceColumn} AS Price
                FROM {_context.QuotedPriceTable} AS prRaw
                WHERE prRaw.PTypeId = u.PTypeId
                  AND prRaw.UnitID = u.Ordid
                ORDER BY {orderBy}
            ) AS pr
            OUTER APPLY (
                SELECT
                    STUFF((
                        SELECT N',' + CAST(bcRaw.{_context.QuotedBarcodeColumn} AS NVARCHAR(100))
                        FROM {_context.QuotedBarcodeTable} AS bcRaw
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

        await using var connection = _context.CreateConnection();
        var rows = await connection.QueryAsync<DbProductUnitRow>(_context.CreateCommand(
            sql,
            new
            {
                ProductId = productId,
                MatchedBarcode = matchedBarcode,
                PreferredPriceTypeId = _context.PreferredPriceTypeId
            },
            cancellationToken));
        return rows.ToList();
    }
}
