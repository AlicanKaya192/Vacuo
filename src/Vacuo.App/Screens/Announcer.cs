using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Vacuo.App.Screens;

/// <summary>
/// Says a finished result through the screen reader (Narrator) without moving the focus, so a user who cannot
/// see the screen hears what a move, a removal or an update ended with (vacuo-gelistirme-plani Aşama 3f).
/// </summary>
internal static class Announcer
{
    private const string ActivityId = "Vacuo.Result";

    /// <param name="source">Any element that is on screen; a screen's title is always there.</param>
    public static void Say(FrameworkElement source, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        AutomationPeer? peer = FrameworkElementAutomationPeer.FromElement(source)
            ?? FrameworkElementAutomationPeer.CreatePeerForElement(source);
        peer?.RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted,
            AutomationNotificationProcessing.ImportantMostRecent,
            text,
            ActivityId);
    }

    /// <summary>The lines a result panel shows, read one after another.</summary>
    public static void SayLines(FrameworkElement source, Panel lines) =>
        Say(source, string.Join(". ", lines.Children.OfType<TextBlock>().Select(line => line.Text).Where(text => text.Length > 0)));
}
