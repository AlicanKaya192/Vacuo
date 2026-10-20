using System.Globalization;
using System.Text.Json;

namespace Vacuo.Core.Ram;

/// <summary>
/// <see cref="ForegroundHistory"/> as it crosses from the tray process to the panel (B-137): only
/// program names and times, held in memory on both sides and never written to disk. Anything that
/// is not exactly this shape is refused, since another program could send the panel bytes too.
/// </summary>
public sealed record ForegroundSnapshot(DateTimeOffset StartedAt, string? InFront, IReadOnlyDictionary<string, DateTimeOffset> LeftFront)
{
    public const int MaxBytes = 64 * 1024;
    private const int MaxNameLength = 260;
    private const string StartedAtField = "startedAt";
    private const string InFrontField = "inFront";
    private const string LeftFrontField = "leftFront";

    public byte[] Encode()
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString(StartedAtField, StartedAt);
            writer.WriteString(InFrontField, InFront);
            writer.WriteStartObject(LeftFrontField);
            foreach ((string name, DateTimeOffset left) in LeftFront)
            {
                writer.WriteString(name, left);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    public static ForegroundSnapshot Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxBytes)
        {
            throw new FormatException($"A foreground snapshot is at most {MaxBytes} bytes.");
        }

        try
        {
            var reader = new Utf8JsonReader(bytes);
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("A foreground snapshot is a JSON object.");
            }

            var leftFront = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty app in root.GetProperty(LeftFrontField).EnumerateObject())
            {
                leftFront[CheckedName(app.Name)] = TimeOf(app.Value);
            }

            JsonElement inFront = root.GetProperty(InFrontField);
            return new ForegroundSnapshot(
                TimeOf(root.GetProperty(StartedAtField)),
                inFront.ValueKind == JsonValueKind.Null ? null : CheckedName(inFront.GetString()),
                leftFront);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new FormatException("Not a foreground snapshot.", exception);
        }
    }

    private static DateTimeOffset TimeOf(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out DateTimeOffset time)
            ? time
            : throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"'{value}' is not a time."));

    private static string CheckedName(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength
            ? throw new FormatException("A program name is empty or too long.")
            : name;
}
