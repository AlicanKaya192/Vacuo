namespace Vacuo.Core.Scanning;

/// <summary>Where this machine keeps Windows and its temp folders.</summary>
public sealed record WindowsLocations(string SystemDrive, string Windows, string UserTemp)
{
    public static WindowsLocations ForThisMachine()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return new(Path.GetPathRoot(windows)!, windows, Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar));
    }
}

/// <summary>How Vacuo handles a leftover (ADR-003).</summary>
public enum LeftoverKind
{
    /// <summary>The user's own temp folder: no administrator, items go to the Recycle Bin.</summary>
    UserTemp,
    /// <summary>A Windows folder whose contents only an administrator can remove, for good.</summary>
    AdminFolder,
    /// <summary>Left to Windows' own Disk Cleanup: removing it needs ownership changes Vacuo does not make.</summary>
    WindowsCleanupTool,
    /// <summary>DISM component store cleanup: an operation, not a folder.</summary>
    ComponentCleanup,
}

public sealed record WindowsLeftover(string Id, LeftoverKind Kind, string? Path);

/// <summary>
/// The fixed table of Windows leftovers. Paths come only from here, never from the user or a
/// scan, so the administrator helper can be told an id and nothing else.
/// </summary>
public static class WindowsLeftovers
{
    public static IReadOnlyList<WindowsLeftover> For(WindowsLocations locations) =>
    [
        new("user-temp", LeftoverKind.UserTemp, locations.UserTemp),
        new("windows-temp", LeftoverKind.AdminFolder, Path.Combine(locations.Windows, "Temp")),
        new("update-downloads", LeftoverKind.AdminFolder, Path.Combine(locations.Windows, "SoftwareDistribution", "Download")),
        new("upgrade-files-bt", LeftoverKind.AdminFolder, Path.Combine(locations.SystemDrive, "$Windows.~BT")),
        new("upgrade-files-ws", LeftoverKind.AdminFolder, Path.Combine(locations.SystemDrive, "$Windows.~WS")),
        new("windows-old", LeftoverKind.WindowsCleanupTool, Path.Combine(locations.SystemDrive, "Windows.old")),
        new("component-cleanup", LeftoverKind.ComponentCleanup, Path: null),
    ];
}
