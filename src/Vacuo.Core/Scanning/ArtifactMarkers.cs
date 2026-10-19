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

    /// <summary>
    /// A Python environment can only be rebuilt from a package list next to it; without one,
    /// nobody knows what was installed.
    /// </summary>
    public static bool HasPythonPackageListBeside(DirectoryInfo directory) =>
        directory.Parent is { } parent
        && FileSystemWalk.TryList(parent, out IReadOnlyList<FileSystemInfo> entries)
        && entries.Any(entry => entry is FileInfo file && IsPythonPackageList(file.Name));

    public static bool HoldsOnlyFilesWithExtension(DirectoryInfo directory, string extension) =>
        FileSystemWalk.TryList(directory, out IReadOnlyList<FileSystemInfo> entries)
        && entries.All(entry => entry is FileInfo file && HasExtension(file, extension));

    private static bool HasExtension(FileInfo file, string extension) =>
        file.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> PythonPackageLists = new(
        ["pyproject.toml", "setup.py", "setup.cfg", "Pipfile", "environment.yml", "uv.lock", "poetry.lock"],
        StringComparer.OrdinalIgnoreCase);

    private static bool IsPythonPackageList(string fileName) =>
        PythonPackageLists.Contains(fileName)
        || (fileName.StartsWith("requirements", StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
}
