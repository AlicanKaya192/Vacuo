using Vacuo.Core.Scanning;

namespace Vacuo.Core.Organize;

/// <summary>
/// The files Düzen may move from a folder: only those at its top level (B-161). Subfolders, hidden and
/// system files, shortcuts and desktop.ini are left alone; system folders are refused outright. Given
/// <paramref name="gameShortcuts"/>, the shortcuts of games are read too; the automatic run does not ask for them,
/// since a shortcut moving by itself is harder to find again than a file.
/// </summary>
public static class FolderItems
{
    public static IReadOnlyList<FolderItem> Read(string folder, IShortcutReader? gameShortcuts = null)
    {
        ScanRootGuard.ForThisMachine().EnsureAllowed(folder);
        return new DirectoryInfo(folder)
            .EnumerateFiles()
            .Where(IsVisible)
            .Select(file => (File: file, IsGame: gameShortcuts is not null && GameShortcut.IsGame(file.FullName, gameShortcuts)))
            .Where(found => found.IsGame || IsTheUsersOwn(found.File))
            .Select(found => new FolderItem(found.File.Name, new DateTimeOffset(found.File.LastWriteTime), SourceHostOf(found.File.FullName), found.IsGame))
            .ToList();
    }

    private static bool IsVisible(FileInfo file) => (file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0;

    private static bool IsTheUsersOwn(FileInfo file) =>
        !GameShortcut.IsShortcut(file.Name)
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
