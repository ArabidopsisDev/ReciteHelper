using System.Text.RegularExpressions;

namespace ReciteHelper.Core.Services;

public static partial class SourceEvidenceOrder
{
    public static string Normalize(string text) => Whitespace().Replace(text, string.Empty);

    public static int Find(string normalizedSource, string excerpt)
    {
        var normalizedExcerpt = Normalize(excerpt);
        return normalizedExcerpt.Length == 0 ? -1 : normalizedSource.IndexOf(normalizedExcerpt, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
