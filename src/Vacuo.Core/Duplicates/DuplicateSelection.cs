namespace Vacuo.Core.Duplicates;

/// <summary>
/// Which copies the user chose to move to the Recycle Bin. Nothing is picked beforehand, and the
/// last copy of a group can never be picked: one always stays.
/// </summary>
public sealed class DuplicateSelection
{
    private readonly Dictionary<string, DuplicateGroup> _groupOf = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _picked = [];

    public DuplicateSelection(IReadOnlyList<DuplicateGroup> groups)
    {
        foreach (DuplicateGroup group in groups)
        {
            foreach (DuplicateFile file in group.Files)
            {
                _groupOf[file.Path] = group;
            }
        }
    }

    public IReadOnlyList<string> Picked => _picked;

    public long PickedBytes => _picked.Sum(path => _groupOf[path].FileBytes);

    /// <summary>False when the path is in no group, or picking it would leave its group without a copy.</summary>
    public bool TryPick(string path)
    {
        if (!CanPick(path))
        {
            return false;
        }

        if (!IsPicked(path))
        {
            _picked.Add(path);
        }

        return true;
    }

    /// <summary>True when the copy is picked already, or picking it still leaves one in its group.</summary>
    public bool CanPick(string path)
    {
        if (!_groupOf.TryGetValue(path, out DuplicateGroup? group))
        {
            return false;
        }

        return IsPicked(path) || group.Files.Count(file => IsPicked(file.Path)) + 1 < group.Files.Count;
    }

    public bool IsPicked(string path) => _picked.Contains(path, StringComparer.OrdinalIgnoreCase);

    public void Unpick(string path) => _picked.RemoveAll(picked => picked.Equals(path, StringComparison.OrdinalIgnoreCase));
}
