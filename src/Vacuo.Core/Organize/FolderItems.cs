using Vacuo.Core.Scanning;

namespace Vacuo.Core.Organize;

/// <summary>
/// The files Düzen may move from a folder: only those at its top level (B-161). Subfolders, hidden and
/// system files, shortcuts and desktop.ini are left alone; system folders are refused outright.
/// </summary>
public static class FolderItems
{
    private static readonly HashSet<string> ShortcutExtensions = new([".lnk", ".url"], StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<FolderItem> Read(string folder)
    {
        ScanRootGuard.ForThisMachine().EnsureAllowed(folder);
        return new DirectoryInfo(folder)
            .EnumerateFiles()
            .Where(IsTheUsersOwn)
            .Select(file => new FolderItem(file.Name, new DateTimeOffset(file.LastWriteTime), SourceHostOf(file.FullName)))
            .ToList();
    }

    private static bool IsTheUsersOwn(FileInfo file) =>
        (file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0
        && !ShortcutExtensions.Contains(file.Extension)
        && !file.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase);

    private static string? SourceHostOf(string path)
    {
        try
        {
            return ZoneIdentifier.SourceHost(File.ReadAllText(path + ZoneIdentifier.StreamSuffix));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Most files have no download note; one that cannot be read counts as none.
            return null;
        }
    }
}
