using System.Text;

namespace SwcsScanner.Api.Services;

public sealed class BarcodeLookupCandidateBuilder : IBarcodeLookupCandidateBuilder
{
    public IReadOnlyList<string> Build(string barcode)
    {
        var input = barcode?.Trim() ?? string.Empty;
        if (input.Length == 0)
        {
            return [];
        }

        var candidates = new List<string>();
        AddCandidate(candidates, input);

        var digitsBuilder = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            if (char.IsDigit(ch))
            {
                digitsBuilder.Append(ch);
            }
        }

        var digits = digitsBuilder.ToString();
        if (digits.Length > 0)
        {
            AddCandidate(candidates, digits);
        }

        if (digits.Length >= 16 && digits.StartsWith("01", StringComparison.Ordinal))
        {
            var gtin14 = digits.Substring(2, 14);
            AddCandidate(candidates, gtin14);
            if (gtin14[0] == '0')
            {
                AddCandidate(candidates, gtin14[1..]);
            }
        }

        if (digits.Length == 14 && digits[0] == '0')
        {
            AddCandidate(candidates, digits[1..]);
        }
        else if (digits.Length == 13)
        {
            AddCandidate(candidates, $"0{digits}");
        }

        return candidates;
    }

    private static void AddCandidate(ICollection<string> candidates, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var existing in candidates)
        {
            if (string.Equals(existing, value, StringComparison.Ordinal))
            {
                return;
            }
        }

        candidates.Add(value);
    }
}
