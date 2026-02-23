namespace SwcsScanner.Api.Services;

public sealed record ProductLookupResult(
    string ProductName,
    string ProductCode,
    string ProductShortCode,
    string Specification,
    decimal Price,
    string BarcodeMatchedBy,
    ProductLookupUnitResult? CurrentUnit,
    IReadOnlyList<ProductLookupUnitResult> Units);
