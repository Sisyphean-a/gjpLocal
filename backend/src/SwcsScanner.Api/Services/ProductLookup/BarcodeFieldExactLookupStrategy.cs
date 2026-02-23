using SwcsScanner.Api.Data;

namespace SwcsScanner.Api.Services;

public sealed class BarcodeFieldExactLookupStrategy : IProductLookupStrategy
{
    private readonly ISwcsProductLookupRepository _repository;

    public BarcodeFieldExactLookupStrategy(ISwcsProductLookupRepository repository)
    {
        _repository = repository;
    }

    public async Task<ProductLookupMatch?> LookupAsync(
        string candidate,
        ProductLookupContext context,
        CancellationToken cancellationToken)
    {
        foreach (var barcodeField in context.AvailableBarcodeFields)
        {
            var row = await _repository.LookupByFieldAsync(
                candidate,
                barcodeField,
                context.PriceField,
                context.SpecificationField,
                cancellationToken);

            if (row is not null)
            {
                return new ProductLookupMatch(row, barcodeField);
            }
        }

        return null;
    }
}
