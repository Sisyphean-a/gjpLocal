namespace SwcsScanner.Api.Services;

public sealed record ProductLookupResult(
    string ProductId,
    string ProductName,
    string ProductCode,
    string ProductShortCode,
    string Specification,
    decimal Price,
    string MatchedBy,
    ProductLookupUnitResult? CurrentUnit,
    IReadOnlyList<ProductLookupUnitResult> Units);
