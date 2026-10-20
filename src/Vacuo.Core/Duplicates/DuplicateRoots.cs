using Vacuo.Core.Scanning;

namespace Vacuo.Core.Duplicates;

public enum AddFolderResult
{
    Added,
    AlreadyIncluded,
    SystemFolder,
}

/// <summary>
/// The folders the duplicate files screen searches: the defaults and whatever the user adds. A folder
/// already searched (or inside one) is not added twice, a wider one takes the place of those it holds,
/// system folders are refused, and at least one folder always stays.
/// </summary>
public sealed class DuplicateRoots(IEnumerable<string> folders, ScanRootGuard guard)
{
    private readonly List<string> _folders = [.. folders];

    public IReadOnlyList<string> Folders => _folders;

    public AddFolderResult Add(string folder)
    {
        if (!guard.IsAllowed(folder))
        {
            return AddFolderResult.SystemFolder;
        }

        if (_folders.Any(listed => IsSameOrInside(folder, listed)))
        {
            return AddFolderResult.AlreadyIncluded;
        }

        _folders.RemoveAll(listed => IsSameOrInside(listed, folder));
        _folders.Add(folder);
        return AddFolderResult.Added;
    }

    /// <summary>False when the folder is the last one: a search needs somewhere to look.</summary>
    public bool Remove(string folder)
    {
        if (_folders.Count <= 1)
        {
            return false;
        }

        return _folders.RemoveAll(listed => Normalize(listed).Equals(Normalize(folder), StringComparison.OrdinalIgnoreCase)) > 0;
    }

    private static bool IsSameOrInside(string path, string folder)
    {
        string candidate = Normalize(path);
        string parent = Normalize(folder);
        return candidate.Equals(parent, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
