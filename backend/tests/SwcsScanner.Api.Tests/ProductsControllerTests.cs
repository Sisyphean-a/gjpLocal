using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SwcsScanner.Api.Controllers;
using SwcsScanner.Api.Models.Responses;
using SwcsScanner.Api.Options;
using SwcsScanner.Api.Services;

namespace SwcsScanner.Api.Tests;

public sealed class ProductsControllerTests
{
    [Fact]
    public async Task Search_ShouldReturnBadRequest_WhenKeywordTooShort()
    {
        var service = new FakeProductLookupService();
        var controller = CreateController(service);

        var result = await controller.Search("1", null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var payload = Assert.IsType<ApiEnvelope<object?>>(badRequest.Value);
        Assert.Equal("INVALID_SEARCH_KEYWORD", payload.Code);
    }

    [Fact]
    public async Task Search_ShouldReturnOk_WithItems()
    {
        var service = new FakeProductLookupService
        {
            SearchResults =
            [
                new ProductSearchItemResult("雪碧", "08093", "xb", "500ml", 3m, "6901028075803", "Barcode")
            ]
        };
        var controller = CreateController(service);

        var result = await controller.Search("6901", 20, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<ApiEnvelope<ProductSearchResponse>>(ok.Value);
        Assert.Equal("OK", payload.Code);
        Assert.NotNull(payload.Data);
        Assert.Equal(1, payload.Data!.Count);
        Assert.Equal("6901028075803", payload.Data.Items[0].Barcode);
        Assert.Equal("08093", payload.Data.Items[0].ProductCode);
        Assert.Equal("xb", payload.Data.Items[0].ProductShortCode);
    }

    private static IOptions<SwcsOptions> CreateOptions()
    {
        return Microsoft.Extensions.Options.Options.Create(new SwcsOptions
        {
            ProductTable = "dbo.Ptype",
            SpecificationField = "Standard",
            BarcodeFields = ["Standard", "Barcode"],
            PriceFields = ["RetailPrice", "Price1"],
            EnableFunctionFallback = true
        });
    }

    private static ProductsController CreateController(IProductLookupService service)
    {
        return new ProductsController(service, CreateOptions())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private sealed class FakeProductLookupService : IProductLookupService
    {
        public ProductLookupResult? LookupResult { get; init; }

        public List<ProductSearchItemResult> SearchResults { get; init; } = [];

        public Task<ProductLookupResult?> LookupAsync(string barcode, CancellationToken cancellationToken)
        {
            return Task.FromResult(LookupResult);
        }

        public Task<IReadOnlyList<ProductSearchItemResult>> SearchByBarcodeFragmentAsync(
            string keyword,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ProductSearchItemResult>>(SearchResults);
        }
    }
}
