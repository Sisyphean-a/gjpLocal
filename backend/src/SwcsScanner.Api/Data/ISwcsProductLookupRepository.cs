namespace SwcsScanner.Api.Data;

public interface ISwcsProductLookupRepository
{
    Task<SwcsSchemaSnapshot> GetSchemaSnapshotAsync(CancellationToken cancellationToken);

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

    Task<IReadOnlyList<DbProductUnitRow>> GetUnitsByProductIdAsync(
        string productId,
        string? matchedBarcode,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DbProductSearchRow>> SearchByBarcodeFragmentAsync(
        string keyword,
        IReadOnlyList<string> barcodeFields,
        string? priceField,
        string? specificationField,
        int limit,
        CancellationToken cancellationToken);
}
