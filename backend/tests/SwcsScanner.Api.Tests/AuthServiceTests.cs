using System.Security.Cryptography;
using System.Text;
using SwcsScanner.Api.Options;
using SwcsScanner.Api.Services;

namespace SwcsScanner.Api.Tests;

public sealed class AuthServiceTests
{
    [Fact]
    public void Authenticate_ShouldReturnUser_WhenPlainPasswordMatches()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AuthOptions
        {
            Users =
            [
                new AuthUserOption
                {
                    Username = "user01",
                    Password = "1234"
                }
            ]
        });

        var service = new AuthService(options);
        var result = service.Authenticate("user01", "1234");

        Assert.NotNull(result);
        Assert.Equal("user01", result!.Username);
    }

    [Fact]
    public void Authenticate_ShouldReturnUser_WhenSha256HashMatches()
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("1234"))).ToLowerInvariant();
        var options = Microsoft.Extensions.Options.Options.Create(new AuthOptions
        {
            Users =
            [
                new AuthUserOption
                {
                    Username = "user01",
                    PasswordHash = $"sha256:{hash}"
                }
            ]
        });

        var service = new AuthService(options);
        var result = service.Authenticate("user01", "1234");

        Assert.NotNull(result);
    }

    [Fact]
    public void Authenticate_ShouldReturnNull_WhenPasswordDoesNotMatch()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AuthOptions
        {
            Users =
            [
                new AuthUserOption
                {
                    Username = "user01",
                    Password = "1234"
                }
            ]
        });

        var service = new AuthService(options);
        var result = service.Authenticate("user01", "12345");

        Assert.Null(result);
    }
}
