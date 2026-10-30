using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Vacuo.Core.Organize;

namespace Vacuo.Windows;

/// <summary>Reads a .lnk through the shell, as written; the target is not looked for or changed.</summary>
public sealed class ShellShortcutReader : IShortcutReader
{
    private const int LongestPath = 32_768;
    private const uint RawPath = 0x4;
    private const int ReadOnly = 0;

    private static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");

    public string? TargetOf(string shortcut)
    {
        object? link = null;
        try
        {
            link = Activator.CreateInstance(Type.GetTypeFromCLSID(ShellLinkClass, throwOnError: true)!)!;
            ((IPersistFile)link).Load(shortcut, ReadOnly);
            var target = new StringBuilder(LongestPath);
            ((IShellLinkW)link).GetPath(target, target.Capacity, 0, RawPath);
            return target.Length > 0 ? target.ToString() : null;
        }
        catch (Exception exception) when (exception is COMException or IOException or UnauthorizedAccessException or InvalidCastException)
        {
            // A broken shortcut is not known to be a game, so it stays where it is.
            return null;
        }
        finally
        {
            if (link is not null)
            {
                Marshal.ReleaseComObject(link);
            }
        }
    }
}
