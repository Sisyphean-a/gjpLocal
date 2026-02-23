namespace SwcsScanner.Api.Data;

public interface IProductSearchReadRepository
{
    Task<IReadOnlyList<DbProductSearchRow>> SearchByBarcodeFragmentAsync(
        string keyword,
        IReadOnlyList<string> barcodeFields,
        string? priceField,
        string? specificationField,
        int limit,
        CancellationToken cancellationToken);
}
