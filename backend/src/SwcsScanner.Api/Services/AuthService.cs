using System.Security.Cryptography;
using System.Text;
using BCrypt.Net;
using Microsoft.Extensions.Options;
using SwcsScanner.Api.Options;
using SwcsScanner.Api.Security;

namespace SwcsScanner.Api.Services;

public sealed class AuthService : IAuthService
{
    private readonly AuthOptions _options;

    public AuthService(IOptions<AuthOptions> options)
    {
        _options = options.Value;
    }

    public AuthenticatedUser? Authenticate(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var user = _options.Users.FirstOrDefault(candidate =>
            candidate.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

        if (user is null)
        {
            return null;
        }

        return VerifyPassword(user, password) ? new AuthenticatedUser(user.Username) : null;
    }

    private static bool VerifyPassword(AuthUserOption user, string password)
    {
        if (!string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            var hash = user.PasswordHash!;
            if (hash.StartsWith("$2", StringComparison.Ordinal))
            {
                return BCrypt.Net.BCrypt.Verify(password, hash);
            }

            if (hash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                var expectedHash = hash[7..];
                var currentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant();
                return FixedTimeEquals(expectedHash, currentHash);
            }
        }

        if (!string.IsNullOrWhiteSpace(user.Password))
        {
            return FixedTimeEquals(user.Password!, password);
        }

        return false;
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
               CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
