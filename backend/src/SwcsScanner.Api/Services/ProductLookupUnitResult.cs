namespace SwcsScanner.Api.Services;

public sealed record ProductLookupUnitResult(
    string UnitId,
    string UnitName,
    string UnitRate,
    decimal Price,
    IReadOnlyList<string> Barcodes,
    bool IsMatchedUnit);
