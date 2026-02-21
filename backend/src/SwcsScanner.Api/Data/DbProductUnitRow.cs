namespace SwcsScanner.Api.Data;

public sealed class DbProductUnitRow
{
    public string UnitId { get; init; } = string.Empty;

    public string UnitName { get; init; } = string.Empty;

    public string UnitRate { get; init; } = string.Empty;

    public decimal Price { get; init; }

    public string BarcodeList { get; init; } = string.Empty;

    public bool IsMatchedUnit { get; init; }
}
