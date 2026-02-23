using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SwcsScanner.Api.Data;
using SwcsScanner.Api.Options;
using SwcsScanner.Api.Security;
using SwcsScanner.Api.Services;

namespace SwcsScanner.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSwcsOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();
        services.AddSingleton<IValidateOptions<SwcsOptions>, SwcsOptionsValidator>();
        services
            .AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateOnStart();
        services
            .AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateOnStart();
        services
            .AddOptions<CorsOptions>()
            .Bind(configuration.GetSection(CorsOptions.SectionName))
            .ValidateOnStart();
        services
            .AddOptions<SwcsOptions>()
            .Bind(configuration.GetSection(SwcsOptions.SectionName))
            .ValidateOnStart();

        return services;
    }

    public static IServiceCollection AddSwcsCoreServices(this IServiceCollection services)
    {
        services.AddSingleton<ITimeProvider, SystemTimeProvider>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<IProductLookupService, ProductLookupService>();
        services.AddScoped<ISwcsProductLookupRepository, SwcsProductLookupRepository>();
        return services;
    }

    public static IServiceCollection AddSwcsSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            var scheme = new OpenApiSecurityScheme
            {
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Bearer {token}"
            };

            options.AddSecurityDefinition("Bearer", scheme);
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }

    public static IServiceCollection AddSwcsJwt(this IServiceCollection services, JwtOptions jwtOptions)
    {
        var jwtKeyBytes = Encoding.UTF8.GetBytes(jwtOptions.Key);
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(jwtKeyBytes),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        services.AddAuthorization();
        return services;
    }

    public static IServiceCollection AddSwcsRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("lookup", context =>
            {
                var userName = context.User.Identity?.Name?.Trim();
                var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                var partitionKey = string.IsNullOrWhiteSpace(userName)
                    ? clientIp
                    : $"{userName}@{clientIp}";

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 1,
                    Window = TimeSpan.FromSeconds(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 1,
                    AutoReplenishment = true
                });
            });
        });

        return services;
    }

    public static IServiceCollection AddSwcsCors(this IServiceCollection services, CorsOptions corsOptions)
    {
        var allowedCorsOrigins = corsOptions.AllowedOrigins
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        services.AddCors(options =>
        {
            options.AddPolicy("mobile-client", policy =>
            {
                if (allowedCorsOrigins.Count == 0)
                {
                    policy.WithOrigins(
                        "https://localhost:5173",
                        "https://127.0.0.1:5173",
                        "https://localhost:5001");
                }
                else
                {
                    policy.WithOrigins(allowedCorsOrigins.ToArray());
                }

                policy.AllowAnyHeader().AllowAnyMethod();
            });
        });

        return services;
    }
}
