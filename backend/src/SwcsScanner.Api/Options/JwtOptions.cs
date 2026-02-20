namespace SwcsScanner.Api.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "SwcsScanner";

    public string Audience { get; init; } = "SwcsScannerClient";

    public string Key { get; init; } = "ChangeThisKeyToA32ByteLongRandomSecret!";

    public int ExpireHours { get; init; } = 12;
}
