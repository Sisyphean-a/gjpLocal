using SwcsScanner.Api.Data;

namespace SwcsScanner.Api.Services;

public sealed class CompatibilityCompositeLookupStrategy : IProductLookupStrategy
{
    private readonly ISwcsProductLookupRepository _repository;

    public CompatibilityCompositeLookupStrategy(ISwcsProductLookupRepository repository)
    {
        _repository = repository;
    }

    public async Task<ProductLookupMatch?> LookupAsync(
        string candidate,
        ProductLookupContext context,
        CancellationToken cancellationToken)
    {
        if (candidate.Length < 8 || !HasAtLeastDigits(candidate, 8))
        {
            return null;
        }

        // 兼容管家婆模糊匹配路径：精确匹配失败时再退化。
        var row = await _repository.LookupByCompositeKeywordAsync(
            candidate,
            context.PriceField,
            context.SpecificationField,
            cancellationToken);

        return row is null ? null : new ProductLookupMatch(row, "LegacyCompositeLike");
    }

    private static bool HasAtLeastDigits(string value, int minDigits)
    {
        var count = 0;
        foreach (var ch in value)
        {
            if (!char.IsDigit(ch))
            {
                continue;
            }

            count++;
            if (count >= minDigits)
            {
                return true;
            }
        }

        return false;
    }
}
