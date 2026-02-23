using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwcsScanner.Api.Models.Responses;

namespace SwcsScanner.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v2/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiEnvelope<object>>(StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        var payload = new
        {
            status = "ok",
            utc = DateTime.UtcNow
        };

        return Ok(ApiEnvelopeFactory.Success(payload, HttpContext.TraceIdentifier));
    }
}
