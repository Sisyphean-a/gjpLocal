using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwcsScanner.Api.Models.Requests;
using SwcsScanner.Api.Models.Responses;
using SwcsScanner.Api.Services;

namespace SwcsScanner.Api.Controllers;

[ApiController]
[Route("api/v2/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ITokenService _tokenService;

    public AuthController(IAuthService authService, ITokenService tokenService)
    {
        _authService = authService;
        _tokenService = tokenService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<ApiEnvelope<LoginResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiEnvelope<object>>(StatusCodes.Status401Unauthorized)]
    public ActionResult<ApiEnvelope<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        var user = _authService.Authenticate(request.Username, request.Password);
        if (user is null)
        {
            return Unauthorized(ApiEnvelopeFactory.Failure(
                ApiErrorResponse.InvalidCredential(),
                HttpContext.TraceIdentifier));
        }

        var token = _tokenService.GenerateToken(user);
        var payload = new LoginResponse(token.AccessToken, "Bearer", token.ExpiresAtUtc);
        return Ok(ApiEnvelopeFactory.Success(payload, HttpContext.TraceIdentifier));
    }
}
