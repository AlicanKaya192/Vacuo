using Microsoft.Win32;
using Vacuo.Core.Uninstall;

namespace Vacuo.Windows;

/// <summary>
/// Reads the <c>Uninstall</c> keys Windows Settings lists programs from: the machine's 64-bit and
/// 32-bit views and the current user's. Opens every key read-only; never writes or deletes.
/// </summary>
public static class UninstallRegistry
{
    private const string UninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public static IReadOnlyList<UninstallEntry> Read()
    {
        var entries = new List<UninstallEntry>();
        ReadFrom(RegistryHive.LocalMachine, RegistryView.Registry64, entries);
        ReadFrom(RegistryHive.LocalMachine, RegistryView.Registry32, entries);
        ReadFrom(RegistryHive.CurrentUser, RegistryView.Registry64, entries);
        return entries;
    }

    private static void ReadFrom(RegistryHive hive, RegistryView view, List<UninstallEntry> entries)
    {
        using RegistryKey root = RegistryKey.OpenBaseKey(hive, view);
        using RegistryKey? uninstall = root.OpenSubKey(UninstallPath, writable: false);
        if (uninstall is null)
        {
            return;
        }

        foreach (string name in uninstall.GetSubKeyNames())
        {
            using RegistryKey? program = uninstall.OpenSubKey(name, writable: false);
            if (program is not null)
            {
                entries.Add(new UninstallEntry(
                    Text(program, "DisplayName"),
                    Text(program, "Publisher"),
                    Text(program, "DisplayVersion"),
                    program.GetValue("EstimatedSize") as int?,
                    Text(program, "InstallDate"),
                    Text(program, "InstallLocation"),
                    Text(program, "UninstallString"),
                    program.GetValue("SystemComponent") is int systemComponent && systemComponent == 1,
                    Text(program, "ParentKeyName"),
                    Text(program, "ReleaseType")));
            }
        }
    }

    private static string? Text(RegistryKey key, string name) => key.GetValue(name) as string;
}
