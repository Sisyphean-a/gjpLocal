namespace SwcsScanner.Api.Options;

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public List<string> AllowedOrigins { get; init; } = [];
}
