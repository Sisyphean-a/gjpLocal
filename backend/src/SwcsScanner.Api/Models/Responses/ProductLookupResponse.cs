namespace SwcsScanner.Api.Models.Responses;

public sealed record ProductLookupResponse(
    string ProductId,
    string ProductName,
    string ProductCode,
    string ProductShortCode,
    string Specification,
    decimal Price,
    string MatchedBy,
    ProductPricingMetaResponse PricingMeta,
    ProductLookupUnitResponse? CurrentUnit,
    IReadOnlyList<ProductLookupUnitResponse> Units);
