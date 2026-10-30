namespace Vacuo.Core.DiskMap;

/// <summary>
/// A folder or a large file on the disk map. What is too small to be seen on the map has no node of its own:
/// it is part of its folder's <see cref="SmallBytes"/>. The root's name is its full path.
/// </summary>
public sealed class FolderNode
{
    private List<FolderNode>? _children;

    internal FolderNode(string name, FolderNode? parent, bool isFile = false, long bytes = 0)
    {
        Name = name;
        Parent = parent;
        IsFile = isFile;
        Bytes = bytes;
    }

    public string Name { get; }

    public FolderNode? Parent { get; }

    public bool IsFile { get; }

    /// <summary>Everything below this node, its <see cref="SmallBytes"/> included.</summary>
    public long Bytes { get; internal set; }

    public long SmallBytes { get; internal set; }

    /// <summary>Largest first.</summary>
    public IReadOnlyList<FolderNode> Children => _children is null ? [] : _children;

    public string FullPath => Parent is null ? Name : Path.Join(Parent.FullPath, Name);

    internal void Add(FolderNode child) => (_children ??= []).Add(child);

    internal void SortChildren() => _children?.Sort((left, right) => right.Bytes.CompareTo(left.Bytes));
}
