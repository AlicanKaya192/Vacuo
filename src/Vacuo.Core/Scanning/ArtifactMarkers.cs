namespace Vacuo.Core.Scanning;

/// <summary>
/// File checks that tell a real build artifact from a folder that merely has the same name.
/// An unreadable folder never counts as a match: when in doubt, the folder stays out of the bucket.
/// </summary>
internal static class ArtifactMarkers
{
    public static bool HasFile(DirectoryInfo directory, string fileName) =>
        File.Exists(Path.Combine(directory.FullName, fileName));

    public static bool HasSiblingFile(DirectoryInfo directory, string fileName) =>
        directory.Parent is { } parent && HasFile(parent, fileName);

    public static bool HasSiblingFileWithExtension(DirectoryInfo directory, string extension) =>
        directory.Parent is { } parent
        && FileSystemWalk.TryList(parent, out IReadOnlyList<FileSystemInfo> entries)
        && entries.Any(entry => entry is FileInfo file && HasExtension(file, extension));

    public static bool HoldsOnlyFilesWithExtension(DirectoryInfo directory, string extension) =>
        FileSystemWalk.TryList(directory, out IReadOnlyList<FileSystemInfo> entries)
        && entries.All(entry => entry is FileInfo file && HasExtension(file, extension));

    private static bool HasExtension(FileInfo file, string extension) =>
        file.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase);
}
