namespace Vacuo.Core.Organize;

/// <summary>
/// Turns a folder's files into moves into subfolders of that same folder (B-161), so every move stays on
/// one drive. Nothing is overwritten: a taken name becomes "name (2).ext". A file standing where a target
/// folder would go keeps the files meant for that folder where they are.
/// </summary>
public static class OrganizePlanner
{
    public static OrganizePlan Plan(
        string folder,
        IEnumerable<FolderItem> items,
        OrganizeRule rule,
        Func<OrganizeTarget, string> folderName,
        PathProbe probe)
    {
        var moves = new List<PlannedMove>();
        var foldersToCreate = new List<string>();
        var plannedDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (FolderItem item in items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (OrganizeClassifier.TargetOf(item, rule) is not { } target)
            {
                continue;
            }

            string targetFolder = Path.Combine(folder, folderName(target));
            if (probe.FileExists(targetFolder))
            {
                continue;
            }

            if (!probe.FolderExists(targetFolder) && !foldersToCreate.Contains(targetFolder, StringComparer.OrdinalIgnoreCase))
            {
                foldersToCreate.Add(targetFolder);
            }

            string destination = FreeName(targetFolder, item.Name, probe, plannedDestinations);
            plannedDestinations.Add(destination);
            moves.Add(new PlannedMove(Path.Combine(folder, item.Name), destination));
        }

        return new OrganizePlan(folder, moves, foldersToCreate);
    }

    private static string FreeName(string targetFolder, string name, PathProbe probe, HashSet<string> planned)
    {
        string stem = Path.GetFileNameWithoutExtension(name);
        string extension = Path.GetExtension(name);
        string candidate = Path.Combine(targetFolder, name);
        for (int number = 2; IsTaken(candidate, probe, planned); number++)
        {
            candidate = Path.Combine(targetFolder, $"{stem} ({number}){extension}");
        }

        return candidate;
    }

    private static bool IsTaken(string path, PathProbe probe, HashSet<string> planned) =>
        planned.Contains(path) || probe.FileExists(path) || probe.FolderExists(path);
}
