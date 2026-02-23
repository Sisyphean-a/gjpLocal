using SwcsScanner.Api.Data;

namespace SwcsScanner.Api.Services;

public interface IProductLookupResultMapper
{
    Task<ProductLookupResult> MapAsync(
        DbProductLookupRow row,
        string matchedBy,
        CancellationToken cancellationToken);
}
