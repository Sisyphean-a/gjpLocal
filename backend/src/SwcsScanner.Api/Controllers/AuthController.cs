using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwcsScanner.Api.Models.Requests;
using SwcsScanner.Api.Models.Responses;
using SwcsScanner.Api.Services;

namespace SwcsScanner.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
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
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status401Unauthorized)]
    public ActionResult<LoginResponse> Login([FromBody] LoginRequest request)
    {
        var user = _authService.Authenticate(request.Username, request.Password);
        if (user is null)
        {
            return Unauthorized(ApiErrorResponse.InvalidCredential());
        }

        var token = _tokenService.GenerateToken(user);
        return Ok(new LoginResponse(token.AccessToken, "Bearer", token.ExpiresAtUtc));
    }
}
