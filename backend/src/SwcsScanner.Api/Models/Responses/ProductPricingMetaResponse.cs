namespace SwcsScanner.Api.Models.Responses;

public sealed record ProductPricingMetaResponse(
    string SourceTable,
    string SourceField,
    bool UnitScoped,
    string? PriceTypeId);
