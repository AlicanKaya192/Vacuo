using Vacuo.Core.Ram;

namespace Vacuo.Core.Notices;

/// <summary>
/// When the tray tells the user that memory is tight (ADR-004): once when it becomes tight, and at most once
/// every <see cref="MinimumGap"/>, so memory that comes and goes around the line does not nag. It only tells;
/// nothing is closed (vacuo-kurallar 4). The tray reads memory every <see cref="CheckInterval"/>.
/// </summary>
public static class MemoryNoticePolicy
{
    public static TimeSpan CheckInterval { get; } = TimeSpan.FromMinutes(1);

    public static TimeSpan MinimumGap { get; } = TimeSpan.FromHours(1);

    /// <summary>A last notice in the future means the clock was set back; it does not hold the next one.</summary>
    public static bool ShouldNotify(MemoryPressure pressure, bool wasTight, DateTimeOffset? lastNotice, DateTimeOffset now) =>
        pressure == MemoryPressure.Tight
        && !wasTight
        && (lastNotice is not { } last || last > now || now - last >= MinimumGap);
}
