using SwcsScanner.Api.Data;
using SwcsScanner.Api.Options;

namespace SwcsScanner.Api.Services;

public sealed class ProductLookupContextBuilder : IProductLookupContextBuilder
{
    private readonly ISwcsProductLookupRepository _repository;
    private readonly SwcsOptions _options;

    public ProductLookupContextBuilder(ISwcsProductLookupRepository repository, SwcsOptions options)
    {
        _repository = repository;
        _options = options;
    }

    public async Task<ProductLookupContext> BuildAsync(CancellationToken cancellationToken)
    {
        var schema = await _repository.GetSchemaSnapshotAsync(cancellationToken);
        var priceField = ResolvePriceField(schema.Columns);
        var specificationField = schema.Columns.Contains(_options.SpecificationField)
            ? _options.SpecificationField
            : null;
        var availableBarcodeFields = (_options.BarcodeFields ?? [])
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Where(schema.Columns.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var useBarcodeTable = !string.IsNullOrWhiteSpace(_options.BarcodeTable) &&
                              !string.IsNullOrWhiteSpace(_options.BarcodeColumn);
        var canUseFunctionFallback = _options.EnableFunctionFallback &&
                                     schema.HasBarcodeFunction &&
                                     schema.Columns.Contains("ptypeid");

        return new ProductLookupContext(
            schema,
            priceField,
            specificationField,
            availableBarcodeFields,
            useBarcodeTable,
            canUseFunctionFallback);
    }

    private string? ResolvePriceField(IReadOnlySet<string> existingColumns)
    {
        if (!string.IsNullOrWhiteSpace(_options.PriceTable))
        {
            return null;
        }

        var priceField = (_options.PriceFields ?? []).FirstOrDefault(existingColumns.Contains);
        if (priceField is null)
        {
            throw new InvalidOperationException($"No available price field found in {_options.ProductTable}.");
        }

        return priceField;
    }
}
