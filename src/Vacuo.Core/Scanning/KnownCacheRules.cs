namespace Vacuo.Core.Scanning;

/// <summary>
/// The one table of what Vacuo treats as rebuildable. Rules stay narrow: a folder only matches
/// with its marker files, and anything uncertain is left for the "Yours" bucket or not shown.
/// </summary>
public static class KnownCacheRules
{
    public static readonly IReadOnlyList<KnownLocationRule> KnownLocations =
    [
        Download("pip-cache", KnownRoot.LocalAppData, @"pip\Cache"),
        Download("npm-cache", KnownRoot.LocalAppData, "npm-cache"),
        Download("torch-hub", KnownRoot.UserProfile, @".cache\torch\hub"),
        Download("conda-pkgs-anaconda", KnownRoot.UserProfile, @"anaconda3\pkgs"),
        // The tools' documented defaults; not yet seen on the development machine.
        Download("uv-cache", KnownRoot.LocalAppData, @"uv\cache"),
        Download("pnpm-store", KnownRoot.LocalAppData, @"pnpm\store"),
        Download("conda-pkgs-miniconda", KnownRoot.UserProfile, @"miniconda3\pkgs"),
        Download("huggingface-hub", KnownRoot.UserProfile, @".cache\huggingface\hub"),
    ];

    public static readonly IReadOnlyList<ProjectArtifactRule> ProjectArtifacts =
    [
        Artifact("node-modules", RebuildCost.ReinstalledByCommand,
            directory => directory.Name == "node_modules" && ArtifactMarkers.HasSiblingFile(directory, "package.json")),
        Artifact("python-venv", RebuildCost.ReinstalledByCommand,
            directory => ArtifactMarkers.HasFile(directory, "pyvenv.cfg")),
        Artifact("python-bytecode", RebuildCost.RegeneratedAutomatically,
            directory => directory.Name == "__pycache__" && ArtifactMarkers.HoldsOnlyFilesWithExtension(directory, ".pyc")),
        Artifact("jupyter-checkpoints", RebuildCost.RegeneratedAutomatically,
            directory => directory.Name == ".ipynb_checkpoints" && ArtifactMarkers.HasSiblingFileWithExtension(directory, ".ipynb")),
        Artifact("rust-target", RebuildCost.ReinstalledByCommand,
            directory => directory.Name == "target"
                && ArtifactMarkers.HasSiblingFile(directory, "Cargo.toml")
                && ArtifactMarkers.HasFile(directory, "CACHEDIR.TAG")),
    ];

    /// <summary>
    /// Folders of installed software. Their <c>node_modules</c> and the like are part of a
    /// program, not a project, so removing them would break it.
    /// </summary>
    public static readonly IReadOnlySet<string> InstalledSoftwareFolders =
        new HashSet<string>(["AppData", "anaconda3", "miniconda3", "site-packages"], StringComparer.OrdinalIgnoreCase);

    public static IEnumerable<CacheRule> All => KnownLocations.Concat<CacheRule>(ProjectArtifacts);

    private static KnownLocationRule Download(string id, KnownRoot root, string relativePath) =>
        new(id, Bucket.RebuildsItself, RebuildCost.Redownloaded, root, relativePath);

    private static ProjectArtifactRule Artifact(string id, RebuildCost cost, Func<DirectoryInfo, bool> matches) =>
        new(id, Bucket.RebuildsItself, cost, matches);
}
