using System.Text.RegularExpressions;
using Vacuo.Core.Scanning;

namespace Vacuo.Core.Uninstall;

public sealed record LeftoverFolder(string Path, DirectorySize Size);

/// <summary>
/// After a program's own uninstaller has run, the data folders it may have left in AppData:
/// only a folder named exactly after the program (spaces, case, punctuation and a trailing
/// version aside), directly in a data folder or in its publisher's folder. A name another
/// installed program still uses is never suggested. Only suggests; moving is the user's choice.
/// </summary>
public static partial class LeftoverFinder
{
    // Windows' own folders directly under AppData; a program sharing one of these names never gets them.
    private static readonly HashSet<string> WindowsDataFolders = new(
        ["temp", "programs", "packages", "microsoft", "windows", "comms", "connecteddevicesplatform", "crashdumps", "d3dscache", "publishers", "virtualstore"],
        StringComparer.Ordinal);

    /// <summary>Where programs keep the current user's data: AppData's Roaming, Local and LocalLow.</summary>
    public static IReadOnlyList<string> DataRootsForThisUser() =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow"),
    ];

    public static IReadOnlyList<LeftoverFolder> Find(
        InstalledProgram removed, IReadOnlyList<string> dataRoots, IReadOnlyList<InstalledProgram> stillInstalled)
    {
        string target = Key(removed.Name);
        if (target.Length == 0 || WindowsDataFolders.Contains(target) || stillInstalled.Any(program => Key(program.Name) == target))
        {
            return [];
        }

        string publisher = removed.Publisher is { } name ? Key(name) : string.Empty;
        var leftovers = new List<LeftoverFolder>();
        foreach (DirectoryInfo root in dataRoots.Select(root => new DirectoryInfo(root)).Where(root => root.Exists))
        {
            foreach (DirectoryInfo folder in Folders(root))
            {
                string key = Key(folder.Name);
                if (key == target)
                {
                    leftovers.Add(Measure(folder));
                }
                else if (publisher.Length > 0 && key == publisher)
                {
                    leftovers.AddRange(Folders(folder).Where(product => Key(product.Name) == target).Select(Measure));
                }
            }
        }

        return leftovers;
    }

    private static IEnumerable<DirectoryInfo> Folders(DirectoryInfo parent) =>
        FileSystemWalk.TryList(parent, out IReadOnlyList<FileSystemInfo> entries)
            ? entries.OfType<DirectoryInfo>().Where(entry => !FileSystemWalk.IsLink(entry))
            : [];

    private static LeftoverFolder Measure(DirectoryInfo folder) => new(folder.FullName, DirectorySizer.Measure(folder));

    // "Foo Player 6.2.1 (x64)" and "foo-player" both become "fooplayer".
    private static string Key(string name)
    {
        string withoutVersion = TrailingVersion().Replace(TrailingBracket().Replace(name.Trim(), string.Empty), string.Empty);
        return new string(withoutVersion.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    [GeneratedRegex(@"\s*\([^)]*\)\s*$")]
    private static partial Regex TrailingBracket();

    [GeneratedRegex(@"\s+v?\d+(\.\d+)*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingVersion();
}
