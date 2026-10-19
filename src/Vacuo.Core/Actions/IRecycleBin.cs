namespace Vacuo.Core.Actions;

/// <summary>
/// The Windows Recycle Bin. Vacuo only moves items in and restores them; it never
/// empties the bin or deletes anything for good (project rule 3).
/// </summary>
public interface IRecycleBin
{
    RecycledItem MoveToBin(string path);

    void Restore(RecycledItem item);
}
