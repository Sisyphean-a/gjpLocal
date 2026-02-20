namespace SwcsScanner.Api.Models.Responses;

public sealed record ProductLookupResponse(
    string ProductName,
    string Specification,
    decimal Price,
    string BarcodeMatchedBy);
