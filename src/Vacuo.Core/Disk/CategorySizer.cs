using Vacuo.Core.Scanning;

namespace Vacuo.Core.Disk;

/// <summary>
/// Adds up each known folder like <see cref="DirectorySizer"/>, with two differences: a root inside
/// another root is left to its own category, so nothing is counted twice; and files whose content is
/// not on this disk (OneDrive's online-only placeholders) count nothing. Only reads.
/// </summary>
public static class CategorySizer
{
    public static IReadOnlyList<CategorySize> Measure(IReadOnlyList<CategoryRoot> roots)
    {
        var rootPaths = new HashSet<string>(roots.Select(root => Normalize(root.Path)), StringComparer.OrdinalIgnoreCase);
        return roots.Select(root => new CategorySize(root, MeasureOne(new DirectoryInfo(root.Path), rootPaths))).ToList();
    }

    private static DirectorySize MeasureOne(DirectoryInfo root, IReadOnlySet<string> rootPaths)
    {
        if (!root.Exists)
        {
            return new DirectorySize(0, 0);
        }

        long bytes = 0;
        int skippedEntries = 0;
        var pending = new Stack<DirectoryInfo>([root]);

        while (pending.TryPop(out DirectoryInfo? current))
        {
            if (!FileSystemWalk.TryList(current, out IReadOnlyList<FileSystemInfo> entries))
            {
                skippedEntries++;
                continue;
            }

            foreach (FileSystemInfo entry in entries.Where(entry => !FileSystemWalk.IsLink(entry)))
            {
                if (entry is FileInfo file)
                {
                    bytes += FileSystemWalk.IsOnThisDisk(file) ? file.Length : 0;
                }
                else if (!rootPaths.Contains(Normalize(entry.FullName)))
                {
                    pending.Push((DirectoryInfo)entry);
                }
            }
        }

        return new DirectorySize(bytes, skippedEntries);
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
