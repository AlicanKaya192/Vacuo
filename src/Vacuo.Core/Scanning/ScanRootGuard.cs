namespace Vacuo.Core.Scanning;

/// <summary>
/// Refuses scan roots that are, or contain, a system folder, so no scan ever walks
/// Windows or installed programs (project rule 5).
/// </summary>
public sealed class ScanRootGuard
{
    private readonly IReadOnlyList<string> _protectedFolders;

    public ScanRootGuard(IEnumerable<string> protectedFolders)
    {
        _protectedFolders = protectedFolders.Select(Normalize).ToList();
    }

    public static ScanRootGuard ForThisMachine()
    {
        Environment.SpecialFolder[] systemFolders =
        [
            Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonApplicationData,
        ];
        return new ScanRootGuard(systemFolders.Select(Environment.GetFolderPath).Where(path => path.Length > 0));
    }

    public bool IsAllowed(string folder)
    {
        string candidate = Normalize(folder);
        return !_protectedFolders.Any(
            protectedFolder => IsSameOrInside(candidate, protectedFolder) || IsSameOrInside(protectedFolder, candidate));
    }

    public void EnsureAllowed(string folder)
    {
        if (!IsAllowed(folder))
        {
            throw new ArgumentException($"'{folder}' is or contains a system folder, so it cannot be scanned.", nameof(folder));
        }
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsSameOrInside(string path, string folder)
    {
        string folderWithSeparator = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
        return path.Equals(folder, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(folderWithSeparator, StringComparison.OrdinalIgnoreCase);
    }
}
