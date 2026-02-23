using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using SwcsScanner.Api.Data;
using SwcsScanner.Api.Models.Responses;
using SwcsScanner.Api.Options;
using SwcsScanner.Api.Security;
using SwcsScanner.Api.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "SwcsScanner";
});

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

builder.Services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<SwcsOptions>, SwcsOptionsValidator>();
builder.Services
    .AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName))
    .ValidateOnStart();
builder.Services
    .AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .ValidateOnStart();
builder.Services
    .AddOptions<CorsOptions>()
    .Bind(builder.Configuration.GetSection(CorsOptions.SectionName))
    .ValidateOnStart();
builder.Services
    .AddOptions<SwcsOptions>()
    .Bind(builder.Configuration.GetSection(SwcsOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<ITimeProvider, SystemTimeProvider>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IProductLookupService, ProductLookupService>();
builder.Services.AddScoped<ISwcsProductLookupRepository, SwcsProductLookupRepository>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
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

var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var corsOptions = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
var strictSecurityValidation = builder.Configuration.GetValue<bool?>("Security:EnableStrictValidation") ?? false;
if (strictSecurityValidation)
{
    SecurityConfigurationValidator.Validate(builder.Environment, jwtOptions, authOptions, corsOptions);
}
else if (jwtOptions.Key.Length < 32)
{
    throw new InvalidOperationException("Jwt:Key 长度至少 32 位。");
}

var jwtKeyBytes = Encoding.UTF8.GetBytes(jwtOptions.Key);
builder.Services
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

builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("lookup", context =>
    {
        var partitionKey = context.User.Identity?.Name
                           ?? context.Connection.RemoteIpAddress?.ToString()
                           ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 15,
            Window = TimeSpan.FromSeconds(1),
            QueueLimit = 0
        });
    });
});

var allowedCorsOrigins = corsOptions.AllowedOrigins
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim())
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToList();

builder.Services.AddCors(options =>
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

var app = builder.Build();

app.UseExceptionHandler(handler =>
{
    handler.Run(async context =>
    {
        var exceptionFeature = context.Features.Get<IExceptionHandlerPathFeature>();
        if (exceptionFeature?.Error is not null)
        {
            var logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("GlobalException");
            logger.LogError(
                exceptionFeature.Error,
                "Unhandled exception at {Path}",
                exceptionFeature.Path ?? context.Request.Path.Value ?? "unknown");
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = ApiErrorResponse.ServerError();
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("mobile-client");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseStatusCodePages(async context =>
{
    if (context.HttpContext.Response.StatusCode == StatusCodes.Status429TooManyRequests)
    {
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        var payload = ApiErrorResponse.TooManyRequests();
        await context.HttpContext.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
});

app.MapControllers();

if (Directory.Exists(Path.Combine(app.Environment.ContentRootPath, "wwwroot")))
{
    var webRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallback(async context =>
    {
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await context.Response.SendFileAsync(Path.Combine(webRoot, "index.html"));
    });
}

app.Run();
