namespace SwcsScanner.Api.Models.Responses;

public sealed record LoginResponse(string AccessToken, string TokenType, DateTime ExpiresAtUtc);
