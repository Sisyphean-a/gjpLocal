namespace SwcsScanner.Api.Models.Responses;

public sealed record ApiErrorResponse(string Code, string Message)
{
    public static ApiErrorResponse InvalidBarcode() => new("INVALID_BARCODE", "条码不能为空。");

    public static ApiErrorResponse NotFoundBarcode(string barcode) => new("PRODUCT_NOT_FOUND", $"未找到条码 {barcode} 对应的商品。");

    public static ApiErrorResponse InvalidCredential() => new("INVALID_CREDENTIAL", "用户名或密码错误。");

    public static ApiErrorResponse TooManyRequests() => new("TOO_MANY_REQUESTS", "请求过于频繁，请稍后重试。");

    public static ApiErrorResponse ServerError() => new("SERVER_ERROR", "服务器内部错误，请联系管理员。");
}
