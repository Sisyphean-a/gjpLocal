namespace SwcsScanner.Api.Options;

public sealed class SwcsOptions
{
    public const string SectionName = "Swcs";

    public string ProductTable { get; init; } = "dbo.Ptype";

    public string SpecificationField { get; init; } = "Standard";

    public List<string> BarcodeFields { get; init; } = ["Standard", "Barcode"];

    public List<string> PriceFields { get; init; } = ["RetailPrice", "Price1", "Price"];

    public bool EnableFunctionFallback { get; init; } = true;

    public int SchemaCacheMinutes { get; init; } = 10;
}
