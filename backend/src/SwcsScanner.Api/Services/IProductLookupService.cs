namespace SwcsScanner.Api.Services;

public interface IProductLookupService
{
    Task<ProductLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken);
}
