namespace SwcsScanner.Api.Data;

public sealed class SwcsSchemaSnapshot
{
    public required HashSet<string> Columns { get; init; }

    public required bool HasBarcodeFunction { get; init; }
}
