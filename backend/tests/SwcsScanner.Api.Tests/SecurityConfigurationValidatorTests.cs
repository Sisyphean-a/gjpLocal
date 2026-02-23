using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SwcsScanner.Api.Options;
using SwcsScanner.Api.Security;

namespace SwcsScanner.Api.Tests;

public sealed class SecurityConfigurationValidatorTests
{
    [Fact]
    public void Validate_ShouldThrow_WhenJwtKeyIsPlaceholder()
    {
        var environment = CreateEnvironment(Environments.Development);
        var jwtOptions = new JwtOptions
        {
            Key = SecurityConfigurationValidator.JwtPlaceholderKey
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SecurityConfigurationValidator.Validate(
                environment,
                jwtOptions,
                new AuthOptions(),
                new CorsOptions()));

        Assert.Contains("Jwt:Key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ShouldAllowDevelopment_WhenJwtKeyIsSecure()
    {
        var environment = CreateEnvironment(Environments.Development);
        var jwtOptions = new JwtOptions
        {
            Key = "DevOnlyJwtKey_ChangeWhenTeamShared_20260223"
        };

        SecurityConfigurationValidator.Validate(
            environment,
            jwtOptions,
            new AuthOptions(),
            new CorsOptions());
    }

    [Fact]
    public void Validate_ShouldThrowInProduction_WhenCorsOriginsEmpty()
    {
        var environment = CreateEnvironment(Environments.Production);
        var jwtOptions = new JwtOptions
        {
            Key = "ProdJwtKey_AtLeast32Chars_AndRandom_0001"
        };
        var authOptions = new AuthOptions
        {
            Users =
            [
                new AuthUserOption
                {
                    Username = "user01",
                    PasswordHash = BuildSha256Hash("Complex#Pass2026")
                }
            ]
        };

        Assert.Throws<InvalidOperationException>(() =>
            SecurityConfigurationValidator.Validate(
                environment,
                jwtOptions,
                authOptions,
                new CorsOptions()));
    }

    [Fact]
    public void Validate_ShouldThrowInProduction_WhenPlainPasswordConfigured()
    {
        var environment = CreateEnvironment(Environments.Production);
        var jwtOptions = new JwtOptions
        {
            Key = "ProdJwtKey_AtLeast32Chars_AndRandom_0002"
        };
        var authOptions = new AuthOptions
        {
            Users =
            [
                new AuthUserOption
                {
                    Username = "user01",
                    Password = "StrongPass!2026"
                }
            ]
        };
        var corsOptions = new CorsOptions
        {
            AllowedOrigins = ["https://localhost:5001"]
        };

        Assert.Throws<InvalidOperationException>(() =>
            SecurityConfigurationValidator.Validate(
                environment,
                jwtOptions,
                authOptions,
                corsOptions));
    }

    [Fact]
    public void Validate_ShouldThrowInProduction_WhenWeakHashConfigured()
    {
        var environment = CreateEnvironment(Environments.Production);
        var jwtOptions = new JwtOptions
        {
            Key = "ProdJwtKey_AtLeast32Chars_AndRandom_0003"
        };
        var authOptions = new AuthOptions
        {
            Users =
            [
                new AuthUserOption
                {
                    Username = "user01",
                    PasswordHash = BuildSha256Hash("1234")
                }
            ]
        };
        var corsOptions = new CorsOptions
        {
            AllowedOrigins = ["https://localhost:5001"]
        };

        Assert.Throws<InvalidOperationException>(() =>
            SecurityConfigurationValidator.Validate(
                environment,
                jwtOptions,
                authOptions,
                corsOptions));
    }

    [Fact]
    public void Validate_ShouldPassInProduction_WhenSecurityConfigIsValid()
    {
        var environment = CreateEnvironment(Environments.Production);
        var jwtOptions = new JwtOptions
        {
            Key = "ProdJwtKey_AtLeast32Chars_AndRandom_0004"
        };
        var authOptions = new AuthOptions
        {
            Users =
            [
                new AuthUserOption
                {
                    Username = "user01",
                    PasswordHash = BuildSha256Hash("Complex#Pass2026")
                }
            ]
        };
        var corsOptions = new CorsOptions
        {
            AllowedOrigins = ["https://localhost:5001"]
        };

        SecurityConfigurationValidator.Validate(
            environment,
            jwtOptions,
            authOptions,
            corsOptions);
    }

    private static string BuildSha256Hash(string password)
    {
        return $"sha256:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(password))).ToLowerInvariant()}";
    }

    private static IHostEnvironment CreateEnvironment(string environmentName)
    {
        return new FakeHostEnvironment(environmentName);
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
            ApplicationName = "SwcsScanner.Api.Tests";
            ContentRootPath = AppContext.BaseDirectory;
            ContentRootFileProvider = new NullFileProvider();
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; }

        public string ContentRootPath { get; set; }

        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
