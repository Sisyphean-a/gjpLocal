namespace SwcsScanner.Api.Services;

public sealed record ProductLookupResult(
    string ProductName,
    string Specification,
    decimal Price,
    string BarcodeMatchedBy,
    ProductLookupUnitResult? CurrentUnit,
    IReadOnlyList<ProductLookupUnitResult> Units);
