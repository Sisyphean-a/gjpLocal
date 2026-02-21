using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SwcsScanner.Api.Models.Responses;
using SwcsScanner.Api.Services;

namespace SwcsScanner.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public sealed class ProductsController : ControllerBase
{
    private const int MinSearchKeywordLength = 2;
    private const int DefaultSearchLimit = 20;

    private readonly IProductLookupService _lookupService;

    public ProductsController(IProductLookupService lookupService)
    {
        _lookupService = lookupService;
    }

    [HttpGet("lookup")]
    [EnableRateLimiting("lookup")]
    [ProducesResponseType<ProductLookupResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ProductLookupResponse>> Lookup(
        [FromQuery] string barcode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return BadRequest(ApiErrorResponse.InvalidBarcode());
        }

        var result = await _lookupService.LookupAsync(barcode, cancellationToken);
        if (result is null)
        {
            return NotFound(ApiErrorResponse.NotFoundBarcode(barcode));
        }

        return Ok(new ProductLookupResponse(
            result.ProductName,
            result.Specification,
            result.Price,
            result.BarcodeMatchedBy));
    }

    [HttpGet("search")]
    [EnableRateLimiting("lookup")]
    [ProducesResponseType<ProductSearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiErrorResponse>(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ProductSearchResponse>> Search(
        [FromQuery] string keyword,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var normalizedKeyword = keyword?.Trim() ?? string.Empty;
        if (normalizedKeyword.Length < MinSearchKeywordLength)
        {
            return BadRequest(ApiErrorResponse.InvalidSearchKeyword(MinSearchKeywordLength));
        }

        var result = await _lookupService.SearchByBarcodeFragmentAsync(
            normalizedKeyword,
            limit ?? DefaultSearchLimit,
            cancellationToken);

        var items = result
            .Select(item => new ProductSearchItemResponse(
                item.ProductName,
                item.Specification,
                item.Price,
                item.Barcode,
                item.BarcodeMatchedBy))
            .ToList();

        return Ok(new ProductSearchResponse(normalizedKeyword, items.Count, items));
    }
}
