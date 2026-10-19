using Vacuo.Core.Scanning;

namespace Vacuo.Core.Actions;

/// <summary>
/// Checks, just before an action, that no other program holds a file open. Opening each file
/// without sharing fails while any other handle is open. A file that cannot be checked
/// refuses the action too: when in doubt, nothing moves.
/// </summary>
public static class OpenFileProbe
{
    public static void EnsureNothingInUse(string path)
    {
        if (Directory.Exists(path))
        {
            EnsureFolderNotInUse(new DirectoryInfo(path));
        }
        else
        {
            EnsureFileNotInUse(path);
        }
    }

    private static void EnsureFolderNotInUse(DirectoryInfo folder)
    {
        var pending = new Stack<DirectoryInfo>([folder]);
        while (pending.TryPop(out DirectoryInfo? current))
        {
            if (!FileSystemWalk.TryList(current, out IReadOnlyList<FileSystemInfo> entries))
            {
                throw new ActionRefusedException($"'{current.FullName}' cannot be read, so it is unclear whether its files are in use.");
            }

            // A link inside moves as the link itself; its target is not touched.
            foreach (FileSystemInfo entry in entries.Where(entry => !FileSystemWalk.IsLink(entry)))
            {
                if (entry is DirectoryInfo directory)
                {
                    pending.Push(directory);
                }
                else
                {
                    EnsureFileNotInUse(entry.FullName);
                }
            }
        }
    }

    private static void EnsureFileNotInUse(string file)
    {
        try
        {
            using var exclusive = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        catch (UnauthorizedAccessException)
        {
            throw new ActionRefusedException($"'{file}' cannot be opened, so it is unclear whether it is in use.");
        }
        catch (IOException)
        {
            throw new FileInUseException(file);
        }
    }
}
