using SwcsScanner.Api.Data;
using SwcsScanner.Api.Options;

namespace SwcsScanner.Api.Services;

public sealed class BarcodeTableExactLookupStrategy : IProductLookupStrategy
{
    private readonly ISwcsProductLookupRepository _repository;
    private readonly SwcsOptions _options;

    public BarcodeTableExactLookupStrategy(ISwcsProductLookupRepository repository, SwcsOptions options)
    {
        _repository = repository;
        _options = options;
    }

    public async Task<ProductLookupMatch?> LookupAsync(
        string candidate,
        ProductLookupContext context,
        CancellationToken cancellationToken)
    {
        if (!context.UseBarcodeTable)
        {
            return null;
        }

        var row = await _repository.LookupByFieldAsync(
            candidate,
            string.Empty,
            context.PriceField,
            context.SpecificationField,
            cancellationToken);

        if (row is null)
        {
            return null;
        }

        var matchedBy = string.IsNullOrWhiteSpace(_options.BarcodeColumn)
            ? "BarcodeTable"
            : _options.BarcodeColumn!;

        return new ProductLookupMatch(row, matchedBy);
    }
}
