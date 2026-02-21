namespace SwcsScanner.Api.Data;

public sealed class DbProductSearchRow
{
    public string ProductName { get; init; } = string.Empty;

    public string? Specification { get; init; }

    public decimal Price { get; init; }

    public string Barcode { get; init; } = string.Empty;

    public string BarcodeMatchedBy { get; init; } = string.Empty;
}
