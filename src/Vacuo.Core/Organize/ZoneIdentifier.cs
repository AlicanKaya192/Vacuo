namespace Vacuo.Core.Organize;

/// <summary>
/// Reads the "came from the internet" note Windows keeps beside a download (the Zone.Identifier stream).
/// Vacuo only reads it, never changes it (development plan, Aşama 3).
/// </summary>
public static class ZoneIdentifier
{
    public const string StreamSuffix = ":Zone.Identifier";

    /// <summary>The website a download came from, without "www."; <see langword="null"/> when none is recorded.</summary>
    public static string? SourceHost(string zoneText)
    {
        Dictionary<string, string> values = zoneText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .GroupBy(parts => parts[0].Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First()[1].Trim(), StringComparer.OrdinalIgnoreCase);

        return HostOf(values.GetValueOrDefault("HostUrl")) ?? HostOf(values.GetValueOrDefault("ReferrerUrl"));
    }

    private static string? HostOf(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Host.Length == 0)
        {
            return null;
        }

        return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
    }
}
