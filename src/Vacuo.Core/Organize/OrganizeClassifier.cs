using System.Globalization;

namespace Vacuo.Core.Organize;

/// <summary>
/// Decides where one file goes under a rule, or <see langword="null"/> when it has no clear place and
/// stays where it is. Works only from the name, the last change and the recorded download source.
/// </summary>
public static class OrganizeClassifier
{
    private static readonly IReadOnlyDictionary<string, FileKind> KindByExtension = BuildKinds(
        (FileKind.Documents, [".pdf", ".doc", ".docx", ".odt", ".rtf", ".txt", ".md", ".xls", ".xlsx", ".ods", ".csv", ".ppt", ".pptx", ".odp", ".epub"]),
        (FileKind.Images, [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".svg", ".tif", ".tiff"]),
        (FileKind.Videos, [".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm", ".m4v"]),
        (FileKind.Music, [".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma"]),
        (FileKind.Archives, [".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso"]),
        (FileKind.Installers, [".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle"]),
        (FileKind.Code, [".py", ".ipynb", ".cs", ".js", ".ts", ".java", ".c", ".cpp", ".h", ".json", ".html", ".css", ".sql", ".r", ".go", ".rs", ".sh", ".ps1", ".yaml", ".yml"]));

    // Turkish and English. A trailing "*" also matches longer words ("fatura*" finds "faturası");
    // without it the whole word must match, so "lab" does not find "label" nor "exam" "example".
    private static readonly IReadOnlyList<(Topic Topic, string[] Keywords)> TopicKeywords =
    [
        (Topic.Invoices, ["fatura*", "invoice", "invoices", "receipt", "receipts", "makbuz*", "dekont*"]),
        (Topic.School, ["ödev*", "odev*", "homework", "assignment", "assignments", "lab", "sınav*", "sinav*", "exam", "exams", "quiz", "lecture", "lectures", "ders*", "vize*", "syllabus"]),
        (Topic.Work, ["cv", "resume", "özgeçmiş*", "ozgecmis*", "sözleşme*", "sozlesme*", "contract", "contracts", "teklif*", "proposal"]),
        (Topic.Travel, ["bilet*", "ticket", "tickets", "boarding", "rezervasyon*", "reservation", "booking", "otel*", "hotel", "itinerary"]),
        (Topic.Screenshots, ["screenshot", "screenshots", "screen shot", "ekran görüntü*", "ekran alıntı*"]),
    ];

    public static OrganizeTarget? TargetOf(FolderItem item, OrganizeRule rule)
    {
        string? key = rule switch
        {
            OrganizeRule.ByType => KindOf(item.Name)?.ToString(),
            OrganizeRule.ByDate => item.LastWrite.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            OrganizeRule.BySource => item.SourceHost,
            OrganizeRule.ByTopic => TopicOf(item.Name)?.ToString(),
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule, "Unknown organize rule."),
        };
        return key is null ? null : new OrganizeTarget(rule, key);
    }

    private static FileKind? KindOf(string fileName) =>
        KindByExtension.TryGetValue(Path.GetExtension(fileName), out FileKind kind) ? kind : null;

    private static Topic? TopicOf(string fileName)
    {
        string words = Words(Path.GetFileNameWithoutExtension(fileName));
        foreach ((Topic topic, string[] keywords) in TopicKeywords)
        {
            if (keywords.Any(keyword => Contains(words, keyword)))
            {
                return topic;
            }
        }

        return null;
    }

    // " ekran görüntüsü 2026 10 20 " for "Ekran görüntüsü 2026-10-20": every word between single spaces.
    private static string Words(string name)
    {
        string[] words = name.ToLowerInvariant().Split(
            name.Where(character => !char.IsLetterOrDigit(character)).Distinct().ToArray(),
            StringSplitOptions.RemoveEmptyEntries);
        return " " + string.Join(' ', words) + " ";
    }

    private static bool Contains(string words, string keyword) =>
        keyword.EndsWith('*')
            ? words.Contains(" " + keyword[..^1], StringComparison.Ordinal)
            : words.Contains(" " + keyword + " ", StringComparison.Ordinal);

    private static Dictionary<string, FileKind> BuildKinds(params (FileKind Kind, string[] Extensions)[] groups) =>
        groups
            .SelectMany(group => group.Extensions.Select(extension => (extension, group.Kind)))
            .ToDictionary(pair => pair.extension, pair => pair.Kind, StringComparer.OrdinalIgnoreCase);
}
