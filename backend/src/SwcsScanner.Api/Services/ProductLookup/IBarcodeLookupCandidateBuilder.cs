namespace SwcsScanner.Api.Services;

public interface IBarcodeLookupCandidateBuilder
{
    IReadOnlyList<string> Build(string barcode);
}
