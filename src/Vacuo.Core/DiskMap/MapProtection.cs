namespace Vacuo.Core.DiskMap;

/// <summary>Why the disk map will not move something; <see cref="None"/> means it can.</summary>
public enum MapLock
{
    None,

    /// <summary>Windows and Vacuo's own files, with everything inside them.</summary>
    System,

    /// <summary>Installed programs, with everything inside them; they are removed on the uninstall screen.</summary>
    Programs,

    /// <summary>The folder itself stays (the Desktop, the user's profile); what is inside it can move.</summary>
    KeepFolder,
}

public sealed record MapProtectedPaths(string DriveRoot, IReadOnlyList<string> System, IReadOnlyList<string> Programs, IReadOnlyList<string> KeepFolders)
{
    public static MapProtectedPaths ForThisMachine()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string root = Path.GetPathRoot(windows) ?? @"C:\";
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        string[] system =
        [
            windows,
            AppContext.BaseDirectory,
            Path.Combine(local, "Vacuo"),
            Path.Combine(local, "Microsoft"),
            Path.Combine(roaming, "Microsoft"),
            .. new[] { "Default", "Default User", "All Users", "Public" }.Select(name => Path.Combine(Path.GetDirectoryName(profile) ?? root, name)),
            .. new[]
            {
                "$Recycle.Bin", "$WinREAgent", "System Volume Information", "Recovery", "Boot", "EFI", "PerfLogs", "Config.Msi",
                "Documents and Settings", "pagefile.sys", "hiberfil.sys", "swapfile.sys", "DumpStack.log", "DumpStack.log.tmp", "bootmgr", "BOOTNXT",
            }.Select(name => Path.Combine(root, name)),
        ];

        string[] programs =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetEnvironmentVariable("ProgramW6432") ?? string.Empty,
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Path.Combine(local, "Programs"),
            Path.Combine(local, "Packages"),
        ];

        string[] keepFolders =
        [
            profile,
            local,
            roaming,
            Path.Combine(profile, "AppData", "LocalLow"),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(profile, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetEnvironmentVariable("OneDrive") ?? string.Empty,
        ];

        return new MapProtectedPaths(root, Known(system), Known(programs), Known(keepFolders));
    }

    // A folder that is not set on this machine reads as an empty path.
    private static string[] Known(string[] paths) => paths.Where(path => path.Length > 0).ToArray();
}

/// <summary>
/// What the disk map refuses to move (Aşama 3k). The map can reach the whole drive, unlike the screens that only
/// offer what a rule recognises, so this is the one thing between a click and a broken Windows. A folder holding
/// something protected is kept too: moving it would take that along.
/// </summary>
public sealed class MapProtection
{
    private readonly string[] _system;
    private readonly string[] _programs;
    private readonly string[] _keepFolders;

    public MapProtection(MapProtectedPaths paths)
    {
        _system = paths.System.Select(Normalize).ToArray();
        _programs = paths.Programs.Select(Normalize).ToArray();
        _keepFolders = [Normalize(paths.DriveRoot), .. paths.KeepFolders.Select(Normalize)];
    }

    public MapLock LockOf(string path)
    {
        string target = Normalize(path);
        if (_system.Any(folder => IsSameOrInside(target, folder)))
        {
            return MapLock.System;
        }

        if (_programs.Any(folder => IsSameOrInside(target, folder)))
        {
            return MapLock.Programs;
        }

        bool holdsProtected = _system.Concat(_programs).Concat(_keepFolders).Any(folder => IsSameOrInside(folder, target));
        return holdsProtected ? MapLock.KeepFolder : MapLock.None;
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool IsSameOrInside(string path, string folder)
    {
        if (path.Equals(folder, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // A drive root keeps its separator; "C:\Windows2" must not count as inside "C:\Windows".
        string prefix = folder.EndsWith(Path.DirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
