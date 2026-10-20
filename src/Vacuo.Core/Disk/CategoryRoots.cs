namespace Vacuo.Core.Disk;

/// <summary>
/// The one table of which known folders count toward which category. Only folders on the system
/// drive count, since the categories split that drive's used space; a deeper folder wins over the
/// folder holding it (pip's cache is Developer, though it sits in AppData).
/// </summary>
public static class CategoryRoots
{
    public static IReadOnlyList<CategoryRoot> ForThisMachine()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

        CategoryRoot[] roots =
        [
            Apps(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)),
            Apps(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)),
            Apps(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)),
            Apps(local),
            Apps(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),

            Documents(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
            Documents(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
            Documents(Path.Combine(profile, "Downloads")),
            Documents(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)),
            Documents(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
            Documents(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),

            Developer(Path.Combine(profile, ".nuget")),
            Developer(Path.Combine(profile, ".cargo")),
            Developer(Path.Combine(profile, ".rustup")),
            Developer(Path.Combine(profile, ".gradle")),
            Developer(Path.Combine(profile, ".m2")),
            Developer(Path.Combine(profile, "anaconda3")),
            Developer(Path.Combine(profile, "miniconda3")),
            Developer(Path.Combine(local, "pip")),
            Developer(Path.Combine(local, "npm-cache")),
            Developer(Path.Combine(local, "uv")),
            Developer(Path.Combine(local, "pnpm")),

            AiAgents(Path.Combine(profile, @".cache\huggingface")),
            AiAgents(Path.Combine(profile, @".cache\torch")),
            AiAgents(Path.Combine(profile, ".ollama")),
            AiAgents(Path.Combine(profile, ".lmstudio")),
            AiAgents(Path.Combine(profile, ".claude")),
            AiAgents(Path.Combine(profile, ".cursor")),
            AiAgents(Path.Combine(profile, ".codex")),
        ];

        // A folder redirected to another drive (or not set) splits nothing here.
        return roots
            .Where(root => root.Path.Length > 0
                && string.Equals(Path.GetPathRoot(root.Path), systemDrive, StringComparison.OrdinalIgnoreCase))
            .DistinctBy(root => root.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static CategoryRoot Apps(string path) => new(DiskCategory.AppsAndData, path);

    private static CategoryRoot Documents(string path) => new(DiskCategory.DocumentsAndMedia, path);

    private static CategoryRoot Developer(string path) => new(DiskCategory.Developer, path);

    private static CategoryRoot AiAgents(string path) => new(DiskCategory.AiAgents, path);
}
