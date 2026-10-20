using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Vacuo.Core.Settings;

namespace Vacuo.Windows;

/// <summary>
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>: the current user's own key, so no
/// administrator rights. Only <see cref="StartupRegistration"/> writes here, and only Vacuo's value.
/// </summary>
public sealed class StartupRunKey : IRunValues
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Get(string name)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(name) as string;
    }

    public void Set(string name, string value)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void Delete(string name)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    /// <summary>The program behind a window: here the tray, whose window handle the panel is given.</summary>
    public static string? ProgramOfWindow(nint window)
    {
        if (window == 0 || GetWindowThreadProcessId(window, out uint processId) == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.MainModule?.FileName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
