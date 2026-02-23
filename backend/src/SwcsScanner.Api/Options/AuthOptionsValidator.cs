using Microsoft.Extensions.Options;

namespace SwcsScanner.Api.Options;

public sealed class AuthOptionsValidator : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        var failures = new List<string>();

        var users = options.Users ?? [];
        for (var index = 0; index < users.Count; index++)
        {
            var user = users[index];
            if (string.IsNullOrWhiteSpace(user.Username))
            {
                failures.Add($"Auth:Users[{index}].Username 不能为空。");
            }

            if (string.IsNullOrWhiteSpace(user.Password) &&
                string.IsNullOrWhiteSpace(user.PasswordHash))
            {
                failures.Add($"Auth:Users[{index}] 需要配置 Password 或 PasswordHash。");
            }
        }

        var duplicateUsernames = users
            .Select(user => user.Username?.Trim())
            .Where(username => !string.IsNullOrWhiteSpace(username))
            .GroupBy(username => username!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateUsernames.Count > 0)
        {
            failures.Add($"Auth:Users 存在重复用户名: {string.Join(", ", duplicateUsernames)}。");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
