using Microsoft.Extensions.Options;

namespace SwcsScanner.Api.Options;

public sealed class SwcsOptionsValidator : IValidateOptions<SwcsOptions>
{
    public ValidateOptionsResult Validate(string? name, SwcsOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ProductTable))
        {
            failures.Add("Swcs:ProductTable 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(options.ProductNameField))
        {
            failures.Add("Swcs:ProductNameField 不能为空。");
        }

        if (!string.IsNullOrWhiteSpace(options.BarcodeTable) &&
            string.IsNullOrWhiteSpace(options.BarcodeColumn))
        {
            failures.Add("配置了 Swcs:BarcodeTable 时，必须配置 Swcs:BarcodeColumn。");
        }

        if (string.IsNullOrWhiteSpace(options.PriceTable) &&
            (options.PriceFields?.Count ?? 0) == 0)
        {
            failures.Add("未配置 Swcs:PriceTable 时，Swcs:PriceFields 至少提供一个候选字段。");
        }

        if (string.IsNullOrWhiteSpace(options.BarcodeTable) &&
            (options.BarcodeFields?.Count ?? 0) == 0 &&
            !options.EnableFunctionFallback)
        {
            failures.Add("未配置 Swcs:BarcodeTable 时，若关闭函数回退，Swcs:BarcodeFields 至少提供一个候选字段。");
        }

        if (!string.IsNullOrWhiteSpace(options.PriceTable) &&
            string.IsNullOrWhiteSpace(options.PriceColumn))
        {
            failures.Add("配置了 Swcs:PriceTable 时，必须配置 Swcs:PriceColumn。");
        }

        if (options.SchemaCacheMinutes < 0)
        {
            failures.Add("Swcs:SchemaCacheMinutes 不能小于 0。");
        }

        if (options.QueryTimeoutSeconds <= 0)
        {
            failures.Add("Swcs:QueryTimeoutSeconds 必须大于 0。");
        }

        if (options.QueryTimeoutSeconds > 120)
        {
            failures.Add("Swcs:QueryTimeoutSeconds 不能大于 120。");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
