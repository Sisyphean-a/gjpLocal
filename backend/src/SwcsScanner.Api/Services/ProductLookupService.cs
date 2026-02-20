using Microsoft.Extensions.Options;
using SwcsScanner.Api.Data;
using SwcsScanner.Api.Options;

namespace SwcsScanner.Api.Services;

public sealed class ProductLookupService : IProductLookupService
{
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
        var trimmedBarcode = barcode.Trim();
        if (trimmedBarcode.Length == 0)
        {
            return null;
        }

        var schema = await _repository.GetSchemaSnapshotAsync(cancellationToken);
        
        string? priceField = null;
        if (string.IsNullOrEmpty(_options.PriceTable))
        {
            priceField = PickFirstExistingField(_options.PriceFields, schema.Columns);
            if (priceField is null)
            {
                throw new InvalidOperationException($"在 {_options.ProductTable} 中未找到任何可用价格字段。");
            }
        }

        var specificationField = schema.Columns.Contains(_options.SpecificationField)
            ? _options.SpecificationField
            : _options.SpecificationField;

        if (!string.IsNullOrEmpty(_options.BarcodeTable))
        {
            var row = await _repository.LookupByFieldAsync(
                trimmedBarcode,
                string.Empty,
                priceField,
                specificationField,
                cancellationToken);

            if (row is not null)
            {
                return new ProductLookupResult(
                    row.ProductName,
                    row.Specification ?? string.Empty,
                    row.Price,
                    "BarcodeTable");
            }
        }

        var availableBarcodeFields = _options.BarcodeFields
            .Where(field => schema.Columns.Contains(field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var barcodeField in availableBarcodeFields)
        {
            var row = await _repository.LookupByFieldAsync(
                trimmedBarcode,
                barcodeField,
                priceField,
                specificationField,
                cancellationToken);

            if (row is null)
            {
                continue;
            }

            return new ProductLookupResult(
                row.ProductName,
                row.Specification ?? string.Empty,
                row.Price,
                barcodeField);
        }

        if (_options.EnableFunctionFallback &&
            schema.HasBarcodeFunction &&
            schema.Columns.Contains("ptypeid"))
        {
            var fallbackRow = await _repository.LookupByFunctionAsync(
                trimmedBarcode,
                priceField,
                specificationField,
                cancellationToken);

            if (fallbackRow is not null)
            {
                return new ProductLookupResult(
                    fallbackRow.ProductName,
                    fallbackRow.Specification ?? string.Empty,
                    fallbackRow.Price,
                    "fn_strunitptype(B)");
            }
        }
        else
        {
            _logger.LogDebug("未启用函数回退，或数据库中缺少 fn_strunitptype/ptypeid。");
        }

        return null;
    }

    private static string? PickFirstExistingField(IEnumerable<string> candidates, IReadOnlySet<string> existingColumns)
    {
        return candidates.FirstOrDefault(existingColumns.Contains);
    }
}
