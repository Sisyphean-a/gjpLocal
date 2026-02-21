namespace SwcsScanner.Api.Options;

public sealed class SwcsOptions
{
    public const string SectionName = "Swcs";

    public string ProductTable { get; init; } = "dbo.Ptype";

    public string? BarcodeTable { get; init; }

    public string? BarcodeColumn { get; init; }

    public string? PriceTable { get; init; }

    public string? PriceColumn { get; init; }

    public string? PriceTypeId { get; init; } = "0001";

    public string ProductNameField { get; init; } = "FullName";

    public string SpecificationField { get; init; } = "Standard";

    public List<string> BarcodeFields { get; init; } = ["Standard", "Barcode"];

    public List<string> PriceFields { get; init; } = ["RetailPrice", "Price1", "Price"];

    public bool EnableFunctionFallback { get; init; } = true;

    public int SchemaCacheMinutes { get; init; } = 10;
}
