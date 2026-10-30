using System.Text.Json;

namespace Vacuo.Core.Organize;

public enum ScheduleEvery
{
    Weekly,
    Monthly,
}

/// <summary>A folder the user wants to be reminded of (Aşama 3m). A reminder only asks; nothing moves without the confirm card.</summary>
public sealed record OrganizeSchedule(string Folder, OrganizeRule Rule, ScheduleEvery Every, DateTimeOffset LastAsked);

/// <summary>When a reminder is due. The tray only reads the dates; counting files is the helper's work (ADR-004).</summary>
public static class OrganizeSchedulePolicy
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(15);

    /// <summary>A notice nobody is there to see is lost for the whole period, so one is shown only to someone at the computer.</summary>
    public static readonly TimeSpan AtTheComputerWithin = TimeSpan.FromMinutes(1);

    /// <summary>A last asked in the future means the clock was set back; waiting for it could take years.</summary>
    public static bool IsDue(OrganizeSchedule schedule, DateTimeOffset now) =>
        schedule.LastAsked > now || now >= NextAfter(schedule);

    /// <summary>The longest waiting first, so one that keeps having nothing to say cannot hold the others back.</summary>
    public static IReadOnlyList<OrganizeSchedule> Due(IEnumerable<OrganizeSchedule> schedules, DateTimeOffset now) => schedules
        .Where(schedule => IsDue(schedule, now))
        .OrderBy(schedule => schedule.LastAsked)
        .ToList();

    public static int Pending(IEnumerable<FolderItem> items, OrganizeRule rule) =>
        items.Count(item => OrganizeClassifier.TargetOf(item, rule) is not null);

    private static DateTimeOffset NextAfter(OrganizeSchedule schedule) => schedule.Every == ScheduleEvery.Weekly
        ? schedule.LastAsked.AddDays(7)
        : schedule.LastAsked.AddMonths(1);
}

/// <summary>
/// The reminders, in <c>%LOCALAPPDATA%\Vacuo\organize-schedules.json</c>. Written by the panel and the helper, read by
/// the tray. Apart from settings.json, which the Settings screen rewrites whole from what it loaded. A missing or
/// damaged file means no reminders; a row that cannot be understood is dropped and the others stay.
/// </summary>
public sealed class OrganizeScheduleStore(string path)
{
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vacuo", "organize-schedules.json");

    public IReadOnlyList<OrganizeSchedule> Load() => Read().Schedules;

    /// <summary>The reminder the last notice was about, for the panel its click opens; null when it has since been removed.</summary>
    public OrganizeSchedule? LatestAsked()
    {
        State state = Read();
        return state.LatestAsked is { } folder ? state.Schedules.FirstOrDefault(schedule => IsSame(schedule.Folder, folder)) : null;
    }

    /// <summary>A new reminder starts counting now; changing one keeps its date, so changing the rule does not put it off.</summary>
    public void Set(string folder, OrganizeRule rule, ScheduleEvery every, DateTimeOffset now)
    {
        State state = Read();
        OrganizeSchedule? existing = state.Schedules.FirstOrDefault(schedule => IsSame(schedule.Folder, folder));
        OrganizeSchedule changed = existing is null
            ? new OrganizeSchedule(Path.TrimEndingDirectorySeparator(folder), rule, every, now)
            : existing with { Rule = rule, Every = every };
        Write(state with { Schedules = [.. state.Schedules.Where(schedule => schedule != existing), changed] });
    }

    public void Remove(string folder)
    {
        State state = Read();
        Write(state with { Schedules = [.. state.Schedules.Where(schedule => !IsSame(schedule.Folder, folder))] });
    }

    /// <summary>Looked and there was nothing to say: the next look is a period away.</summary>
    public void MarkChecked(string folder, DateTimeOffset now) => Write(WithDate(Read(), folder, now));

    /// <summary>The notice is going out, about this folder.</summary>
    public void MarkAsked(string folder, DateTimeOffset now) => Write(WithDate(Read(), folder, now) with { LatestAsked = folder });

    private static State WithDate(State state, string folder, DateTimeOffset now) => state with
    {
        Schedules = [.. state.Schedules.Select(schedule => IsSame(schedule.Folder, folder) ? schedule with { LastAsked = now } : schedule)],
    };

    private static bool IsSame(string a, string b) => string.Equals(
        Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), StringComparison.OrdinalIgnoreCase);

    private State Read()
    {
        try
        {
            StateFile? file = File.Exists(path) ? JsonSerializer.Deserialize<StateFile>(File.ReadAllText(path)) : null;
            return new State(file?.Schedules?.Select(Understood).OfType<OrganizeSchedule>().ToList() ?? [], file?.LatestAsked);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new State([], null);
        }
    }

    private void Write(State state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        List<ScheduleRow?> rows = state.Schedules
            .Select(schedule => (ScheduleRow?)new ScheduleRow(schedule.Folder, schedule.Rule.ToString(), schedule.Every.ToString(), schedule.LastAsked))
            .ToList();
        File.WriteAllText(temporary, JsonSerializer.Serialize(new StateFile(rows, state.LatestAsked)));
        File.Move(temporary, path, overwrite: true);
    }

    private static OrganizeSchedule? Understood(ScheduleRow? row) =>
        row is { Folder: { Length: > 0 } folder, LastAsked: { } lastAsked }
        && Enum.TryParse(row.Rule, out OrganizeRule rule) && Enum.IsDefined(rule)
        && Enum.TryParse(row.Every, out ScheduleEvery every) && Enum.IsDefined(every)
            ? new OrganizeSchedule(folder, rule, every, lastAsked)
            : null;

    private sealed record State(IReadOnlyList<OrganizeSchedule> Schedules, string? LatestAsked);

    private sealed record StateFile(List<ScheduleRow?>? Schedules, string? LatestAsked);

    private sealed record ScheduleRow(string? Folder, string? Rule, string? Every, DateTimeOffset? LastAsked);
}
