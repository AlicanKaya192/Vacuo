using Vacuo.Core.Actions;

namespace Vacuo.Core.Organize;

/// <summary>
/// Puts the icon file into <paramref name="folder"/> as <see cref="FolderIcon.IconFile"/> (hidden and
/// system) and points the folder at it. Throws <see cref="IOException"/> when the shell refuses.
/// </summary>
public interface IFolderIconWriter
{
    void Apply(string folder, string iconFile);
}

/// <summary>
/// Gives the folders Düzen makes a picture of what is inside them (Aşama 3h): one icon per file kind
/// and topic, read from <paramref name="iconFolder"/> as "&lt;name&gt;.ico". Month and website folders get none.
/// </summary>
public sealed class FolderIcons(IFolderIconWriter writer, string iconFolder)
{
    public static string? NameOf(OrganizeTarget target) =>
        target.Rule is OrganizeRule.ByType or OrganizeRule.ByTopic ? target.Key.ToLowerInvariant() : null;

    /// <summary>Gives <paramref name="folder"/> its icon and fingerprints what that left inside; a failure leaves the folder plain.</summary>
    public FolderIcon? Stamp(string folder, OrganizeTarget target)
    {
        string? iconFile = NameOf(target) is { } name ? Path.Combine(iconFolder, name + ".ico") : null;
        if (iconFile is null || !File.Exists(iconFile) || FolderIcon.Of(folder) is not null)
        {
            // An icon file already there is not Vacuo's, so it is neither overwritten nor taken on undo.
            return null;
        }

        try
        {
            writer.Apply(folder, iconFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The folder stays sorted, only plain; whatever the writer left is still fingerprinted for undo.
        }

        return FolderIcon.Of(folder);
    }
}
