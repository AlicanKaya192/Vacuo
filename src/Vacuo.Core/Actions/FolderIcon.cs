using System.Security.Cryptography;

namespace Vacuo.Core.Actions;

/// <summary>
/// The two files a folder icon leaves inside a folder Düzen made (Aşama 3h), each by the SHA-256 of
/// what Vacuo wrote, or <see langword="null"/> when that file was never written.
/// </summary>
public sealed record FolderIcon(string? DesktopIniSha256, string? IconFileSha256)
{
    public const string DesktopIni = "desktop.ini";
    public const string IconFile = "folder.ico";

    /// <summary>Fingerprints the icon files now inside <paramref name="folder"/>, or <see langword="null"/> when there are none.</summary>
    public static FolderIcon? Of(string folder)
    {
        string? desktopIni = HashOf(Path.Combine(folder, DesktopIni));
        string? iconFile = HashOf(Path.Combine(folder, IconFile));
        return desktopIni is null && iconFile is null ? null : new FolderIcon(desktopIni, iconFile);
    }

    /// <summary>The icon files still exactly as Vacuo wrote them; a changed one is the user's now and is not listed.</summary>
    public IReadOnlyList<string> UnchangedFilesIn(string folder) =>
        new[] { (Name: DesktopIni, Sha256: DesktopIniSha256), (Name: IconFile, Sha256: IconFileSha256) }
            .Where(file => file.Sha256 is not null && HashOf(Path.Combine(folder, file.Name)) == file.Sha256)
            .Select(file => Path.Combine(folder, file.Name))
            .ToList();

    private static string? HashOf(string path) =>
        File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;
}
