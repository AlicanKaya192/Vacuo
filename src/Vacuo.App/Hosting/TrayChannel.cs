using System.ComponentModel;
using System.Runtime.InteropServices;
using Vacuo.Core.Hosting;
using Vacuo.Core.Ram;
using static Vacuo.App.Tray.NativeMethods;

namespace Vacuo.App.Hosting;

/// <summary>
/// The panel process's side of <see cref="PanelProtocol"/>: listens on the panel window for
/// "show", "show on Disk" and "quit" from the tray, tells the tray when the panel was hidden, asks it for
/// the foreground history, and sets the icon's tooltip while an update runs.
/// </summary>
internal sealed class TrayChannel : IDisposable
{
    private const nuint SubclassId = 2;
    private const uint TooltipTimeoutMs = 1000;

    private readonly nint _panelWindow;
    private readonly nint _trayWindow;
    private readonly uint _showMessage = RegisterWindowMessage(PanelProtocol.ShowPanelMessage);
    private readonly uint _showDiskMessage = RegisterWindowMessage(PanelProtocol.ShowDiskMessage);
    private readonly uint _showOrganizeMessage = RegisterWindowMessage(PanelProtocol.ShowOrganizeMessage);
    private readonly uint _quitMessage = RegisterWindowMessage(PanelProtocol.QuitPanelMessage);
    private readonly uint _hiddenMessage = RegisterWindowMessage(PanelProtocol.PanelHiddenMessage);
    private readonly uint _foregroundRequestMessage = RegisterWindowMessage(PanelProtocol.ForegroundRequestMessage);
    // Kept in a field so the delegate is not collected while Windows still calls it.
    private readonly SubclassProc _subclassProc;
    private TaskCompletionSource<ForegroundSnapshot?>? _foregroundReply;

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

    public event EventHandler? ShowDiskRequested;

    public event EventHandler? ShowOrganizeRequested;

    public event EventHandler? QuitRequested;

    public void NotifyPanelHidden()
    {
        if (_trayWindow != 0)
        {
            PostMessage(_trayWindow, _hiddenMessage, 0, 0);
        }
    }

    /// <summary>
    /// The tray's foreground history, or null when the tray does not answer within <paramref name="timeout"/>
    /// or sends something that is not a history. Call on the panel's UI thread.
    /// </summary>
    public async Task<ForegroundSnapshot?> RequestForegroundAsync(TimeSpan timeout)
    {
        if (_trayWindow == 0)
        {
            return null;
        }

        _foregroundReply ??= new TaskCompletionSource<ForegroundSnapshot?>();
        Task<ForegroundSnapshot?> reply = _foregroundReply.Task;
        if (!PostMessage(_trayWindow, _foregroundRequestMessage, _panelWindow, 0))
        {
            _foregroundReply = null;
            return null;
        }

        Task first = await Task.WhenAny(reply, Task.Delay(timeout));
        if (first != reply)
        {
            _foregroundReply = null;
            return null;
        }

        return await reply;
    }

    /// <summary>Puts <paramref name="text"/> over the tray icon; empty gives back the tray's own tooltip.</summary>
    public void SendTooltip(string text)
    {
        if (_trayWindow == 0)
        {
            return;
        }

        byte[] data = TrayTooltip.Encode(text);
        GCHandle pinned = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var copy = new CopyDataStruct { dwData = PanelProtocol.TooltipDataTag, cbData = data.Length, lpData = pinned.AddrOfPinnedObject() };
            // A busy tray must not freeze the panel.
            _ = SendMessageTimeout(_trayWindow, WmCopyData, _panelWindow, ref copy, SendAbortIfHung, TooltipTimeoutMs, out _);
        }
        finally
        {
            pinned.Free();
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

        if (message == _showDiskMessage)
        {
            ShowDiskRequested?.Invoke(this, EventArgs.Empty);
            return 0;
        }

        if (message == _showOrganizeMessage)
        {
            ShowOrganizeRequested?.Invoke(this, EventArgs.Empty);
            return 0;
        }

        if (message == _quitMessage)
        {
            QuitRequested?.Invoke(this, EventArgs.Empty);
            return 0;
        }

        // Only the tray's own window may hand over a history; other senders are passed on unread.
        if (message == WmCopyData && wParam == _trayWindow && _trayWindow != 0)
        {
            var copy = Marshal.PtrToStructure<CopyDataStruct>(lParam);
            if (copy.dwData == PanelProtocol.ForegroundDataTag)
            {
                ReceiveForeground(copy);
                return 1;
            }
        }

        return DefSubclassProc(hWnd, message, wParam, lParam);
    }

    // The data is only valid during the message, so it is copied before anything else.
    private void ReceiveForeground(CopyDataStruct copy)
    {
        TaskCompletionSource<ForegroundSnapshot?>? reply = _foregroundReply;
        _foregroundReply = null;
        if (reply is null)
        {
            return;
        }

        if (copy.cbData <= 0 || copy.cbData > ForegroundSnapshot.MaxBytes || copy.lpData == 0)
        {
            reply.TrySetResult(null);
            return;
        }

        byte[] bytes = new byte[copy.cbData];
        Marshal.Copy(copy.lpData, bytes, 0, copy.cbData);
        try
        {
            reply.TrySetResult(ForegroundSnapshot.Decode(bytes));
        }
        catch (FormatException)
        {
            reply.TrySetResult(null);
        }
    }
}
