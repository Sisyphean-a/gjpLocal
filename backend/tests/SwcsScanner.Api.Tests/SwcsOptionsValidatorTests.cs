using SwcsScanner.Api.Options;

namespace SwcsScanner.Api.Tests;

public sealed class SwcsOptionsValidatorTests
{
    private readonly SwcsOptionsValidator _validator = new();

    [Fact]
    public void Validate_ShouldFail_WhenQueryTimeoutIsNotPositive()
    {
        var options = BuildValidOptions(0);

        var result = _validator.Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, failure => failure.Contains("QueryTimeoutSeconds", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ShouldFail_WhenQueryTimeoutIsTooLarge()
    {
        var options = BuildValidOptions(121);

        var result = _validator.Validate(null, options);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Validate_ShouldSucceed_WhenQueryTimeoutIsWithinRange()
    {
        var options = BuildValidOptions(12);

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    private static SwcsOptions BuildValidOptions(int queryTimeoutSeconds)
    {
        return new SwcsOptions
        {
            ProductTable = "dbo.Ptype",
            ProductNameField = "pfullname",
            BarcodeTable = "xw_PtypeBarCode",
            BarcodeColumn = "BarCode",
            PriceTable = "xw_P_PtypePrice",
            PriceColumn = "Price",
            PriceFields = [],
            BarcodeFields = [],
            QueryTimeoutSeconds = queryTimeoutSeconds
        };
    }
}
