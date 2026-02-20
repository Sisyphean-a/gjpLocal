using SwcsScanner.Api.Security;

namespace SwcsScanner.Api.Services;

public interface ITokenService
{
    TokenResult GenerateToken(AuthenticatedUser user);
}
