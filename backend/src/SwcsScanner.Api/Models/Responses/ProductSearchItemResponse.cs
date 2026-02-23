namespace SwcsScanner.Api.Models.Responses;

public sealed record ProductSearchItemResponse(
    string ProductName,
    string ProductCode,
    string ProductShortCode,
    string Specification,
    decimal Price,
    string Barcode,
    string BarcodeMatchedBy);
