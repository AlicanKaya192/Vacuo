using System.ComponentModel;
using System.Runtime.InteropServices;
using static Vacuo.App.Tray.NativeMethods;

namespace Vacuo.App.Tray;

/// <summary>
/// The Vacuo logo in the notification area (bottom right of the taskbar).
/// Event-driven: it only reacts to messages from the shell, nothing polls.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint CallbackMessage = WmApp + 1;
    private const uint IconId = 1;
    private const nuint SubclassId = 1;

    private readonly nint _window;
    private readonly nint _icon;
    private readonly string _tooltip;
    private readonly uint _taskbarCreatedMessage;
    // Kept in a field so the delegate is not collected while Windows still calls it.
    private readonly SubclassProc _subclassProc;
    private bool _disposed;

    public TrayIcon(nint window, string iconPath, string tooltip)
    {
        _window = window;
        _tooltip = tooltip;
        _icon = LoadSmallIcon(window, iconPath);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        _subclassProc = OnWindowMessage;

        if (!SetWindowSubclass(window, _subclassProc, SubclassId, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not listen for tray icon messages.");
        }

        AddToNotificationArea();
    }

    public event EventHandler? Clicked;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        var data = CreateData();
        Shell_NotifyIcon(NimDelete, ref data);
        RemoveWindowSubclass(_window, _subclassProc, SubclassId);
        DestroyIcon(_icon);
        _disposed = true;
    }

    private static nint LoadSmallIcon(nint window, string iconPath)
    {
        uint dpi = GetDpiForWindow(window);
        int width = GetSystemMetricsForDpi(SmallIconWidthMetric, dpi);
        int height = GetSystemMetricsForDpi(SmallIconHeightMetric, dpi);
        nint icon = LoadImage(0, iconPath, ImageIcon, width, height, LoadFromFile);
        if (icon == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not load tray icon from '{iconPath}'.");
        }

        return icon;
    }

    private void AddToNotificationArea()
    {
        var data = CreateData();
        data.uFlags = NifMessage | NifIcon | NifTip | NifShowTip;
        if (!Shell_NotifyIcon(NimAdd, ref data))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not add the tray icon.");
        }

        data.uVersion = NotifyIconVersion4;
        Shell_NotifyIcon(NimSetVersion, ref data);
    }

    private NotifyIconData CreateData() => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _window,
        uID = IconId,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private nint OnWindowMessage(nint hWnd, uint message, nint wParam, nint lParam, nuint subclassId, nuint refData)
    {
        if (message == CallbackMessage)
        {
            // With version 4 the low word of lParam carries the event.
            uint trayEvent = (uint)(lParam & 0xFFFF);
            if (trayEvent is NinSelect or NinKeySelect or WmContextMenu)
            {
                Clicked?.Invoke(this, EventArgs.Empty);
            }

            return 0;
        }

        if (message == _taskbarCreatedMessage)
        {
            // Explorer restarted; the icon has to be added again.
            AddToNotificationArea();
        }

        return DefSubclassProc(hWnd, message, wParam, lParam);
    }
}
