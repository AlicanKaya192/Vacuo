using System.Security.Cryptography;
using Vacuo.Core.Scanning;

namespace Vacuo.Core.Duplicates;

public sealed record DuplicateFile(string Path, DateTime LastWriteTimeUtc);

/// <summary>Files with the same content. Keeping one, the others are <see cref="WastedBytes"/>.</summary>
public sealed record DuplicateGroup(long FileBytes, IReadOnlyList<DuplicateFile> Files)
{
    public long WastedBytes => FileBytes * (Files.Count - 1);
}

/// <summary>Groups, largest waste first; <paramref name="SkippedEntries"/> counts folders and files that could not be read.</summary>
public sealed record DuplicateScan(IReadOnlyList<DuplicateGroup> Groups, int SkippedEntries);

/// <summary>
/// Finds files with identical content in three narrowing steps: same length, then the same hash
/// of the first 64 KB, then the same SHA-256 of the whole file, so most files are never read in
/// full. Only reads. Links, code repositories and tool folders are not searched, empty files never
/// count, and online-only files are left alone (reading them would download them).
/// </summary>
public static class DuplicateFinder
{
    public const int PartialHashBytes = 64 * 1024;
    public const long DefaultMinimumBytes = 1024 * 1024;

    /// <summary>Where copies pile up (plan Aşama 3d B); the user can add other folders.</summary>
    public static IReadOnlyList<string> DefaultRoots() =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
    ];

    public static DuplicateScan Find(IReadOnlyList<string> roots, long minimumBytes, CancellationToken cancellation)
    {
        int skippedEntries = 0;
        Dictionary<long, List<FileInfo>> byLength = ListFiles(roots, Math.Max(1, minimumBytes), cancellation, ref skippedEntries);

        var groups = new List<DuplicateGroup>();
        foreach ((long length, List<FileInfo> sameLength) in byLength.Where(pair => pair.Value.Count > 1))
        {
            foreach (List<FileInfo> samePartial in GroupByHash(sameLength, PartialHash, cancellation, ref skippedEntries))
            {
                // A file no longer than the partial hash has already been hashed whole.
                IEnumerable<List<FileInfo>> sameContent = length <= PartialHashBytes
                    ? [samePartial]
                    : GroupByHash(samePartial, FullHash, cancellation, ref skippedEntries);
                groups.AddRange(sameContent.Select(files => new DuplicateGroup(
                    length,
                    files.Select(file => new DuplicateFile(file.FullName, file.LastWriteTimeUtc)).OrderBy(file => file.Path).ToList())));
            }
        }

        return new DuplicateScan(
            groups.OrderByDescending(group => group.WastedBytes).ThenBy(group => group.Files[0].Path).ToList(),
            skippedEntries);
    }

    private static Dictionary<long, List<FileInfo>> ListFiles(
        IReadOnlyList<string> roots, long minimumBytes, CancellationToken cancellation, ref int skippedEntries)
    {
        var byLength = new Dictionary<long, List<FileInfo>>();
        // Roots may overlap (Downloads inside a chosen folder); a file is counted once.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<DirectoryInfo>(roots.Select(root => new DirectoryInfo(root)).Where(root => root.Exists));

        while (pending.TryPop(out DirectoryInfo? current))
        {
            cancellation.ThrowIfCancellationRequested();
            if (!FileSystemWalk.TryList(current, out IReadOnlyList<FileSystemInfo> entries))
            {
                skippedEntries++;
                continue;
            }

            foreach (FileSystemInfo entry in entries.Where(entry => !FileSystemWalk.IsLink(entry)))
            {
                if (entry is DirectoryInfo directory)
                {
                    if (!BelongsToTools(directory))
                    {
                        pending.Push(directory);
                    }
                }
                else if (entry is FileInfo file && file.Length >= minimumBytes && FileSystemWalk.IsOnThisDisk(file) && seen.Add(file.FullName))
                {
                    if (!byLength.TryGetValue(file.Length, out List<FileInfo>? sameLength))
                    {
                        byLength[file.Length] = sameLength = [];
                    }

                    sameLength.Add(file);
                }
            }
        }

        return byLength;
    }

    // Copies there are the tools' business, not the user's: removing one breaks a project or a
    // program. Dot folders hold tool data, a .git folder marks a code repository, and project
    // build output (node_modules, a venv, a Rust target) is recognised by its markers.
    private static bool BelongsToTools(DirectoryInfo directory) =>
        directory.Name.StartsWith('.')
        || Directory.Exists(Path.Combine(directory.FullName, ".git"))
        || KnownCacheRules.ProjectArtifacts.Any(rule => rule.Matches(directory));

    // Files that cannot be read (in use, no access) drop out and are counted as skipped.
    private static List<List<FileInfo>> GroupByHash(
        List<FileInfo> files, Func<FileInfo, string> hash, CancellationToken cancellation, ref int skippedEntries)
    {
        var byHash = new Dictionary<string, List<FileInfo>>();
        foreach (FileInfo file in files)
        {
            cancellation.ThrowIfCancellationRequested();
            string key;
            try
            {
                key = hash(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                skippedEntries++;
                continue;
            }

            if (!byHash.TryGetValue(key, out List<FileInfo>? same))
            {
                byHash[key] = same = [];
            }

            same.Add(file);
        }

        return byHash.Values.Where(same => same.Count > 1).ToList();
    }

    private static string PartialHash(FileInfo file)
    {
        using FileStream stream = Open(file);
        byte[] buffer = new byte[PartialHashBytes];
        int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, read)));
    }

    private static string FullHash(FileInfo file)
    {
        using FileStream stream = Open(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    // Shared, so a file another program has open is still read rather than skipped.
    private static FileStream Open(FileInfo file) => new(
        file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);
}
