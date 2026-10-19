namespace Vacuo.Core.Actions;

/// <summary>Moves a file or folder to the Recycle Bin, where the user can still get it back.</summary>
public sealed class MoveToRecycleBinAction(string path, IRecycleBin recycleBin) : IAction
{
    public string TargetPath => path;

    public UndoRecord Execute()
    {
        if (!Path.Exists(path))
        {
            throw new ActionRefusedException($"'{path}' no longer exists.");
        }

        return recycleBin.MoveToBin(path);
    }
}
