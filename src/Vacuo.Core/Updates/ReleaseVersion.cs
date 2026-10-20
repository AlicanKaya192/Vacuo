using System.Globalization;

namespace Vacuo.Core.Updates;

/// <summary>
/// A release number such as 0.4.0. Releases are tagged <c>setup-X.Y.Z</c> (the release workflow's trigger);
/// any other tag is not a Vacuo release. Missing parts count as zero, so 0.4 and 0.4.0 are the same release.
/// </summary>
public sealed class ReleaseVersion : IComparable<ReleaseVersion>
{
    public const string TagPrefix = "setup-";

    private const int MaximumParts = 4;

    private readonly int[] _parts;

    private ReleaseVersion(int[] parts) => _parts = parts;

    public static bool TryParseTag(string tag, out ReleaseVersion? version)
    {
        version = null;
        return tag.StartsWith(TagPrefix, StringComparison.Ordinal) && TryParse(tag[TagPrefix.Length..], out version);
    }

    /// <summary>Digits and dots only: a suffix such as "-beta" is not a release number.</summary>
    public static bool TryParse(string text, out ReleaseVersion? version)
    {
        version = null;
        string[] pieces = text.Split('.');
        if (pieces.Length > MaximumParts)
        {
            return false;
        }

        var parts = new int[pieces.Length];
        for (int i = 0; i < pieces.Length; i++)
        {
            if (!int.TryParse(pieces[i], NumberStyles.None, CultureInfo.InvariantCulture, out parts[i]))
            {
                return false;
            }
        }

        version = new ReleaseVersion(parts);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        for (int i = 0; i < Math.Max(_parts.Length, other._parts.Length); i++)
        {
            int difference = Part(i).CompareTo(other.Part(i));
            if (difference != 0)
            {
                return difference;
            }
        }

        return 0;
    }

    public override string ToString() => string.Join('.', _parts);

    private int Part(int index) => index < _parts.Length ? _parts[index] : 0;
}
