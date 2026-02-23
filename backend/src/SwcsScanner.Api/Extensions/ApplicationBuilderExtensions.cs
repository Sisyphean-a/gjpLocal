using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using SwcsScanner.Api.Models.Responses;

namespace SwcsScanner.Api.Extensions;

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseSwcsExceptionEnvelope(this IApplicationBuilder app)
    {
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
                var error = ApiErrorResponse.ServerError();
                var payload = ApiEnvelopeFactory.Failure(error, context.TraceIdentifier);
                await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
            });
        });

        return app;
    }

    public static IApplicationBuilder UseSwcsStatusCodeEnvelope(this IApplicationBuilder app)
    {
        app.UseStatusCodePages(async context =>
        {
            if (context.HttpContext.Response.StatusCode == StatusCodes.Status429TooManyRequests)
            {
                context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
                var error = ApiErrorResponse.TooManyRequests();
                var payload = ApiEnvelopeFactory.Failure(error, context.HttpContext.TraceIdentifier);
                await context.HttpContext.Response.WriteAsync(JsonSerializer.Serialize(payload));
            }
        });

        return app;
    }
}
