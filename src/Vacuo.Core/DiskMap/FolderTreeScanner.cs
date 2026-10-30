using System.Diagnostics.CodeAnalysis;
using System.IO.Enumeration;
using Vacuo.Core.Scanning;

namespace Vacuo.Core.DiskMap;

/// <summary>What a whole-drive scan found. Hard links count once per name, so the total can exceed the used space.</summary>
public sealed record FolderTreeScan(FolderNode Root, int SkippedFolders, long UsedBytes)
{
    /// <summary>The used space no readable folder accounts for: Windows' own files, other users, the page file.</summary>
    public long UnreadBytes => Math.Max(0, UsedBytes - Root.Bytes);
}

/// <summary>
/// Reads a whole drive for the disk map (Aşama 3k). Only reads. Links are neither followed nor counted, and
/// online-only files take no space, as in <see cref="DirectorySizer"/>. A finished folder too small to be seen on
/// the map folds into its parent straight away, so memory follows what the map can show, not the number of files.
/// </summary>
public sealed class FolderTreeScanner(long smallestKeptBytes = FolderTreeScanner.DefaultSmallestKeptBytes)
{
    public const long DefaultSmallestKeptBytes = 16L << 20;

    private const double ProgressStep = 0.01;

    private static readonly EnumerationOptions OneLevelEverything = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
    };

    public FolderTreeScan Scan(string root, long usedBytes, IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        string rootPath = Path.GetFullPath(root);
        var tally = new Tally(usedBytes, progress);
        var open = new Stack<OpenFolder>();
        var rootNode = new FolderNode(rootPath, parent: null);
        open.Push(Read(rootNode, rootPath, tally));

        while (open.TryPeek(out OpenFolder? folder))
        {
            cancellation.ThrowIfCancellationRequested();
            if (folder.TryTakeSubfolder(out string? name))
            {
                open.Push(Read(new FolderNode(name, folder.Node), Path.Join(folder.Path, name), tally));
            }
            else
            {
                open.Pop();
                Close(folder.Node);
            }
        }

        tally.Finish();
        return new FolderTreeScan(rootNode, tally.SkippedFolders, usedBytes);
    }

    private OpenFolder Read(FolderNode node, string path, Tally tally)
    {
        var subfolders = new List<string>();
        try
        {
            var entries = new FileSystemEnumerable<Entry>(path, ToEntry, OneLevelEverything);
            foreach (Entry entry in entries)
            {
                if (FileSystemWalk.IsLink(entry.Attributes))
                {
                    continue;
                }

                if (entry.IsDirectory)
                {
                    subfolders.Add(entry.Name);
                }
                else if (FileSystemWalk.IsOnThisDisk(entry.Attributes))
                {
                    AddFile(node, entry, tally);
                }
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            tally.SkippedFolders++;
        }

        return new OpenFolder(node, path, subfolders);
    }

    // Small files need no name: they only add to a total, and a drive has millions of them.
    private Entry ToEntry(ref FileSystemEntry entry) =>
        new(entry.IsDirectory || entry.Length >= smallestKeptBytes ? entry.FileName.ToString() : string.Empty,
            entry.Attributes,
            entry.IsDirectory ? 0 : entry.Length,
            entry.IsDirectory);

    private void AddFile(FolderNode folder, Entry file, Tally tally)
    {
        folder.Bytes += file.Length;
        tally.Found(file.Length);
        if (file.Length >= smallestKeptBytes)
        {
            folder.Add(new FolderNode(file.Name, folder, isFile: true, file.Length));
        }
        else
        {
            folder.SmallBytes += file.Length;
        }
    }

    private void Close(FolderNode folder)
    {
        folder.SortChildren();
        if (folder.Parent is not { } parent)
        {
            return;
        }

        parent.Bytes += folder.Bytes;
        if (folder.Bytes >= smallestKeptBytes)
        {
            parent.Add(folder);
        }
        else
        {
            parent.SmallBytes += folder.Bytes;
        }
    }

    private readonly record struct Entry(string Name, FileAttributes Attributes, long Length, bool IsDirectory);

    private sealed class OpenFolder(FolderNode node, string path, List<string> subfolders)
    {
        private int _next;

        public FolderNode Node => node;

        public string Path => path;

        public bool TryTakeSubfolder([NotNullWhen(true)] out string? name)
        {
            name = _next < subfolders.Count ? subfolders[_next++] : null;
            return name is not null;
        }
    }

    private sealed class Tally(long usedBytes, IProgress<double>? progress)
    {
        private long _found;
        private double _reported;

        public int SkippedFolders { get; set; }

        public void Found(long bytes)
        {
            _found += bytes;
            if (usedBytes > 0 && (double)_found / usedBytes >= _reported + ProgressStep)
            {
                // Never quite done until the walk is: hard links can pass the used space early.
                _reported = Math.Min((double)_found / usedBytes, 1 - ProgressStep);
                progress?.Report(_reported);
            }
        }

        public void Finish() => progress?.Report(1);
    }
}
