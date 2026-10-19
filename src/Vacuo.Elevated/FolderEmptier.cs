namespace Vacuo.Elevated;

public sealed record EmptyResult(long DeletedBytes, int DeletedFiles, int Skipped);

/// <summary>
/// Deletes what is inside a folder, for good, but keeps the folder. Links are never entered or
/// removed, and files that are in use or cannot be deleted are skipped and counted.
/// </summary>
internal static class FolderEmptier
{
    public static EmptyResult Empty(string folder)
    {
        var root = new DirectoryInfo(folder);
        if (!root.Exists)
        {
            return new EmptyResult(0, 0, 0);
        }

        if (IsLink(root))
        {
            return new EmptyResult(0, 0, Skipped: 1);
        }

        var tally = new Tally();
        EmptyContents(root, tally);
        return new EmptyResult(tally.Bytes, tally.Files, tally.Skipped);
    }

    private static void EmptyContents(DirectoryInfo folder, Tally tally)
    {
        FileSystemInfo[] entries;
        try
        {
            entries = folder.GetFileSystemInfos();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            tally.Skipped++;
            return;
        }

        foreach (FileSystemInfo entry in entries)
        {
            if (IsLink(entry))
            {
                tally.Skipped++;
            }
            else if (entry is DirectoryInfo directory)
            {
                EmptyContents(directory, tally);
                TryRemoveEmptyFolder(directory);
            }
            else if (entry is FileInfo file)
            {
                TryDelete(file, tally);
            }
        }
    }

    private static void TryDelete(FileInfo file, Tally tally)
    {
        try
        {
            long size = file.Length;
            file.Delete();
            tally.Bytes += size;
            tally.Files++;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            tally.Skipped++;
        }
    }

    // Non-recursive on purpose: a folder that still holds a skipped file simply stays.
    private static void TryRemoveEmptyFolder(DirectoryInfo directory)
    {
        try
        {
            directory.Delete(recursive: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Not empty or not ours to remove; what was in it is already counted.
        }
    }

    private static bool IsLink(FileSystemInfo entry) => entry.Attributes.HasFlag(FileAttributes.ReparsePoint);

    private sealed class Tally
    {
        public long Bytes;
        public int Files;
        public int Skipped;
    }
}
