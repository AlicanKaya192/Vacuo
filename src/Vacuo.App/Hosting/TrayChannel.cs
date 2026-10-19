using System.ComponentModel;
using System.Runtime.InteropServices;
using Vacuo.Core.Hosting;
using static Vacuo.App.Tray.NativeMethods;

namespace Vacuo.App.Hosting;

/// <summary>
/// The panel process's side of <see cref="PanelProtocol"/>: listens on the panel window for
/// "show" and "quit" from the tray, and tells the tray when the panel was hidden.
/// </summary>
internal sealed class TrayChannel : IDisposable
{
    private const nuint SubclassId = 2;

    private readonly nint _panelWindow;
    private readonly nint _trayWindow;
    private readonly uint _showMessage = RegisterWindowMessage(PanelProtocol.ShowPanelMessage);
    private readonly uint _quitMessage = RegisterWindowMessage(PanelProtocol.QuitPanelMessage);
    private readonly uint _hiddenMessage = RegisterWindowMessage(PanelProtocol.PanelHiddenMessage);
    // Kept in a field so the delegate is not collected while Windows still calls it.
    private readonly SubclassProc _subclassProc;

    public TrayChannel(nint panelWindow, nint trayWindow)
    {
        _panelWindow = panelWindow;
        _trayWindow = trayWindow;
        _subclassProc = OnWindowMessage;
        if (!SetWindowSubclass(panelWindow, _subclassProc, SubclassId, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not listen for messages from the tray.");
        }
    }

    public event EventHandler? ShowRequested;

    public event EventHandler? QuitRequested;

    public void NotifyPanelHidden()
    {
        if (_trayWindow != 0)
        {
            PostMessage(_trayWindow, _hiddenMessage, 0, 0);
        }
    }

    public void Dispose() => RemoveWindowSubclass(_panelWindow, _subclassProc, SubclassId);

    private nint OnWindowMessage(nint hWnd, uint message, nint wParam, nint lParam, nuint subclassId, nuint refData)
    {
        if (message == _showMessage)
        {
            ShowRequested?.Invoke(this, EventArgs.Empty);
            return 0;
        }

        if (message == _quitMessage)
        {
            QuitRequested?.Invoke(this, EventArgs.Empty);
            return 0;
        }

        return DefSubclassProc(hWnd, message, wParam, lParam);
    }
}
