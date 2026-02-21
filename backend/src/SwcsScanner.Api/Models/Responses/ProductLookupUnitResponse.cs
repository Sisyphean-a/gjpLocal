namespace SwcsScanner.Api.Models.Responses;

public sealed record ProductLookupUnitResponse(
    string UnitId,
    string UnitName,
    string UnitRate,
    decimal Price,
    IReadOnlyList<string> Barcodes,
    bool IsMatchedUnit);
