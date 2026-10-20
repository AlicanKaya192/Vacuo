namespace Vacuo.Core.Uninstall;

public enum ProgramSort
{
    Name,
    Size,
    InstalledOn,
}

/// <summary>The Uninstall screen's list without its looks: searching, sorting, and whether a program is gone.</summary>
public static class ProgramList
{
    /// <summary>
    /// Programs whose name or publisher holds <paramref name="query"/> (any case), in the chosen order.
    /// Largest and newest come first; programs that report no size or date come last.
    /// </summary>
    public static IReadOnlyList<InstalledProgram> Show(IReadOnlyList<InstalledProgram> programs, string query, ProgramSort sort)
    {
        string wanted = query.Trim();
        IEnumerable<InstalledProgram> matching = programs.Where(program =>
            wanted.Length == 0
            || program.Name.Contains(wanted, StringComparison.InvariantCultureIgnoreCase)
            || (program.Publisher?.Contains(wanted, StringComparison.InvariantCultureIgnoreCase) ?? false));

        IOrderedEnumerable<InstalledProgram> ordered = sort switch
        {
            ProgramSort.Size => matching.OrderBy(program => program.SizeBytes is null).ThenByDescending(program => program.SizeBytes),
            ProgramSort.InstalledOn => matching.OrderBy(program => program.InstalledOn is null).ThenByDescending(program => program.InstalledOn),
            _ => matching.OrderBy(program => 0),
        };
        return ordered.ThenBy(program => program.Name, StringComparer.InvariantCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Whether the program is still listed after its uninstaller closed. Some uninstallers hand over to
    /// a copy of themselves and close at once, so a closed uninstaller does not mean a removed program.
    /// </summary>
    public static bool IsStillInstalled(InstalledProgram removed, IReadOnlyList<InstalledProgram> installed) =>
        installed.Any(program => string.Equals(program.Name, removed.Name, StringComparison.OrdinalIgnoreCase));
}
