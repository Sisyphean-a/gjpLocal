using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SwcsScanner.Api.Models.Responses;
using SwcsScanner.Api.Options;
using SwcsScanner.Api.Services;

namespace SwcsScanner.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v2/products")]
public sealed class ProductsController : ControllerBase
{
    private const int MinSearchKeywordLength = 2;
    private const int DefaultSearchLimit = 20;

    private readonly IProductLookupService _lookupService;
    private readonly SwcsOptions _swcsOptions;

    public ProductsController(IProductLookupService lookupService, IOptions<SwcsOptions> swcsOptions)
    {
        _lookupService = lookupService;
        _swcsOptions = swcsOptions.Value;
    }

    [HttpGet("lookup")]
    [EnableRateLimiting("lookup")]
    [ProducesResponseType<ApiEnvelope<ProductLookupResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiEnvelope<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiEnvelope<object>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiEnvelope<object>>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiEnvelope<ProductLookupResponse>>> Lookup(
        [FromQuery] string barcode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return BadRequest(ApiEnvelopeFactory.Failure(ApiErrorResponse.InvalidBarcode(), HttpContext.TraceIdentifier));
        }

        var result = await _lookupService.LookupAsync(barcode, cancellationToken);
        if (result is null)
        {
            return NotFound(ApiEnvelopeFactory.Failure(
                ApiErrorResponse.NotFoundBarcode(barcode),
                HttpContext.TraceIdentifier));
        }

        var pricingTable = string.IsNullOrWhiteSpace(_swcsOptions.PriceTable)
            ? _swcsOptions.ProductTable
            : _swcsOptions.PriceTable!;
        var pricingField = string.IsNullOrWhiteSpace(_swcsOptions.PriceColumn)
            ? string.Join(" | ", _swcsOptions.PriceFields)
            : _swcsOptions.PriceColumn!;

        var payload = new ProductLookupResponse(
            result.ProductId,
            result.ProductName,
            result.ProductCode,
            result.ProductShortCode,
            result.Specification,
            result.Price,
            result.MatchedBy,
            new ProductPricingMetaResponse(
                pricingTable,
                pricingField,
                !string.IsNullOrWhiteSpace(_swcsOptions.BarcodeTable) &&
                !string.IsNullOrWhiteSpace(_swcsOptions.PriceTable),
                _swcsOptions.PriceTypeId),
            result.CurrentUnit is null
                ? null
                : new ProductLookupUnitResponse(
                    result.CurrentUnit.UnitId,
                    result.CurrentUnit.UnitName,
                    result.CurrentUnit.UnitRate,
                    result.CurrentUnit.Price,
                    result.CurrentUnit.Barcodes,
                    result.CurrentUnit.IsMatchedUnit),
            result.Units
                .Select(unit => new ProductLookupUnitResponse(
                    unit.UnitId,
                    unit.UnitName,
                    unit.UnitRate,
                    unit.Price,
                    unit.Barcodes,
                    unit.IsMatchedUnit))
                .ToList());

        return Ok(ApiEnvelopeFactory.Success(payload, HttpContext.TraceIdentifier));
    }

    [HttpGet("search")]
    [EnableRateLimiting("lookup")]
    [ProducesResponseType<ApiEnvelope<ProductSearchResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiEnvelope<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiEnvelope<object>>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApiEnvelope<ProductSearchResponse>>> Search(
        [FromQuery] string keyword,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var normalizedKeyword = keyword?.Trim() ?? string.Empty;
        if (normalizedKeyword.Length < MinSearchKeywordLength)
        {
            return BadRequest(ApiEnvelopeFactory.Failure(
                ApiErrorResponse.InvalidSearchKeyword(MinSearchKeywordLength),
                HttpContext.TraceIdentifier));
        }

        var result = await _lookupService.SearchByBarcodeFragmentAsync(
            normalizedKeyword,
            limit ?? DefaultSearchLimit,
            cancellationToken);

        var items = result
            .Select(item => new ProductSearchItemResponse(
                item.ProductName,
                item.ProductCode,
                item.ProductShortCode,
                item.Specification,
                item.Price,
                item.Barcode,
                item.MatchedBy))
            .ToList();

        var payload = new ProductSearchResponse(normalizedKeyword, items.Count, items);
        return Ok(ApiEnvelopeFactory.Success(payload, HttpContext.TraceIdentifier));
    }
}
