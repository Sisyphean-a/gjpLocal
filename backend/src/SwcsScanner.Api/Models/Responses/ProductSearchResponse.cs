namespace SwcsScanner.Api.Models.Responses;

public sealed record ProductSearchResponse(
    string Keyword,
    int Count,
    IReadOnlyList<ProductSearchItemResponse> Items);
