using Vacuo.Core.Actions;

namespace Vacuo.Core.Tests.Actions;

/// <summary>
/// Stands in for the Windows Recycle Bin, which tests must not touch: "recycled" items
/// are moved into a folder of the test's temp tree and moved back on restore.
/// </summary>
internal sealed class FakeRecycleBin(string binFolder, TimeProvider time) : IRecycleBin
{
    private readonly HashSet<string> _stored = [];

    public int ItemCount => _stored.Count;

    public RecycledItem MoveToBin(string path)
    {
        Directory.CreateDirectory(binFolder);
        string binPath = Path.Combine(binFolder, Guid.NewGuid().ToString("N"));
        MoveEntry(path, binPath);
        _stored.Add(binPath);
        return new RecycledItem(path, time.GetUtcNow(), binPath);
    }

    public void Restore(RecycledItem item)
    {
        // Same as the Shell bin: an item restored or emptied elsewhere is simply not there.
        if (!Path.Exists(item.BinPath))
        {
            throw new ItemNotInBinException($"'{item.OriginalPath}' is no longer in the Recycle Bin.");
        }

        MoveEntry(item.BinPath, item.OriginalPath);
        _stored.Remove(item.BinPath);
    }

    private static void MoveEntry(string from, string to)
    {
        if (Directory.Exists(from))
        {
            Directory.Move(from, to);
        }
        else
        {
            File.Move(from, to);
        }
    }
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
