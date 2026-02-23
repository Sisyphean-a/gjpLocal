using Microsoft.Extensions.Logging.Abstractions;
using SwcsScanner.Api.Data;
using SwcsScanner.Api.Options;
using SwcsScanner.Api.Services;

namespace SwcsScanner.Api.Tests;

public sealed class ProductLookupServiceTests
{
    [Fact]
    public void BuildCandidates_ShouldExpandGs1AndGtinVariants()
    {
        var builder = new BarcodeLookupCandidateBuilder();

        var candidates = builder.Build("(01)06923644237943");

        Assert.Equal(
            ["(01)06923644237943", "0106923644237943", "06923644237943", "6923644237943"],
            candidates);
    }

    [Fact]
    public void BuildCandidates_ShouldAddLeadingZeroVariant_ForEan13()
    {
        var builder = new BarcodeLookupCandidateBuilder();

        var candidates = builder.Build("6925303714857");

        Assert.Equal(["6925303714857", "06925303714857"], candidates);
    }

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
                    ProductCode = "10001",
                    ProductShortCode = "kl",
                    Specification = "500ml",
                    Price = 3.5m
                }
            }
        };

        var service = CreateService(repository);
        var result = await service.LookupAsync("6925303714857", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Standard", result!.MatchedBy);
        Assert.Equal("10001", result.ProductCode);
        Assert.Equal("kl", result.ProductShortCode);
        Assert.False(repository.FunctionLookupCalled);
    }

    [Fact]
    public async Task LookupAsync_ShouldPreferBarcodeTable_WhenBarcodeTableAndFieldBothMatch()
    {
        var repository = new FakeRepository
        {
            Schema = BuildSchema(),
            DirectLookupResults =
            {
                [""] = new DbProductLookupRow
                {
                    ProductName = "barcode-table-hit",
                    ProductCode = "bt-001",
                    ProductShortCode = "bt",
                    Specification = "500ml",
                    Price = 12.3m
                },
                ["Standard"] = new DbProductLookupRow
                {
                    ProductName = "field-hit",
                    ProductCode = "fd-001",
                    ProductShortCode = "fd",
                    Specification = "500ml",
                    Price = 9.9m
                }
            }
        };

        var service = CreateService(
            repository,
            new SwcsOptions
            {
                ProductTable = "dbo.Ptype",
                SpecificationField = "Standard",
                BarcodeFields = ["Standard", "Barcode"],
                PriceFields = ["RetailPrice", "Price1"],
                EnableFunctionFallback = true,
                BarcodeTable = "dbo.PBarcode",
                BarcodeColumn = "BarcodeTableColumn"
            });

        var result = await service.LookupAsync("6925303714857", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("BarcodeTableColumn", result!.MatchedBy);
        Assert.Equal("bt-001", result.ProductCode);
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
        Assert.Equal("fn_strunitptype(B)", result!.MatchedBy);
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
        Assert.Equal("LegacyCompositeLike", result!.MatchedBy);
        Assert.True(repository.FunctionLookupCalled);
        var firstCompositeIndex = repository.LookupCallTrace.FindIndex(call => call.StartsWith("composite:", StringComparison.Ordinal));
        var lastFunctionIndex = repository.LookupCallTrace.FindLastIndex(call => call.StartsWith("function:", StringComparison.Ordinal));
        Assert.True(firstCompositeIndex >= 0);
        Assert.True(lastFunctionIndex >= 0);
        Assert.True(firstCompositeIndex > lastFunctionIndex);
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
        Assert.Equal("LegacyCompositeLike", result!.MatchedBy);
        Assert.Contains("6923644237943", repository.CompositeLookupKeywords);
    }

    [Fact]
    public async Task LookupAsync_ShouldExposeCurrentUnit_AndUseCurrentUnitPrice()
    {
        var repository = new FakeRepository
        {
            Schema = BuildSchema(),
            DirectLookupResults =
            {
                ["Standard"] = new DbProductLookupRow
                {
                    ProductId = "000123",
                    ProductName = "cigarette",
                    Specification = "1x10",
                    Price = 28m,
                    MatchedUnitId = "2",
                    MatchedBarcode = "6901028218740"
                }
            },
            UnitRows =
            [
                new DbProductUnitRow
                {
                    UnitId = "1",
                    UnitName = "box",
                    UnitRate = "1",
                    Price = 28m,
                    BarcodeList = "6901028218740",
                    IsMatchedUnit = false
                },
                new DbProductUnitRow
                {
                    UnitId = "2",
                    UnitName = "carton",
                    UnitRate = "10",
                    Price = 280m,
                    BarcodeList = "16901028218747",
                    IsMatchedUnit = true
                }
            ]
        };

        var service = CreateService(repository);
        var result = await service.LookupAsync("6901028218740", CancellationToken.None);

        Assert.NotNull(result);
        Assert.NotNull(result!.CurrentUnit);
        Assert.Equal("2", result.CurrentUnit!.UnitId);
        Assert.Equal(280m, result.Price);
        Assert.Equal(2, result.Units.Count);
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
                    ProductCode = "08093",
                    ProductShortCode = "mnjn",
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
        Assert.Equal("08093", result[0].ProductCode);
        Assert.Equal("mnjn", result[0].ProductShortCode);
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

    private static ProductLookupService CreateService(
        ISwcsProductLookupRepository repository,
        SwcsOptions? overrideOptions = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(overrideOptions ?? new SwcsOptions
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

        public List<DbProductUnitRow> UnitRows { get; init; } = [];

        public List<string> LookupCallTrace { get; } = [];

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
            LookupCallTrace.Add($"field:{barcodeField}:{barcode}");
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
            LookupCallTrace.Add($"function:{barcode}");
            return Task.FromResult(FunctionLookupResult);
        }

        public Task<DbProductLookupRow?> LookupByCompositeKeywordAsync(
            string keyword,
            string? priceField,
            string? specificationField,
            CancellationToken cancellationToken)
        {
            LookupCallTrace.Add($"composite:{keyword}");
            CompositeLookupKeywords.Add(keyword);
            CompositeLookupResults.TryGetValue(keyword, out var result);
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<DbProductUnitRow>> GetUnitsByProductIdAsync(
            string productId,
            string? matchedBarcode,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DbProductUnitRow>>(UnitRows);
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
