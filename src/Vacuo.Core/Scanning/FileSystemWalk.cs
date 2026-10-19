namespace Vacuo.Core.Scanning;

internal static class FileSystemWalk
{
    // Hidden and system entries are listed too: caches are often hidden.
    private static readonly EnumerationOptions OneLevelEverything = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
    };

    /// <summary>
    /// Lists one folder level. Returns false when the folder cannot be read, so the caller
    /// counts it as skipped instead of silently reporting a smaller total.
    /// </summary>
    public static bool TryList(DirectoryInfo directory, out IReadOnlyList<FileSystemInfo> entries)
    {
        try
        {
            entries = directory.EnumerateFileSystemInfos("*", OneLevelEverything).ToList();
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            entries = [];
            return false;
        }
    }

    /// <summary>Junctions and symbolic links point elsewhere; scanning never follows or counts them.</summary>
    public static bool IsLink(FileSystemInfo entry) => entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
}
