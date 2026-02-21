namespace SwcsScanner.Api.Services;

public sealed record ProductSearchItemResult(
    string ProductName,
    string Specification,
    decimal Price,
    string Barcode,
    string BarcodeMatchedBy);
