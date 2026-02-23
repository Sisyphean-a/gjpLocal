using SwcsScanner.Api.Options;

namespace SwcsScanner.Api.Tests;

public sealed class AuthOptionsValidatorTests
{
    private readonly AuthOptionsValidator _validator = new();

    [Fact]
    public void Validate_ShouldFail_WhenCredentialMissing()
    {
        var options = new AuthOptions
        {
            Users =
            [
                new AuthUserOption
                {
                    Username = "user01"
                }
            ]
        };

        var result = _validator.Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains("Password", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ShouldFail_WhenDuplicateUsernamesExist()
    {
        var options = new AuthOptions
        {
            Users =
            [
                new AuthUserOption
                {
                    Username = "user01",
                    Password = "x"
                },
                new AuthUserOption
                {
                    Username = "USER01",
                    PasswordHash = "sha256:abc"
                }
            ]
        };

        var result = _validator.Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains("重复用户名", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ShouldSucceed_WhenUsersConfiguredCorrectly()
    {
        var options = new AuthOptions
        {
            Users =
            [
                new AuthUserOption
                {
                    Username = "user01",
                    PasswordHash = "sha256:abcdef"
                }
            ]
        };

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }
}
