using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using SwcsScanner.Api.Options;

namespace SwcsScanner.Api.Security;

public static class SecurityConfigurationValidator
{
    public const string JwtPlaceholderKey = "PLEASE_SET_JWT_KEY_IN_ENVIRONMENT";

    private static readonly HashSet<string> InsecurePasswords =
    [
        "1234",
        "123456",
        "12345678",
        "password",
        "admin",
        "admin123",
        "qwerty"
    ];

    public static void Validate(
        IHostEnvironment environment,
        JwtOptions jwtOptions,
        AuthOptions authOptions,
        CorsOptions corsOptions)
    {
        if (string.IsNullOrWhiteSpace(jwtOptions.Key) || jwtOptions.Key.Length < 32)
        {
            throw new InvalidOperationException("Jwt:Key 长度至少 32 位。");
        }

        if (IsPlaceholderJwtKey(jwtOptions.Key))
        {
            throw new InvalidOperationException("Jwt:Key 仍为占位值，请通过安全配置注入真实密钥。");
        }

        if (environment.IsDevelopment())
        {
            return;
        }

        var allowedOrigins = corsOptions.AllowedOrigins
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (allowedOrigins.Count == 0)
        {
            throw new InvalidOperationException("生产环境必须配置 Cors:AllowedOrigins。");
        }

        var users = authOptions.Users ?? [];
        if (users.Count == 0)
        {
            throw new InvalidOperationException("生产环境至少配置一个 Auth:Users 账号。");
        }

        foreach (var user in users)
        {
            var username = user.Username?.Trim() ?? string.Empty;
            if (username.Length == 0)
            {
                throw new InvalidOperationException("Auth:Users 中存在空用户名配置。");
            }

            var plainPassword = user.Password?.Trim();
            if (!string.IsNullOrWhiteSpace(plainPassword))
            {
                throw new InvalidOperationException($"生产环境禁止明文密码配置: {username}。");
            }

            var passwordHash = user.PasswordHash?.Trim();
            if (string.IsNullOrWhiteSpace(passwordHash))
            {
                throw new InvalidOperationException($"生产环境账号缺少密码哈希配置: {username}。");
            }

            if (LooksLikePlaceholderHash(passwordHash) || IsHashOfInsecurePassword(passwordHash))
            {
                throw new InvalidOperationException($"生产环境账号使用了弱口令或占位哈希: {username}。");
            }
        }
    }

    private static bool IsPlaceholderJwtKey(string key)
    {
        return key.Equals(JwtPlaceholderKey, StringComparison.Ordinal) ||
               key.StartsWith("PleaseReplace", StringComparison.OrdinalIgnoreCase) ||
               key.StartsWith("PLEASE_SET", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikePlaceholderHash(string passwordHash)
    {
        return passwordHash.Contains("replace", StringComparison.OrdinalIgnoreCase) ||
               passwordHash.Contains("placeholder", StringComparison.OrdinalIgnoreCase) ||
               passwordHash.EndsWith(":");
    }

    private static bool IsHashOfInsecurePassword(string passwordHash)
    {
        if (passwordHash.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            var hashBody = passwordHash[7..];
            foreach (var password in InsecurePasswords)
            {
                var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))).ToLowerInvariant();
                if (hashBody.Equals(expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
