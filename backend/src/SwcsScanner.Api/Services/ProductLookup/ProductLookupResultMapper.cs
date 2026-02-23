using SwcsScanner.Api.Data;

namespace SwcsScanner.Api.Services;

public sealed class ProductLookupResultMapper : IProductLookupResultMapper
{
    private readonly ISwcsProductLookupRepository _repository;

    public ProductLookupResultMapper(ISwcsProductLookupRepository repository)
    {
        _repository = repository;
    }

    public async Task<ProductLookupResult> MapAsync(
        DbProductLookupRow row,
        string matchedBy,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(row.ProductId))
        {
            return new ProductLookupResult(
                string.Empty,
                row.ProductName,
                row.ProductCode,
                row.ProductShortCode,
                row.Specification ?? string.Empty,
                row.Price,
                matchedBy,
                null,
                []);
        }

        var unitRows = await _repository.GetUnitsByProductIdAsync(
            row.ProductId,
            row.MatchedBarcode,
            cancellationToken);

        var units = unitRows
            .Select(unit => new ProductLookupUnitResult(
                unit.UnitId,
                unit.UnitName,
                unit.UnitRate,
                unit.Price,
                SplitBarcodes(unit.BarcodeList),
                unit.IsMatchedUnit))
            .ToList();

        var currentUnit = units.FirstOrDefault(unit => unit.IsMatchedUnit)
                          ?? units.FirstOrDefault(unit =>
                              !string.IsNullOrWhiteSpace(row.MatchedUnitId) &&
                              string.Equals(unit.UnitId, row.MatchedUnitId, StringComparison.OrdinalIgnoreCase))
                          ?? units.FirstOrDefault();

        var currentPrice = currentUnit?.Price ?? row.Price;

        return new ProductLookupResult(
            row.ProductId,
            row.ProductName,
            row.ProductCode,
            row.ProductShortCode,
            row.Specification ?? string.Empty,
            currentPrice,
            matchedBy,
            currentUnit,
            units);
    }

    private static IReadOnlyList<string> SplitBarcodes(string? barcodeList)
    {
        if (string.IsNullOrWhiteSpace(barcodeList))
        {
            return [];
        }

        return barcodeList
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
