namespace SwcsScanner.Api.Services;

public interface IProductLookupContextBuilder
{
    Task<ProductLookupContext> BuildAsync(CancellationToken cancellationToken);
}
