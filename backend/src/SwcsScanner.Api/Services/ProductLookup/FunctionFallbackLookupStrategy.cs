using SwcsScanner.Api.Data;

namespace SwcsScanner.Api.Services;

public sealed class FunctionFallbackLookupStrategy : IProductLookupStrategy
{
    private readonly ISwcsProductLookupRepository _repository;

    public FunctionFallbackLookupStrategy(ISwcsProductLookupRepository repository)
    {
        _repository = repository;
    }

    public async Task<ProductLookupMatch?> LookupAsync(
        string candidate,
        ProductLookupContext context,
        CancellationToken cancellationToken)
    {
        if (!context.CanUseFunctionFallback)
        {
            return null;
        }

        var row = await _repository.LookupByFunctionAsync(
            candidate,
            context.PriceField,
            context.SpecificationField,
            cancellationToken);

        return row is null ? null : new ProductLookupMatch(row, "fn_strunitptype(B)");
    }
}
