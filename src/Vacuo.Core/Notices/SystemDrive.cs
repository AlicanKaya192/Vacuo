namespace Vacuo.Core.Notices;

/// <summary>The drive Windows runs from, for the low-disk notice: read by the tray to decide and by the helper to tell.</summary>
public static class SystemDrive
{
    /// <summary>Null when the drive cannot be read now; the check simply waits for the next time.</summary>
    public static DriveInfo? Read()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\");
            return drive.IsReady && drive.TotalSize > 0 ? drive : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>"C:" rather than "C:\", as the notice says it.</summary>
    public static string NameOf(DriveInfo drive) => drive.Name.TrimEnd(Path.DirectorySeparatorChar);
}
