namespace Vacuo.Core.Scanning;

/// <summary>Total length of the files in a folder tree; <paramref name="SkippedEntries"/> counts unreadable folders.</summary>
public sealed record DirectorySize(long Bytes, int SkippedEntries);

/// <summary>
/// Adds up file lengths (Explorer's "Size", not "Size on disk"). Links are neither followed
/// nor counted. Long paths need no special prefix: .NET adds it itself.
/// </summary>
public static class DirectorySizer
{
    public static DirectorySize Measure(DirectoryInfo directory)
    {
        long bytes = 0;
        int skippedEntries = 0;
        var pending = new Stack<DirectoryInfo>([directory]);

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
                    bytes += file.Length;
                }
                else
                {
                    pending.Push((DirectoryInfo)entry);
                }
            }
        }

        return new DirectorySize(bytes, skippedEntries);
    }
}
