namespace SwcsScanner.Api.Models.Responses;

public sealed record ProductLookupResponse(
    string ProductName,
    string ProductCode,
    string ProductShortCode,
    string Specification,
    decimal Price,
    string BarcodeMatchedBy,
    ProductLookupUnitResponse? CurrentUnit,
    IReadOnlyList<ProductLookupUnitResponse> Units);
