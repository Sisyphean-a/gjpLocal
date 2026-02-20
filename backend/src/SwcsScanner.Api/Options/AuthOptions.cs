namespace SwcsScanner.Api.Options;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public List<AuthUserOption> Users { get; init; } = [];
}

public sealed class AuthUserOption
{
    public string Username { get; init; } = string.Empty;

    public string? Password { get; init; }

    public string? PasswordHash { get; init; }
}
