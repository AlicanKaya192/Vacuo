using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vacuo.Core.Journal;

/// <summary>
/// The action log, one JSON object per line. Lines are only ever appended, an undo too, so a
/// crash while writing can at most cut the last line short and never damages earlier entries.
/// </summary>
public sealed class ActionJournal(string journalPath)
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vacuo", "journal.jsonl");

    public IReadOnlyList<JournalEntry> ReadAll()
    {
        if (!File.Exists(journalPath))
        {
            return [];
        }

        var entries = new List<JournalEntry>();
        int lineNumber = 0;
        foreach (string line in File.ReadLines(journalPath, Utf8WithoutBom))
        {
            lineNumber++;
            switch (ParseLine(line, lineNumber))
            {
                case DoneLine done:
                    entries.Add(done.Entry);
                    break;
                case UndoneLine undone:
                    int index = entries.FindIndex(entry => entry.Id == undone.EntryId);
                    if (index >= 0)
                    {
                        entries[index] = entries[index] with { UndoneAt = undone.At };
                    }
                    break;
            }
        }

        return entries;
    }

    public void Append(JournalEntry entry) => AppendLine(new DoneLine(entry));

    public void MarkUndone(Guid entryId, DateTimeOffset at) => AppendLine(new UndoneLine(entryId, at));

    private void AppendLine(JournalLine line)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(journalPath))!);
        File.AppendAllText(journalPath, JsonSerializer.Serialize(line) + "\n", Utf8WithoutBom);
    }

    private JournalLine ParseLine(string line, int lineNumber)
    {
        try
        {
            return JsonSerializer.Deserialize<JournalLine>(line)
                ?? throw new JsonException("The line holds no journal record.");
        }
        catch (JsonException exception)
        {
            throw new JournalCorruptException(journalPath, lineNumber, exception);
        }
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "line")]
    [JsonDerivedType(typeof(DoneLine), "done")]
    [JsonDerivedType(typeof(UndoneLine), "undone")]
    private abstract record JournalLine;

    private sealed record DoneLine(JournalEntry Entry) : JournalLine;

    private sealed record UndoneLine(Guid EntryId, DateTimeOffset At) : JournalLine;
}
