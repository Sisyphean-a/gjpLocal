using Microsoft.Extensions.Logging.Abstractions;
using SwcsScanner.Api.Data;
using SwcsScanner.Api.Options;
using SwcsScanner.Api.Services;

namespace SwcsScanner.Api.Tests;

public sealed class ProductLookupServiceTests
{
    [Fact]
    public async Task LookupAsync_ShouldPreferDirectField_WhenMatched()
    {
        var repository = new FakeRepository
        {
            Schema = BuildSchema(),
            DirectLookupResults =
            {
                ["Standard"] = new DbProductLookupRow
                {
                    ProductName = "可乐",
                    Specification = "500ml",
                    Price = 3.5m
                }
            }
        };

        var service = CreateService(repository);
        var result = await service.LookupAsync("6925303714857", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Standard", result!.BarcodeMatchedBy);
        Assert.False(repository.FunctionLookupCalled);
    }

    [Fact]
    public async Task LookupAsync_ShouldUseFunctionFallback_WhenDirectFieldsMiss()
    {
        var repository = new FakeRepository
        {
            Schema = BuildSchema(),
            FunctionLookupResult = new DbProductLookupRow
            {
                ProductName = "雪碧",
                Specification = "500ml",
                Price = 3m
            }
        };

        var service = CreateService(repository);
        var result = await service.LookupAsync("6901028075803", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(repository.FunctionLookupCalled);
        Assert.Equal("fn_strunitptype(B)", result!.BarcodeMatchedBy);
    }

    [Fact]
    public async Task LookupAsync_ShouldThrow_WhenPriceFieldUnavailable()
    {
        var repository = new FakeRepository
        {
            Schema = new SwcsSchemaSnapshot
            {
                Columns = new HashSet<string>(["Standard", "Barcode"], StringComparer.OrdinalIgnoreCase),
                HasBarcodeFunction = true
            }
        };

        var service = CreateService(repository);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LookupAsync("123", CancellationToken.None));
    }

    private static ProductLookupService CreateService(ISwcsProductLookupRepository repository)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new SwcsOptions
        {
            ProductTable = "dbo.Ptype",
            SpecificationField = "Standard",
            BarcodeFields = ["Standard", "Barcode"],
            PriceFields = ["RetailPrice", "Price1"],
            EnableFunctionFallback = true
        });

        return new ProductLookupService(repository, options, NullLogger<ProductLookupService>.Instance);
    }

    private static SwcsSchemaSnapshot BuildSchema()
    {
        return new SwcsSchemaSnapshot
        {
            Columns = new HashSet<string>(["Standard", "Barcode", "RetailPrice", "ptypeid"], StringComparer.OrdinalIgnoreCase),
            HasBarcodeFunction = true
        };
    }

    private sealed class FakeRepository : ISwcsProductLookupRepository
    {
        public required SwcsSchemaSnapshot Schema { get; init; }

        public Dictionary<string, DbProductLookupRow> DirectLookupResults { get; } = new(StringComparer.OrdinalIgnoreCase);

        public DbProductLookupRow? FunctionLookupResult { get; init; }

        public bool FunctionLookupCalled { get; private set; }

        public Task<SwcsSchemaSnapshot> GetSchemaSnapshotAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Schema);
        }

        public Task<DbProductLookupRow?> LookupByFieldAsync(
            string barcode,
            string barcodeField,
            string priceField,
            string specificationField,
            CancellationToken cancellationToken)
        {
            DirectLookupResults.TryGetValue(barcodeField, out var result);
            return Task.FromResult(result);
        }

        public Task<DbProductLookupRow?> LookupByFunctionAsync(
            string barcode,
            string priceField,
            string specificationField,
            CancellationToken cancellationToken)
        {
            FunctionLookupCalled = true;
            return Task.FromResult(FunctionLookupResult);
        }
    }
}
