using Vacuo.Core.Actions;

namespace Vacuo.Core.Tests.Actions;

/// <summary>
/// Stands in for the Windows Recycle Bin, which tests must not touch: "recycled" items
/// are moved into a folder of the test's temp tree and moved back on restore.
/// </summary>
internal sealed class FakeRecycleBin(string binFolder, TimeProvider time) : IRecycleBin
{
    private readonly Dictionary<RecycledItem, string> _storedAt = [];

    public int ItemCount => _storedAt.Count;

    public RecycledItem MoveToBin(string path)
    {
        Directory.CreateDirectory(binFolder);
        string storedAt = Path.Combine(binFolder, Guid.NewGuid().ToString("N"));
        MoveEntry(path, storedAt);
        var item = new RecycledItem(path, time.GetUtcNow());
        _storedAt.Add(item, storedAt);
        return item;
    }

    public void Restore(RecycledItem item)
    {
        MoveEntry(_storedAt[item], item.OriginalPath);
        _storedAt.Remove(item);
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
