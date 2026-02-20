namespace SwcsScanner.Api.Security;

public interface ITimeProvider
{
    DateTime UtcNow { get; }
}
