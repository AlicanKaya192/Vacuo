using Vacuo.Core.Settings;

namespace Vacuo.Core.Updates;

/// <summary>
/// The "what's new" text from a release's notes (docs/releases/X.Y.Z.md, the release body): the part under
/// "## Türkçe" on a Turkish interface and under "## English" on any other. Notes without these parts are
/// shown whole; when only one language is there, it is shown rather than nothing.
/// </summary>
public static class ReleaseNotes
{
    private const string TurkishHeading = "## Türkçe";
    private const string EnglishHeading = "## English";
    private const string AnyHeading = "## ";

    public static string For(string body, string language)
    {
        string[] lines = body.ReplaceLineEndings("\n").Split('\n');
        string? turkish = Section(lines, TurkishHeading);
        string? english = Section(lines, EnglishHeading);
        if (turkish is null && english is null)
        {
            return string.Join('\n', lines).Trim();
        }

        return LanguageRule.IsTurkish(language) ? turkish ?? english! : english ?? turkish!;
    }

    private static string? Section(string[] lines, string heading)
    {
        int start = Array.FindIndex(lines, line => line.Trim().Equals(heading, StringComparison.OrdinalIgnoreCase));
        if (start < 0)
        {
            return null;
        }

        int end = Array.FindIndex(lines, start + 1, line => line.TrimStart().StartsWith(AnyHeading, StringComparison.Ordinal));
        return string.Join('\n', lines[(start + 1)..(end < 0 ? lines.Length : end)]).Trim();
    }
}
