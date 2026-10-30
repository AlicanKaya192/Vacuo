using System.Globalization;
using Vacuo.Core.Disk;
using Vacuo.Core.Formatting;
using Vacuo.Core.Maintenance;
using Vacuo.Core.Organize;
using Vacuo.Core.Settings;

namespace Vacuo.Core.Notices;

/// <summary>What a notification says: a short title and one or two sentences.</summary>
public sealed record NoticeText(string Title, string Body);

/// <summary>
/// The helper's sentences (ADR-004, vacuo-kurallar 16). The helper has no window of the panel's and cannot
/// read its .resw files, so, like the tray's, its few words live here in both languages. The Desktop folder
/// names must be Düzen's own (Organize_Kind_*), so a folder the helper makes is the one the panel makes.
/// </summary>
public static class NoticeTexts
{
    private static readonly Words Turkish = new(
        MaintenanceTitle: "Otomatik Bakım",
        Moved: "{0} öğe ({1}) Geri Dönüşüm Kutusu'na taşındı; Günlük'ten geri alabilirsin. Yer, kutu boşaltılınca açılır.",
        MovedOne: "1 öğe ({1}) Geri Dönüşüm Kutusu'na taşındı; Günlük'ten geri alabilirsin. Yer, kutu boşaltılınca açılır.",
        Organized: "Masaüstünde {0} dosya klasörlere yerleşti.",
        OrganizedOne: "Masaüstünde 1 dosya bir klasöre yerleşti.",
        Stopped: "Sen dönünce durdu; kalanı bir sonraki bakımda.",
        MemoryTitle: "Bellek sıkışık",
        MemoryBody: "Programlar yavaşlayabilir. Kullanmadığın programları kapatmak bellek açar; Vacuo hiçbir şeyi kendiliğinden kapatmaz.",
        DiskTitle: "Disk dolmak üzere",
        DiskBody: "{0} sürücüsünde {1} boş yer kaldı. Tıkla, neyin yer kapladığını Disk ekranında gör.",
        DiskGrewMost: "En çok büyüyen: {0} ({1}).",
        OrganizeDueTitle: "Düzen zamanı",
        OrganizeDue: "{0}: {1} dosya düzenlenebilir. Tıkla, önizle; sen onaylamadan hiçbir şey taşınmaz.",
        OrganizeDueOne: "{0}: 1 dosya düzenlenebilir. Tıkla, önizle; sen onaylamadan hiçbir şey taşınmaz.",
        Desktop: "Masaüstü",
        Downloads: "İndirilenler",
        Kinds: new Dictionary<FileKind, string>
        {
            [FileKind.Documents] = "Belgeler",
            [FileKind.Images] = "Resimler",
            [FileKind.Videos] = "Videolar",
            [FileKind.Music] = "Müzik",
            [FileKind.Archives] = "Arşivler",
            [FileKind.Installers] = "Kurulum dosyaları",
            [FileKind.Code] = "Kod",
            [FileKind.Games] = "Oyunlar",
        });

    private static readonly Words English = new(
        MaintenanceTitle: "Automatic maintenance",
        Moved: "{0} items ({1}) went to the Recycle Bin; you can put them back from the Journal. The space frees up when the bin is emptied.",
        MovedOne: "1 item ({1}) went to the Recycle Bin; you can put it back from the Journal. The space frees up when the bin is emptied.",
        Organized: "{0} files on the Desktop were sorted into folders.",
        OrganizedOne: "1 file on the Desktop was sorted into a folder.",
        Stopped: "It stopped when you came back; the rest waits for the next run.",
        MemoryTitle: "Memory is tight",
        MemoryBody: "Programs may slow down. Closing the ones you are not using frees memory; Vacuo never closes anything by itself.",
        DiskTitle: "The disk is almost full",
        DiskBody: "{1} free is left on {0}. Click to see on the Disk screen what takes up the space.",
        DiskGrewMost: "Grew the most: {0} ({1}).",
        OrganizeDueTitle: "Time to tidy up",
        OrganizeDue: "{0}: {1} files can be sorted. Click to preview; nothing moves until you approve.",
        OrganizeDueOne: "{0}: 1 file can be sorted. Click to preview; nothing moves until you approve.",
        Desktop: "Desktop",
        Downloads: "Downloads",
        Kinds: new Dictionary<FileKind, string>
        {
            [FileKind.Documents] = "Documents",
            [FileKind.Images] = "Pictures",
            [FileKind.Videos] = "Videos",
            [FileKind.Music] = "Music",
            [FileKind.Archives] = "Archives",
            [FileKind.Installers] = "Installers",
            [FileKind.Code] = "Code",
            [FileKind.Games] = "Games",
        });

    /// <summary>Null when the run neither moved nor sorted anything: there is nothing to tell.</summary>
    public static NoticeText? ForMaintenance(MaintenanceSummary summary, string language)
    {
        if (summary.Moved == 0 && summary.Organized == 0)
        {
            return null;
        }

        Words words = WordsFor(language);
        CultureInfo culture = CultureInfo.GetCultureInfo(language);
        var sentences = new List<string>();
        if (summary.Moved > 0)
        {
            string format = summary.Moved == 1 ? words.MovedOne : words.Moved;
            sentences.Add(string.Format(culture, format, summary.Moved, ByteSizeFormatter.Format(summary.Bytes, culture)));
        }

        if (summary.Organized > 0)
        {
            sentences.Add(string.Format(culture, summary.Organized == 1 ? words.OrganizedOne : words.Organized, summary.Organized));
        }

        if (summary.Stopped)
        {
            sentences.Add(words.Stopped);
        }

        return new NoticeText(words.MaintenanceTitle, string.Join(' ', sentences));
    }

    /// <summary>A reminder only asks: the click opens the preview, and only its confirm card moves anything.</summary>
    public static NoticeText ForOrganizeDue(string folder, int files, string language)
    {
        Words words = WordsFor(language);
        string format = files == 1 ? words.OrganizeDueOne : words.OrganizeDue;
        return new NoticeText(words.OrganizeDueTitle, string.Format(CultureInfo.GetCultureInfo(language), format, FolderLabel(folder, words), files));
    }

    // The two folders Düzen offers go by the names it shows; on disk they are English on a Turkish Windows too.
    private static string FolderLabel(string folder, Words words)
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        string trimmed = Path.TrimEndingDirectorySeparator(folder);
        return string.Equals(trimmed, desktop, StringComparison.OrdinalIgnoreCase) ? words.Desktop
            : string.Equals(trimmed, downloads, StringComparison.OrdinalIgnoreCase) ? words.Downloads
            : Path.GetFileName(trimmed);
    }

    public static NoticeText ForTightMemory(string language)
    {
        Words words = WordsFor(language);
        return new NoticeText(words.MemoryTitle, words.MemoryBody);
    }

    /// <summary><paramref name="grewMost"/> is known only once there is a disk history to compare.</summary>
    public static NoticeText ForLowDisk(long freeBytes, string drive, string language, FolderGrowth? grewMost = null)
    {
        Words words = WordsFor(language);
        CultureInfo culture = CultureInfo.GetCultureInfo(language);
        string body = string.Format(culture, words.DiskBody, drive, ByteSizeFormatter.Format(freeBytes, culture));
        if (grewMost is not null)
        {
            string grew = ByteSizeFormatter.FormatChange(grewMost.GrownBytes, culture);
            body += " " + string.Format(culture, words.DiskGrewMost, PathNames.Short(grewMost.Path), grew);
        }

        return new NoticeText(words.DiskTitle, body);
    }

    public static string DesktopFolderName(FileKind kind, string language) => WordsFor(language).Kinds[kind];

    private static Words WordsFor(string language) => LanguageRule.IsTurkish(language) ? Turkish : English;

    private sealed record Words(
        string MaintenanceTitle,
        string Moved,
        string MovedOne,
        string Organized,
        string OrganizedOne,
        string Stopped,
        string MemoryTitle,
        string MemoryBody,
        string DiskTitle,
        string DiskBody,
        string DiskGrewMost,
        string OrganizeDueTitle,
        string OrganizeDue,
        string OrganizeDueOne,
        string Desktop,
        string Downloads,
        IReadOnlyDictionary<FileKind, string> Kinds);
}
