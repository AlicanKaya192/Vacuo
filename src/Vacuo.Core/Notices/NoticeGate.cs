namespace Vacuo.Core.Notices;

/// <summary>
/// Whether a notification may show now (vacuo-kurallar 16): only when Windows says the user accepts
/// notifications. A full-screen program, a game, a presentation, quiet time, a locked screen or a Store app
/// in front keep it silent; the work stays in the Journal and in Settings.
/// </summary>
public static class NoticeGate
{
    // QUNS_ACCEPTS_NOTIFICATIONS, from SHQueryUserNotificationState.
    public const int AcceptsNotifications = 5;

    public static bool MayShow(int userNotificationState) => userNotificationState == AcceptsNotifications;
}
