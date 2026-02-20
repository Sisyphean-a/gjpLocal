namespace SwcsScanner.Api.Data;

public interface ISwcsProductLookupRepository
{
    Task<SwcsSchemaSnapshot> GetSchemaSnapshotAsync(CancellationToken cancellationToken);

    Task<DbProductLookupRow?> LookupByFieldAsync(
        string barcode,
        string barcodeField,
        string priceField,
        string specificationField,
        CancellationToken cancellationToken);

    Task<DbProductLookupRow?> LookupByFunctionAsync(
        string barcode,
        string priceField,
        string specificationField,
        CancellationToken cancellationToken);
}
