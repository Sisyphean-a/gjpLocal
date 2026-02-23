namespace SwcsScanner.Api.Data;

public interface IProductUnitReadRepository
{
    Task<IReadOnlyList<DbProductUnitRow>> GetUnitsByProductIdAsync(
        string productId,
        string? matchedBarcode,
        CancellationToken cancellationToken);
}
