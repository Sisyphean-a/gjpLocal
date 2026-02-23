using SwcsScanner.Api.Extensions;
using SwcsScanner.Api.Models.Responses;
using SwcsScanner.Api.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "SwcsScanner";
});

builder.ConfigureSwcsKestrel();
builder.Services.AddSwcsOptions(builder.Configuration);
builder.Services.AddSwcsCoreServices();

builder.Services.AddControllers();
builder.Services.AddSwcsSwagger();

var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var corsOptions = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
builder.ValidateSwcsSecuritySettings(jwtOptions, authOptions, corsOptions);
builder.Services.AddSwcsJwt(jwtOptions);
builder.Services.AddSwcsRateLimiting();
builder.Services.AddSwcsCors(corsOptions);

var app = builder.Build();

app.UseSwcsExceptionEnvelope();

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
app.UseSwcsStatusCodeEnvelope();

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
