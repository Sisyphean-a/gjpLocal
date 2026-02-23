using Microsoft.Extensions.Options;
using SwcsScanner.Api.Options;

namespace SwcsScanner.Api.Data;

public sealed class SwcsProductLookupRepository : ISwcsProductLookupRepository
{
    private readonly ISchemaRepository _schemaRepository;
    private readonly IProductLookupReadRepository _lookupReadRepository;
    private readonly IProductSearchReadRepository _searchReadRepository;
    private readonly IProductUnitReadRepository _unitReadRepository;

    public SwcsProductLookupRepository(
        IConfiguration configuration,
        IOptions<SwcsOptions> options)
        : this(CreateRepositories(configuration, options))
    {
    }

    internal SwcsProductLookupRepository(
        ISchemaRepository schemaRepository,
        IProductLookupReadRepository lookupReadRepository,
        IProductSearchReadRepository searchReadRepository,
        IProductUnitReadRepository unitReadRepository)
    {
        _schemaRepository = schemaRepository;
        _lookupReadRepository = lookupReadRepository;
        _searchReadRepository = searchReadRepository;
        _unitReadRepository = unitReadRepository;
    }

    public Task<SwcsSchemaSnapshot> GetSchemaSnapshotAsync(CancellationToken cancellationToken)
    {
        return _schemaRepository.GetSchemaSnapshotAsync(cancellationToken);
    }

    public Task<DbProductLookupRow?> LookupByFieldAsync(
        string barcode,
        string barcodeField,
        string? priceField,
        string? specificationField,
        CancellationToken cancellationToken)
    {
        return _lookupReadRepository.LookupByFieldAsync(
            barcode,
            barcodeField,
            priceField,
            specificationField,
            cancellationToken);
    }

    public Task<DbProductLookupRow?> LookupByFunctionAsync(
        string barcode,
        string? priceField,
        string? specificationField,
        CancellationToken cancellationToken)
    {
        return _lookupReadRepository.LookupByFunctionAsync(
            barcode,
            priceField,
            specificationField,
            cancellationToken);
    }

    public Task<DbProductLookupRow?> LookupByCompositeKeywordAsync(
        string keyword,
        string? priceField,
        string? specificationField,
        CancellationToken cancellationToken)
    {
        return _lookupReadRepository.LookupByCompositeKeywordAsync(
            keyword,
            priceField,
            specificationField,
            cancellationToken);
    }

    public Task<IReadOnlyList<DbProductUnitRow>> GetUnitsByProductIdAsync(
        string productId,
        string? matchedBarcode,
        CancellationToken cancellationToken)
    {
        return _unitReadRepository.GetUnitsByProductIdAsync(
            productId,
            matchedBarcode,
            cancellationToken);
    }

    public Task<IReadOnlyList<DbProductSearchRow>> SearchByBarcodeFragmentAsync(
        string keyword,
        IReadOnlyList<string> barcodeFields,
        string? priceField,
        string? specificationField,
        int limit,
        CancellationToken cancellationToken)
    {
        return _searchReadRepository.SearchByBarcodeFragmentAsync(
            keyword,
            barcodeFields,
            priceField,
            specificationField,
            limit,
            cancellationToken);
    }

    private static (
        ISchemaRepository SchemaRepository,
        IProductLookupReadRepository LookupReadRepository,
        IProductSearchReadRepository SearchReadRepository,
        IProductUnitReadRepository UnitReadRepository) CreateRepositories(
        IConfiguration configuration,
        IOptions<SwcsOptions> options)
    {
        var context = new SwcsRepositoryContext(configuration, options);
        var schemaRepository = new SwcsSchemaRepository(context);
        var lookupReadRepository = new SwcsProductLookupReadRepository(context, schemaRepository);
        var searchReadRepository = new SwcsProductSearchReadRepository(context, schemaRepository);
        var unitReadRepository = new SwcsProductUnitReadRepository(context);

        return (schemaRepository, lookupReadRepository, searchReadRepository, unitReadRepository);
    }

    private SwcsProductLookupRepository((
        ISchemaRepository SchemaRepository,
        IProductLookupReadRepository LookupReadRepository,
        IProductSearchReadRepository SearchReadRepository,
        IProductUnitReadRepository UnitReadRepository) repositories)
        : this(
            repositories.SchemaRepository,
            repositories.LookupReadRepository,
            repositories.SearchReadRepository,
            repositories.UnitReadRepository)
    {
    }
}
