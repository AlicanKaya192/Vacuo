namespace Vacuo.Core.Actions;

/// <summary>
/// Moves a file or folder within one drive. That is a single rename, so it either happens
/// completely or not at all; a move across drives would be copy plus delete and could stop
/// halfway, so it is refused. Nothing is ever overwritten.
/// </summary>
internal static class SameVolumeMove
{
    public static void Move(string source, string destination)
    {
        if (!Path.Exists(source))
        {
            throw new ActionRefusedException($"'{source}' no longer exists.");
        }

        if (Path.Exists(destination))
        {
            throw new ActionRefusedException($"'{destination}' already exists, so nothing was moved.");
        }

        if (!string.Equals(Path.GetPathRoot(Path.GetFullPath(source)), Path.GetPathRoot(Path.GetFullPath(destination)), StringComparison.OrdinalIgnoreCase))
        {
            throw new ActionRefusedException($"'{source}' and '{destination}' are on different drives; Vacuo only moves within one drive.");
        }

        if (Directory.Exists(source))
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination);
        }
    }
}
