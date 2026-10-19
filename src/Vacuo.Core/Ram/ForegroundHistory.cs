namespace Vacuo.Core.Ram;

/// <summary>
/// When each app was last in front, kept in memory only and never written anywhere (B-135).
/// Vacuo knows nothing from before it started watching, so an app never seen in front counts
/// as unused since then.
/// </summary>
public sealed class ForegroundHistory(DateTimeOffset startedAt)
{
    private readonly Dictionary<string, DateTimeOffset> _leftFront = new(StringComparer.OrdinalIgnoreCase);
    private string? _inFront;

    public void Switched(string appName, DateTimeOffset at)
    {
        if (_inFront is not null)
        {
            _leftFront[_inFront] = at;
        }

        _inFront = appName;
    }

    /// <summary>Since when the app has not been in front; null while it is in front now.</summary>
    public DateTimeOffset? UnusedSince(string appName)
    {
        if (string.Equals(_inFront, appName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return _leftFront.TryGetValue(appName, out DateTimeOffset left) ? left : startedAt;
    }
}
