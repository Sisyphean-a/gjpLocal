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
                    ProductName = "cola",
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
                ProductName = "sprite",
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
    public async Task LookupAsync_ShouldUseCompositeFallback_WhenExactMatchersMiss()
    {
        var repository = new FakeRepository
        {
            Schema = BuildSchema(),
            CompositeLookupResults =
            {
                ["6923644237943"] = new DbProductLookupRow
                {
                    ProductName = "milk",
                    Specification = "200ml",
                    Price = 39.9m
                }
            }
        };

        var service = CreateService(repository);
        var result = await service.LookupAsync("6923644237943", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("LegacyCompositeLike", result!.BarcodeMatchedBy);
        Assert.Contains("6923644237943", repository.CompositeLookupKeywords);
    }

    [Fact]
    public async Task LookupAsync_ShouldNormalizeGs1Input_ForCompositeFallback()
    {
        var repository = new FakeRepository
        {
            Schema = BuildSchema(),
            CompositeLookupResults =
            {
                ["6923644237943"] = new DbProductLookupRow
                {
                    ProductName = "milk",
                    Specification = "200ml",
                    Price = 39.9m
                }
            }
        };

        var service = CreateService(repository);
        var result = await service.LookupAsync("(01)06923644237943", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("LegacyCompositeLike", result!.BarcodeMatchedBy);
        Assert.Contains("6923644237943", repository.CompositeLookupKeywords);
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

    [Fact]
    public async Task SearchByBarcodeFragmentAsync_ShouldReturnEmpty_WhenKeywordTooShort()
    {
        var repository = new FakeRepository
        {
            Schema = BuildSchema()
        };

        var service = CreateService(repository);
        var result = await service.SearchByBarcodeFragmentAsync("9", 20, CancellationToken.None);

        Assert.Empty(result);
        Assert.False(repository.SearchCalled);
    }

    [Fact]
    public async Task SearchByBarcodeFragmentAsync_ShouldNormalizeLimit_AndMapResults()
    {
        var repository = new FakeRepository
        {
            Schema = BuildSchema(),
            SearchResults =
            [
                new DbProductSearchRow
                {
                    ProductName = "sprite",
                    Specification = "500ml",
                    Price = 3m,
                    Barcode = "6901028075803",
                    BarcodeMatchedBy = "Barcode"
                }
            ]
        };

        var service = CreateService(repository);
        var result = await service.SearchByBarcodeFragmentAsync("6901", 200, CancellationToken.None);

        Assert.True(repository.SearchCalled);
        Assert.Equal(50, repository.LastSearchLimit);
        Assert.Single(result);
        Assert.Equal("6901028075803", result[0].Barcode);
    }

    [Fact]
    public async Task SearchByBarcodeFragmentAsync_ShouldPassOnlyExistingBarcodeFields()
    {
        var repository = new FakeRepository
        {
            Schema = new SwcsSchemaSnapshot
            {
                Columns = new HashSet<string>(["Barcode", "RetailPrice", "ptypeid"], StringComparer.OrdinalIgnoreCase),
                HasBarcodeFunction = true
            }
        };

        var service = CreateService(repository);
        await service.SearchByBarcodeFragmentAsync("6925", 20, CancellationToken.None);

        Assert.Equal(["Barcode"], repository.LastSearchBarcodeFields);
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

        public Dictionary<string, DbProductLookupRow> CompositeLookupResults { get; } = new(StringComparer.Ordinal);

        public List<string> CompositeLookupKeywords { get; } = [];

        public List<DbProductSearchRow> SearchResults { get; init; } = [];

        public bool FunctionLookupCalled { get; private set; }

        public bool SearchCalled { get; private set; }

        public int LastSearchLimit { get; private set; }

        public IReadOnlyList<string> LastSearchBarcodeFields { get; private set; } = [];

        public Task<SwcsSchemaSnapshot> GetSchemaSnapshotAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Schema);
        }

        public Task<DbProductLookupRow?> LookupByFieldAsync(
            string barcode,
            string barcodeField,
            string? priceField,
            string? specificationField,
            CancellationToken cancellationToken)
        {
            DirectLookupResults.TryGetValue(barcodeField, out var result);
            return Task.FromResult(result);
        }

        public Task<DbProductLookupRow?> LookupByFunctionAsync(
            string barcode,
            string? priceField,
            string? specificationField,
            CancellationToken cancellationToken)
        {
            FunctionLookupCalled = true;
            return Task.FromResult(FunctionLookupResult);
        }

        public Task<DbProductLookupRow?> LookupByCompositeKeywordAsync(
            string keyword,
            string? priceField,
            string? specificationField,
            CancellationToken cancellationToken)
        {
            CompositeLookupKeywords.Add(keyword);
            CompositeLookupResults.TryGetValue(keyword, out var result);
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<DbProductSearchRow>> SearchByBarcodeFragmentAsync(
            string keyword,
            IReadOnlyList<string> barcodeFields,
            string? priceField,
            string? specificationField,
            int limit,
            CancellationToken cancellationToken)
        {
            SearchCalled = true;
            LastSearchLimit = limit;
            LastSearchBarcodeFields = barcodeFields.ToList();
            return Task.FromResult<IReadOnlyList<DbProductSearchRow>>(SearchResults);
        }
    }
}
