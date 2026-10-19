namespace Vacuo.Core.Actions;

/// <summary>Moves a file or folder to a new place on the same drive.</summary>
public sealed class MoveFileAction(string source, string destination) : IAction
{
    public string TargetPath => source;

    public UndoRecord Execute()
    {
        SameVolumeMove.Move(source, destination);
        return new MovedItem(source, destination);
    }
}
