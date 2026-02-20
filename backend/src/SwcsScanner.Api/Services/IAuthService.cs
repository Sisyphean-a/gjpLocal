using SwcsScanner.Api.Security;

namespace SwcsScanner.Api.Services;

public interface IAuthService
{
    AuthenticatedUser? Authenticate(string username, string password);
}
