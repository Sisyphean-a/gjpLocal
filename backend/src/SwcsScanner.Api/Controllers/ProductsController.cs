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
}
