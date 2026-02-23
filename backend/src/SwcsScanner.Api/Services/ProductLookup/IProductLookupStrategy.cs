namespace SwcsScanner.Api.Services;

public interface IProductLookupStrategy
{
    Task<ProductLookupMatch?> LookupAsync(
        string candidate,
        ProductLookupContext context,
        CancellationToken cancellationToken);
}
