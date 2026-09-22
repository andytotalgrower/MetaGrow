using System.Text.RegularExpressions;
using F23.StringSimilarity;

namespace MetaGrow.Web.Services;

public static class RecordNameMatching
{
    public static bool SameName(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) &&
        string.Equals(CollapseSpaces(first), CollapseSpaces(second), StringComparison.OrdinalIgnoreCase);

    private static string CollapseSpaces(string? name) => Regex.Replace(name?.Trim() ?? "", @"\s+", " ");
    private static string Normalize(string? name) => new((name ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static bool LikelyDuplicate(string? first, string? second)
    {
        var a = Normalize(first);
        var b = Normalize(second);
        if (a.Length == 0 || b.Length == 0) return false;
        if (a == b) return true;
        // Numbered farms and blocks commonly represent distinct records.
        var aDigits = new string(a.Where(char.IsDigit).ToArray());
        var bDigits = new string(b.Where(char.IsDigit).ToArray());
        if (aDigits != bDigits && aDigits.Length > 0 && bDigits.Length > 0) return false;
        var length = Math.Min(a.Length, b.Length);
        var limit = length >= 8 ? 2 : length >= 4 ? 1 : 0;
        return Math.Abs(a.Length - b.Length) <= limit && new Damerau().Distance(a, b) <= limit;
    }

    public static bool Suggest(string? input, string? existing) =>
        LikelyDuplicate(input, existing) ||
        (Normalize(input).Length >= 3 && Normalize(existing).Contains(Normalize(input), StringComparison.Ordinal));
}
