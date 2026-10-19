namespace Vacuo.Core.Ram;

/// <summary>A running program, its processes together: private memory summed, protected if any part is.</summary>
public sealed record RunningApp(string Name, long PrivateBytes, bool HasWindow, bool IsProtected);

public enum AppGroup
{
    /// <summary>Heavy and out of use for a while: the only ones the Memory screen offers to close.</summary>
    Idle,
    InUse,
    /// <summary>System, protected or without a window to ask nicely: shown, never closable.</summary>
    Protected,
}

public sealed record AppUse(RunningApp App, AppGroup Group, TimeSpan? UnusedFor);

/// <summary>Splits running apps into idle, in use and protected, largest first.</summary>
public static class AppSorter
{
    public static readonly TimeSpan IdleAfter = TimeSpan.FromHours(1);
    public const long HeavyBytes = 200L << 20;

    public static IReadOnlyList<AppUse> Sort(IEnumerable<RunningApp> apps, ForegroundHistory history, DateTimeOffset now) => apps
        .OrderByDescending(app => app.PrivateBytes)
        .Select(app => Use(app, history, now))
        .ToList();

    private static AppUse Use(RunningApp app, ForegroundHistory history, DateTimeOffset now)
    {
        TimeSpan? unusedFor = now - history.UnusedSince(app.Name);
        if (app.IsProtected || !app.HasWindow)
        {
            return new AppUse(app, AppGroup.Protected, unusedFor);
        }

        bool idle = app.PrivateBytes >= HeavyBytes && unusedFor >= IdleAfter;
        return new AppUse(app, idle ? AppGroup.Idle : AppGroup.InUse, unusedFor);
    }
}
