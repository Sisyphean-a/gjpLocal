using System.Diagnostics;
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
    private readonly ILogger<ProductLookupService> _logger;
    private readonly IBarcodeLookupCandidateBuilder _candidateBuilder;
    private readonly IProductLookupContextBuilder _contextBuilder;
    private readonly IProductLookupResultMapper _resultMapper;
    private readonly IReadOnlyList<IProductLookupStrategy> _exactLookupStrategies;
    private readonly IProductLookupStrategy _compatibilityLookupStrategy;

    public ProductLookupService(
        ISwcsProductLookupRepository repository,
        IOptions<SwcsOptions> options,
        ILogger<ProductLookupService> logger)
    {
        var swcsOptions = options.Value;

        _repository = repository;
        _logger = logger;
        _candidateBuilder = new BarcodeLookupCandidateBuilder();
        _contextBuilder = new ProductLookupContextBuilder(repository, swcsOptions);
        _resultMapper = new ProductLookupResultMapper(repository);
        _exactLookupStrategies =
        [
            new BarcodeTableExactLookupStrategy(repository, swcsOptions),
            new BarcodeFieldExactLookupStrategy(repository),
            new FunctionFallbackLookupStrategy(repository)
        ];
        _compatibilityLookupStrategy = new CompatibilityCompositeLookupStrategy(repository);
    }

    public async Task<ProductLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var isHit = false;

        try
        {
            var lookupCandidates = _candidateBuilder.Build(barcode);
            if (lookupCandidates.Count == 0)
            {
                return null;
            }

            var context = await _contextBuilder.BuildAsync(cancellationToken);

            foreach (var lookupValue in lookupCandidates)
            {
                var match = await TryLookupAsync(_exactLookupStrategies, lookupValue, context, cancellationToken);
                if (match is null)
                {
                    continue;
                }

                isHit = true;
                return await _resultMapper.MapAsync(match.Row, match.MatchedBy, cancellationToken);
            }

            foreach (var lookupValue in lookupCandidates)
            {
                var match = await _compatibilityLookupStrategy.LookupAsync(lookupValue, context, cancellationToken);
                if (match is null)
                {
                    continue;
                }

                isHit = true;
                return await _resultMapper.MapAsync(match.Row, match.MatchedBy, cancellationToken);
            }

            if (!context.CanUseFunctionFallback)
            {
                _logger.LogDebug("Function fallback disabled or schema missing fn_strunitptype/ptypeid.");
            }

            _logger.LogDebug("Exact lookup miss. Candidates: {Candidates}", string.Join(", ", lookupCandidates));
            return null;
        }
        finally
        {
            LogQueryTiming("lookup", stopwatch.ElapsedMilliseconds, isHit, barcode);
        }
    }

    public async Task<IReadOnlyList<ProductSearchItemResult>> SearchByBarcodeFragmentAsync(
        string keyword,
        int limit,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var isHit = false;
        var normalizedKeyword = keyword.Trim();

        try
        {
            if (normalizedKeyword.Length < MinSearchKeywordLength)
            {
                return [];
            }

            var safeLimit = NormalizeLimit(limit);
            var context = await _contextBuilder.BuildAsync(cancellationToken);
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

            isHit = rows.Count > 0;
            return rows
                .Select(row => new ProductSearchItemResult(
                    row.ProductName,
                    row.ProductCode,
                    row.ProductShortCode,
                    row.Specification ?? string.Empty,
                    row.Price,
                    row.Barcode,
                    row.BarcodeMatchedBy))
                .ToList();
        }
        finally
        {
            LogQueryTiming("search", stopwatch.ElapsedMilliseconds, isHit, normalizedKeyword);
        }
    }

    private static async Task<ProductLookupMatch?> TryLookupAsync(
        IReadOnlyList<IProductLookupStrategy> strategies,
        string lookupValue,
        ProductLookupContext context,
        CancellationToken cancellationToken)
    {
        foreach (var strategy in strategies)
        {
            var match = await strategy.LookupAsync(lookupValue, context, cancellationToken);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static int NormalizeLimit(int limit)
    {
        if (limit <= 0)
        {
            return DefaultSearchLimit;
        }

        return Math.Min(limit, MaxSearchLimit);
    }

    private void LogQueryTiming(string operation, long elapsedMilliseconds, bool hit, string key)
    {
        if (elapsedMilliseconds >= 300)
        {
            _logger.LogWarning(
                "Slow {Operation} query. elapsed={ElapsedMilliseconds}ms hit={Hit} key={Key}",
                operation,
                elapsedMilliseconds,
                hit,
                key);
            return;
        }

        _logger.LogDebug(
            "{Operation} query completed. elapsed={ElapsedMilliseconds}ms hit={Hit}",
            operation,
            elapsedMilliseconds,
            hit);
    }
}
