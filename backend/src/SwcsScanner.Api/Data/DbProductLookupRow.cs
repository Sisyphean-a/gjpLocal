namespace SwcsScanner.Api.Data;

public sealed class DbProductLookupRow
{
    public string ProductId { get; init; } = string.Empty;

    public string ProductName { get; init; } = string.Empty;

    public string? Specification { get; init; }

    public decimal Price { get; init; }

    public string? MatchedUnitId { get; init; }

    public string? MatchedBarcode { get; init; }
}
