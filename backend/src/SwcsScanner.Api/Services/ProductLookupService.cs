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
        var normalizedBarcode = barcode.Trim();
        if (normalizedBarcode.Length == 0)
        {
            return null;
        }

        var context = await BuildLookupContextAsync(cancellationToken);

        if (context.UseBarcodeTable)
        {
            var barcodeTableResult = await _repository.LookupByFieldAsync(
                normalizedBarcode,
                string.Empty,
                context.PriceField,
                context.SpecificationField,
                cancellationToken);

            if (barcodeTableResult is not null)
            {
                var matchedBy = string.IsNullOrWhiteSpace(_options.BarcodeColumn)
                    ? "BarcodeTable"
                    : _options.BarcodeColumn!;
                return MapLookupResult(barcodeTableResult, matchedBy);
            }
        }

        foreach (var barcodeField in context.AvailableBarcodeFields)
        {
            var row = await _repository.LookupByFieldAsync(
                normalizedBarcode,
                barcodeField,
                context.PriceField,
                context.SpecificationField,
                cancellationToken);

            if (row is not null)
            {
                return MapLookupResult(row, barcodeField);
            }
        }

        if (_options.EnableFunctionFallback &&
            context.Schema.HasBarcodeFunction &&
            context.Schema.Columns.Contains("ptypeid"))
        {
            var fallbackRow = await _repository.LookupByFunctionAsync(
                normalizedBarcode,
                context.PriceField,
                context.SpecificationField,
                cancellationToken);

            if (fallbackRow is not null)
            {
                return MapLookupResult(fallbackRow, "fn_strunitptype(B)");
            }
        }
        else
        {
            _logger.LogDebug("未启用函数回退，或数据库中缺少 fn_strunitptype/ptypeid。");
        }

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
            _logger.LogWarning("模糊查询未配置可用条码字段，已返回空结果。");
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
            throw new InvalidOperationException($"在 {_options.ProductTable} 中未找到任何可用价格字段。");
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

    private static ProductLookupResult MapLookupResult(DbProductLookupRow row, string matchedBy)
    {
        return new ProductLookupResult(
            row.ProductName,
            row.Specification ?? string.Empty,
            row.Price,
            matchedBy);
    }

    private sealed record LookupContext(
        SwcsSchemaSnapshot Schema,
        string? PriceField,
        string? SpecificationField,
        IReadOnlyList<string> AvailableBarcodeFields,
        bool UseBarcodeTable);
}
