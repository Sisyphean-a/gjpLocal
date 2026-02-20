namespace SwcsScanner.Api.Security;

public sealed record TokenResult(string AccessToken, DateTime ExpiresAtUtc);
