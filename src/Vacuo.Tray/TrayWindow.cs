using System.ComponentModel;
using System.Runtime.InteropServices;
using Vacuo.Core.Hosting;
using static Vacuo.Tray.NativeMethods;

namespace Vacuo.Tray;

/// <summary>
/// A hidden window that owns the Vacuo logo in the notification area and receives its clicks.
/// Other threads talk to it only by posting messages, so all work runs on the tray thread.
/// </summary>
internal sealed class TrayWindow : IDisposable
{
    private const string ClassName = "VacuoTrayWindow";
    private const uint IconMessage = WmApp + 1;
    private const uint OpenPanelMessage = WmApp + 2;
    private const uint ClosePanelMessage = WmApp + 3;
    private const uint PanelExitedMessage = WmApp + 4;
    private const uint QuitMessage = WmApp + 5;
    private const uint IconId = 1;
    private const nuint TimerId = 1;
    // Nothing the panel sends is longer; a tooltip is at most 254 bytes.
    private const int MaxCopyDataBytes = 4096;

    // Kept in a field so the delegate is not collected while Windows still calls it.
    private readonly WindowProc _windowProc;
    private readonly uint _taskbarCreatedMessage;
    private readonly uint _panelHiddenMessage = RegisterWindowMessage(PanelProtocol.PanelHiddenMessage);
    private readonly uint _foregroundRequestMessage = RegisterWindowMessage(PanelProtocol.ForegroundRequestMessage);
    private readonly nint _icon;
    private readonly string _defaultTooltip;
    private string _tooltip;
    private bool _iconAdded;

    public TrayWindow(string iconPath, string tooltip)
    {
        _defaultTooltip = tooltip;
        _tooltip = tooltip;
        _windowProc = OnMessage;
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        Handle = CreateHiddenWindow(_windowProc);
        _icon = LoadImage(0, iconPath, ImageIcon, GetSystemMetrics(SmallIconWidthMetric), GetSystemMetrics(SmallIconHeightMetric), LoadFromFile);
        if (_icon == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not load tray icon from '{iconPath}'.");
        }

        AddIcon();
    }

    public event Action? IconClicked;

    public event Action? OpenPanelRequested;

    public event Action? ClosePanelRequested;

    public event Action? PanelExited;

    public event Action? PanelHidden;

    public event Action? QuitRequested;

    /// <summary>A window asked for the foreground history; the argument is the window to answer.</summary>
    public event Action<nint>? ForegroundRequested;

    /// <summary>WM_COPYDATA arrived: the sending window, the data's tag and a copy of the data.</summary>
    public event Action<nint, int, byte[]>? DataReceived;

    /// <summary>The repeating timer from <see cref="StartTimer"/> fired.</summary>
    public event Action? TimerTicked;

    /// <summary>A repeating timer on the tray thread; a message every <paramref name="interval"/>, no thread of its own.</summary>
    public void StartTimer(TimeSpan interval)
    {
        if (SetTimer(Handle, TimerId, (uint)interval.TotalMilliseconds, 0) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start the tray's timer.");
        }
    }

    public nint Handle { get; }

    public void PostOpenPanel() => PostMessage(Handle, OpenPanelMessage, 0, 0);

    /// <summary>Asks the tray already running for this user to open its panel; false when none answers.</summary>
    public static bool OpenRunningPanel()
    {
        nint running = FindWindow(ClassName, null);
        return running != 0 && PostMessage(running, OpenPanelMessage, 0, 0);
    }

    public void PostClosePanel() => PostMessage(Handle, ClosePanelMessage, 0, 0);

    public void PostPanelExited() => PostMessage(Handle, PanelExitedMessage, 0, 0);

    public void PostQuit() => PostMessage(Handle, QuitMessage, 0, 0);

    /// <summary>Removes the icon and ends <see cref="RunMessageLoop"/>.</summary>
    public void Quit()
    {
        RemoveIcon();
        PostQuitMessage(0);
    }

    public static void RunMessageLoop()
    {
        while (GetMessage(out Msg message, 0, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessage(ref message);
        }
    }

    public void Dispose()
    {
        RemoveIcon();
        DestroyIcon(_icon);
        DestroyWindow(Handle);
    }

    private static nint CreateHiddenWindow(WindowProc windowProc)
    {
        nint instance = GetModuleHandle(null);
        var windowClass = new WindowClassEx
        {
            cbSize = Marshal.SizeOf<WindowClassEx>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(windowProc),
            hInstance = instance,
            lpszClassName = ClassName,
        };
        if (RegisterClassEx(ref windowClass) == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register the tray window class.");
        }

        // A plain top-level window that is never shown, so it still gets the TaskbarCreated broadcast.
        nint window = CreateWindowEx(0, ClassName, string.Empty, 0, 0, 0, 0, 0, 0, 0, instance, 0);
        if (window == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the tray window.");
        }

        return window;
    }

    private void AddIcon()
    {
        var data = CreateIconData();
        data.uFlags = NifMessage | NifIcon | NifTip;
        if (!Shell_NotifyIcon(NimAdd, ref data))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not add the tray icon.");
        }

        _iconAdded = true;
    }

    /// <summary>A Windows notification from the tray icon; failing to show it changes nothing else.</summary>
    public void ShowWarning(string title, string text)
    {
        var data = CreateIconData();
        data.uFlags = NifInfo;
        data.szInfoTitle = title;
        data.szInfo = text;
        data.dwInfoFlags = NiifWarning;
        Shell_NotifyIcon(NimModify, ref data);
    }

    /// <summary>The icon's tooltip; empty puts back the tray's own. Kept when Explorer restarts.</summary>
    public void SetTooltip(string text)
    {
        _tooltip = text.Length == 0 ? _defaultTooltip : text;
        if (!_iconAdded)
        {
            return;
        }

        var data = CreateIconData();
        data.uFlags = NifTip;
        Shell_NotifyIcon(NimModify, ref data);
    }

    private void RemoveIcon()
    {
        if (!_iconAdded)
        {
            return;
        }

        var data = CreateIconData();
        Shell_NotifyIcon(NimDelete, ref data);
        _iconAdded = false;
    }

    private NotifyIconData CreateIconData() => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = Handle,
        uID = IconId,
        uCallbackMessage = IconMessage,
        hIcon = _icon,
        szTip = _tooltip,
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private nint OnMessage(nint hWnd, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case IconMessage:
                uint mouse = (uint)lParam;
                if (mouse is WmLButtonUp or WmRButtonUp)
                {
                    IconClicked?.Invoke();
                }

                return 0;
            case OpenPanelMessage:
                OpenPanelRequested?.Invoke();
                return 0;
            case ClosePanelMessage:
                ClosePanelRequested?.Invoke();
                return 0;
            case PanelExitedMessage:
                PanelExited?.Invoke();
                return 0;
            case QuitMessage:
                QuitRequested?.Invoke();
                return 0;
            case WmTimer when (nuint)wParam == TimerId:
                TimerTicked?.Invoke();
                return 0;
        }

        if (message == _panelHiddenMessage)
        {
            PanelHidden?.Invoke();
            return 0;
        }

        if (message == _foregroundRequestMessage)
        {
            ForegroundRequested?.Invoke(wParam);
            return 0;
        }

        if (message == WmCopyData)
        {
            ReceiveData(wParam, lParam);
            return 1;
        }

        if (message == _taskbarCreatedMessage)
        {
            // Explorer restarted; the icon has to be added again.
            _iconAdded = false;
            AddIcon();
        }

        return DefWindowProc(hWnd, message, wParam, lParam);
    }

    // The data is only valid during the message, so it is copied first; who sent it is checked by the listener.
    private void ReceiveData(nint sender, nint lParam)
    {
        var copy = Marshal.PtrToStructure<CopyDataStruct>(lParam);
        if (copy.cbData < 0 || copy.cbData > MaxCopyDataBytes || (copy.cbData > 0 && copy.lpData == 0))
        {
            return;
        }

        byte[] data = new byte[copy.cbData];
        if (copy.cbData > 0)
        {
            Marshal.Copy(copy.lpData, data, 0, copy.cbData);
        }

        DataReceived?.Invoke(sender, (int)copy.dwData, data);
    }
}
