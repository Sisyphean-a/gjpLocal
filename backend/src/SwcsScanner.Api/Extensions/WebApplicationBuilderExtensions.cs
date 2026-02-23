using SwcsScanner.Api.Options;
using SwcsScanner.Api.Security;

namespace SwcsScanner.Api.Extensions;

public static class WebApplicationBuilderExtensions
{
    public static HttpsOptions ConfigureSwcsKestrel(this WebApplicationBuilder builder)
    {
        var httpsOptions = builder.Configuration.GetSection(HttpsOptions.SectionName).Get<HttpsOptions>() ?? new HttpsOptions();
        if (!string.IsNullOrWhiteSpace(httpsOptions.PfxPath))
        {
            var pfxPath = Path.GetFullPath(httpsOptions.PfxPath, builder.Environment.ContentRootPath);
            builder.WebHost.ConfigureKestrel(options =>
            {
                if (httpsOptions.HttpPort.HasValue)
                {
                    options.ListenAnyIP(httpsOptions.HttpPort.Value);
                }

                options.ListenAnyIP(httpsOptions.HttpsPort, listen =>
                {
                    listen.UseHttps(pfxPath, httpsOptions.PfxPassword);
                });
            });
        }

        return httpsOptions;
    }

    public static void ValidateSwcsSecuritySettings(
        this WebApplicationBuilder builder,
        JwtOptions jwtOptions,
        AuthOptions authOptions,
        CorsOptions corsOptions)
    {
        var strictSecurityValidation = builder.Configuration.GetValue<bool?>("Security:EnableStrictValidation") ?? false;
        if (strictSecurityValidation)
        {
            SecurityConfigurationValidator.Validate(builder.Environment, jwtOptions, authOptions, corsOptions);
            return;
        }

        if (jwtOptions.Key.Length < 32)
        {
            throw new InvalidOperationException("Jwt:Key 长度至少 32 位。");
        }
    }
}
