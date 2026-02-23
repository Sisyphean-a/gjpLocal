using SwcsScanner.Api.Data;

namespace SwcsScanner.Api.Services;

public sealed record ProductLookupContext(
    SwcsSchemaSnapshot Schema,
    string? PriceField,
    string? SpecificationField,
    IReadOnlyList<string> AvailableBarcodeFields,
    bool UseBarcodeTable,
    bool CanUseFunctionFallback);
