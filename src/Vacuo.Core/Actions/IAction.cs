namespace Vacuo.Core.Actions;

/// <summary>A change on disk the user asked for. Run it through <see cref="ActionRunner"/>.</summary>
public interface IAction
{
    /// <summary>The file or folder the action moves.</summary>
    string TargetPath { get; }

    UndoRecord Execute();
}
