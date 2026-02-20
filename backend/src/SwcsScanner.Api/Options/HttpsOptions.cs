namespace SwcsScanner.Api.Options;

public sealed class HttpsOptions
{
    public const string SectionName = "Https";

    public int HttpsPort { get; init; } = 5001;

    public int? HttpPort { get; init; } = 5000;

    public string? PfxPath { get; init; }

    public string? PfxPassword { get; init; }
}
