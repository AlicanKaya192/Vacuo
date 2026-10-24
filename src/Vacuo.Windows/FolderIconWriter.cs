using System.Runtime.InteropServices;
using Vacuo.Core.Actions;
using Vacuo.Core.Organize;

namespace Vacuo.Windows;

/// <summary>
/// Gives a folder its icon through the shell, which writes desktop.ini and marks the folder read-only
/// (seen on 24 October 2026). The icon file sits inside the folder under a relative path, so the icon
/// stays when the folder moves or Vacuo is removed (Aşama 3h).
/// </summary>
public sealed class FolderIconWriter : IFolderIconWriter
{
    // ShlObj_core.h, Windows SDK 10.0.26100.0.
    private const uint IconFileMask = 0x10; // FCSM_ICONFILE
    private const uint ForceWrite = 0x2; // FCS_FORCEWRITE

    public void Apply(string folder, string iconFile)
    {
        string icon = Path.Combine(folder, FolderIcon.IconFile);
        File.Copy(iconFile, icon, overwrite: false);
        File.SetAttributes(icon, FileAttributes.Hidden | FileAttributes.System);

        var settings = new FolderCustomSettings
        {
            Size = (uint)Marshal.SizeOf<FolderCustomSettings>(),
            Mask = IconFileMask,
            IconFile = FolderIcon.IconFile,
        };
        int result = SHGetSetFolderCustomSettings(ref settings, folder, ForceWrite);
        if (result < 0)
        {
            throw new IOException($"The shell did not give '{folder}' its icon.", result);
        }
    }

    // SHFOLDERCUSTOMSETTINGS; only the icon fields are filled.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FolderCustomSettings
    {
        public uint Size;
        public uint Mask;
        public nint ViewId;
        public string? WebViewTemplate;
        public uint WebViewTemplateLength;
        public string? WebViewTemplateVersion;
        public string? InfoTip;
        public uint InfoTipLength;
        public nint ClassId;
        public uint Flags;
        public string? IconFile;
        public uint IconFileLength;
        public int IconIndex;
        public string? Logo;
        public uint LogoLength;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetSetFolderCustomSettings(ref FolderCustomSettings settings, string folder, uint readWrite);
}
