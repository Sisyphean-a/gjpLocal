namespace SwcsScanner.Api.Data;

public interface IProductLookupReadRepository
{
    Task<DbProductLookupRow?> LookupByFieldAsync(
        string barcode,
        string barcodeField,
        string? priceField,
        string? specificationField,
        CancellationToken cancellationToken);

    Task<DbProductLookupRow?> LookupByFunctionAsync(
        string barcode,
        string? priceField,
        string? specificationField,
        CancellationToken cancellationToken);

    Task<DbProductLookupRow?> LookupByCompositeKeywordAsync(
        string keyword,
        string? priceField,
        string? specificationField,
        CancellationToken cancellationToken);
}
