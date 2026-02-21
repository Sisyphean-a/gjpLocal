namespace SwcsScanner.Api.Services;

public interface IProductLookupService
{
    Task<ProductLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProductSearchItemResult>> SearchByBarcodeFragmentAsync(
        string keyword,
        int limit,
        CancellationToken cancellationToken);
}
