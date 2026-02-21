using System.Text;
using Microsoft.Extensions.Options;
using SwcsScanner.Api.Data;
using SwcsScanner.Api.Options;

namespace SwcsScanner.Api.Services;

public sealed class ProductLookupService : IProductLookupService
{
    private const int DefaultSearchLimit = 20;
    private const int MaxSearchLimit = 50;
    private const int MinSearchKeywordLength = 2;

    private readonly ISwcsProductLookupRepository _repository;
    private readonly SwcsOptions _options;
    private readonly ILogger<ProductLookupService> _logger;

    public ProductLookupService(
        ISwcsProductLookupRepository repository,
        IOptions<SwcsOptions> options,
        ILogger<ProductLookupService> logger)
    {
        _repository = repository;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ProductLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken)
    {
        var lookupCandidates = BuildLookupCandidates(barcode);
        if (lookupCandidates.Count == 0)
        {
            return null;
        }

        var context = await BuildLookupContextAsync(cancellationToken);
        var canUseFunctionFallback = _options.EnableFunctionFallback &&
                                     context.Schema.HasBarcodeFunction &&
                                     context.Schema.Columns.Contains("ptypeid");

        foreach (var lookupValue in lookupCandidates)
        {
            if (context.UseBarcodeTable)
            {
                var barcodeTableResult = await _repository.LookupByFieldAsync(
                    lookupValue,
                    string.Empty,
                    context.PriceField,
                    context.SpecificationField,
                    cancellationToken);

                if (barcodeTableResult is not null)
                {
                    var matchedBy = string.IsNullOrWhiteSpace(_options.BarcodeColumn)
                        ? "BarcodeTable"
                        : _options.BarcodeColumn!;
                    return await MapLookupResultAsync(barcodeTableResult, matchedBy, cancellationToken);
                }
            }

            foreach (var barcodeField in context.AvailableBarcodeFields)
            {
                var row = await _repository.LookupByFieldAsync(
                    lookupValue,
                    barcodeField,
                    context.PriceField,
                    context.SpecificationField,
                    cancellationToken);

                if (row is not null)
                {
                    return await MapLookupResultAsync(row, barcodeField, cancellationToken);
                }
            }

            if (canUseFunctionFallback)
            {
                var fallbackRow = await _repository.LookupByFunctionAsync(
                    lookupValue,
                    context.PriceField,
                    context.SpecificationField,
                    cancellationToken);

                if (fallbackRow is not null)
                {
                    return await MapLookupResultAsync(fallbackRow, "fn_strunitptype(B)", cancellationToken);
                }
            }
        }

        foreach (var lookupValue in lookupCandidates)
        {
            if (lookupValue.Length < 8 || !HasAtLeastDigits(lookupValue, 8))
            {
                continue;
            }

            // 兼容管家婆模糊匹配路径：精确匹配失败时再退化。
            var compatibilityRow = await _repository.LookupByCompositeKeywordAsync(
                lookupValue,
                context.PriceField,
                context.SpecificationField,
                cancellationToken);

            if (compatibilityRow is not null)
            {
                return await MapLookupResultAsync(compatibilityRow, "LegacyCompositeLike", cancellationToken);
            }
        }

        if (!canUseFunctionFallback)
        {
            _logger.LogDebug("Function fallback disabled or schema missing fn_strunitptype/ptypeid.");
        }

        _logger.LogDebug("Exact lookup miss. Candidates: {Candidates}", string.Join(", ", lookupCandidates));
        return null;
    }

    public async Task<IReadOnlyList<ProductSearchItemResult>> SearchByBarcodeFragmentAsync(
        string keyword,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedKeyword = keyword.Trim();
        if (normalizedKeyword.Length < MinSearchKeywordLength)
        {
            return [];
        }

        var safeLimit = NormalizeLimit(limit);
        var context = await BuildLookupContextAsync(cancellationToken);

        if (!context.UseBarcodeTable && context.AvailableBarcodeFields.Count == 0)
        {
            _logger.LogWarning("No available barcode fields for fuzzy search.");
            return [];
        }

        var rows = await _repository.SearchByBarcodeFragmentAsync(
            normalizedKeyword,
            context.AvailableBarcodeFields,
            context.PriceField,
            context.SpecificationField,
            safeLimit,
            cancellationToken);

        return rows
            .Select(row => new ProductSearchItemResult(
                row.ProductName,
                row.Specification ?? string.Empty,
                row.Price,
                row.Barcode,
                row.BarcodeMatchedBy))
            .ToList();
    }

    private async Task<LookupContext> BuildLookupContextAsync(CancellationToken cancellationToken)
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

        return new LookupContext(schema, priceField, specificationField, availableBarcodeFields, useBarcodeTable);
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

    private static int NormalizeLimit(int limit)
    {
        if (limit <= 0)
        {
            return DefaultSearchLimit;
        }

        return Math.Min(limit, MaxSearchLimit);
    }

    private async Task<ProductLookupResult> MapLookupResultAsync(
        DbProductLookupRow row,
        string matchedBy,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(row.ProductId))
        {
            return new ProductLookupResult(
                row.ProductName,
                row.Specification ?? string.Empty,
                row.Price,
                matchedBy,
                null,
                []);
        }

        var unitRows = await _repository.GetUnitsByProductIdAsync(
            row.ProductId,
            row.MatchedBarcode,
            cancellationToken);

        var units = unitRows
            .Select(unit => new ProductLookupUnitResult(
                unit.UnitId,
                unit.UnitName,
                unit.UnitRate,
                unit.Price,
                SplitBarcodes(unit.BarcodeList),
                unit.IsMatchedUnit))
            .ToList();

        var currentUnit = units.FirstOrDefault(unit => unit.IsMatchedUnit)
                          ?? units.FirstOrDefault(unit =>
                              !string.IsNullOrWhiteSpace(row.MatchedUnitId) &&
                              string.Equals(unit.UnitId, row.MatchedUnitId, StringComparison.OrdinalIgnoreCase))
                          ?? units.FirstOrDefault();

        var currentPrice = currentUnit?.Price ?? row.Price;

        return new ProductLookupResult(
            row.ProductName,
            row.Specification ?? string.Empty,
            currentPrice,
            matchedBy,
            currentUnit,
            units);
    }

    private static IReadOnlyList<string> SplitBarcodes(string? barcodeList)
    {
        if (string.IsNullOrWhiteSpace(barcodeList))
        {
            return [];
        }

        return barcodeList
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<string> BuildLookupCandidates(string barcode)
    {
        var input = barcode?.Trim() ?? string.Empty;
        if (input.Length == 0)
        {
            return [];
        }

        var candidates = new List<string>();
        AddCandidate(candidates, input);

        var digitsBuilder = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (char.IsDigit(ch))
            {
                digitsBuilder.Append(ch);
            }
        }

        var digits = digitsBuilder.ToString();
        if (digits.Length > 0)
        {
            AddCandidate(candidates, digits);
        }

        if (digits.Length >= 16 && digits.StartsWith("01", StringComparison.Ordinal))
        {
            var gtin14 = digits.Substring(2, 14);
            AddCandidate(candidates, gtin14);
            if (gtin14[0] == '0')
            {
                AddCandidate(candidates, gtin14[1..]);
            }
        }

        if (digits.Length == 14 && digits[0] == '0')
        {
            AddCandidate(candidates, digits[1..]);
        }
        else if (digits.Length == 13)
        {
            AddCandidate(candidates, $"0{digits}");
        }

        return candidates;
    }

    private static void AddCandidate(ICollection<string> candidates, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var existing in candidates)
        {
            if (string.Equals(existing, value, StringComparison.Ordinal))
            {
                return;
            }
        }

        candidates.Add(value);
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

    private sealed record LookupContext(
        SwcsSchemaSnapshot Schema,
        string? PriceField,
        string? SpecificationField,
        IReadOnlyList<string> AvailableBarcodeFields,
        bool UseBarcodeTable);
}
