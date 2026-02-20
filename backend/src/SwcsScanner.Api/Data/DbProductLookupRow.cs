namespace SwcsScanner.Api.Data;

public sealed class DbProductLookupRow
{
    public string ProductName { get; init; } = string.Empty;

    public string? Specification { get; init; }

    public decimal Price { get; init; }
}
