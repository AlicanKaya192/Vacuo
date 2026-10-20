using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vacuo.Core.Updates;

public sealed record ManifestEntry(string Sha256, long Size);

/// <summary>
/// Every file of a published Vacuo folder with its SHA-256 and size (K4, as in Odyssey). Each release carries
/// its own as <c>Vacuo-X.Y.Z-files.json</c>; the next release is compared with the last published one to build
/// a patch setup that writes only what changed. Paths are relative to the folder, with '/'.
/// </summary>
public sealed record FileManifest(string Version, IReadOnlyDictionary<string, ManifestEntry> Files)
{
    public static FileManifest Of(string version, string root)
    {
        var files = new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            using FileStream stream = File.OpenRead(path);
            string digest = Convert.ToHexStringLower(SHA256.HashData(stream));
            files[Path.GetRelativePath(root, path).Replace('\\', '/')] = new ManifestEntry(digest, stream.Length);
        }

        return new FileManifest(version, files);
    }

    /// <exception cref="FormatException">Not a file list this tool wrote.</exception>
    public static FileManifest Parse(string json)
    {
        ManifestFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ManifestFile>(json);
        }
        catch (JsonException exception)
        {
            throw new FormatException("The file list is not readable JSON.", exception);
        }

        if (file is not { Version: { } version, Files: { } files } || !ReleaseVersion.TryParse(version, out _))
        {
            throw new FormatException("The file list has no release number or no files.");
        }

        return new FileManifest(version, files);
    }

    // In path order, so two lists of the same folder read the same.
    public string ToJson() => JsonSerializer.Serialize(
        new ManifestFile(Version, Files.OrderBy(file => file.Key, StringComparer.Ordinal).ToDictionary(StringComparer.Ordinal)));

    private sealed record ManifestFile(string? Version, Dictionary<string, ManifestEntry>? Files);
}

/// <summary>
/// What a patch setup does to an installed release: write the files that are new or changed, and remove the
/// ones the new release no longer has. Only paths from the two file lists, all inside Vacuo's own folder.
/// </summary>
public sealed record PatchPlan(IReadOnlyList<string> Write, IReadOnlyList<string> Remove)
{
    private const string LineEnd = "\r\n";

    public static PatchPlan Between(FileManifest old, FileManifest current) => new(
        current.Files
            .Where(file => !old.Files.TryGetValue(file.Key, out ManifestEntry? before) || before != file.Value)
            .Select(file => file.Key)
            .Order(StringComparer.Ordinal)
            .ToArray(),
        old.Files.Keys.Where(path => !current.Files.ContainsKey(path)).Order(StringComparer.Ordinal).ToArray());

    /// <summary>The [InstallDelete] and [Files] sections installer/vacuo.iss includes for a patch.</summary>
    public string ToInnoSections()
    {
        var text = new StringBuilder("[InstallDelete]" + LineEnd);
        foreach (string path in Remove)
        {
            text.Append($"Type: files; Name: \"{{app}}\\{Inno(path)}\"{LineEnd}");
        }

        text.Append(LineEnd + "[Files]" + LineEnd);
        foreach (string path in Write)
        {
            int slash = path.LastIndexOf('/');
            string folder = slash < 0 ? "{app}" : "{app}\\" + Inno(path[..slash]);
            text.Append($"Source: \"{{#SourceDir}}\\{Inno(path)}\"; DestDir: \"{folder}\"; Flags: ignoreversion{LineEnd}");
        }

        return text.ToString();
    }

    // Backslashes for Windows; a brace would open an Inno Setup constant, so it is doubled.
    private static string Inno(string path) => path.Replace('/', '\\').Replace("{", "{{", StringComparison.Ordinal);
}
