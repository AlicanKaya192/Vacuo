namespace Vacuo.Core.Notices;

/// <summary>
/// When the tray tells the user that the system drive is almost full (Aşama 3g): the free space is low below a
/// tenth of the drive or below <see cref="LowestFreeBytes"/>, whichever comes first as the drive fills (Alican,
/// 22 October 2026). Told once when it becomes low, and at most once every <see cref="MinimumGap"/>. Reading the
/// free space is one cheap call, made every <see cref="CheckInterval"/>; nothing is scanned.
/// </summary>
public static class DiskSpaceNoticePolicy
{
    public const long LowestFreeBytes = 10L << 30;

    private const int LowShareDivisor = 10;

    public static TimeSpan CheckInterval { get; } = TimeSpan.FromMinutes(15);

    public static TimeSpan MinimumGap { get; } = TimeSpan.FromDays(1);

    public static bool IsLow(long freeBytes, long totalBytes) =>
        freeBytes < Math.Max(totalBytes / LowShareDivisor, LowestFreeBytes);

    /// <summary>A last notice in the future means the clock was set back; it does not hold the next one.</summary>
    public static bool ShouldNotify(bool isLow, bool wasLow, DateTimeOffset? lastNotice, DateTimeOffset now) =>
        isLow
        && !wasLow
        && (lastNotice is not { } last || last > now || now - last >= MinimumGap);
}
