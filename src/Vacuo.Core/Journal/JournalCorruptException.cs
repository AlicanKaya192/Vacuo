namespace Vacuo.Core.Journal;

public sealed class JournalCorruptException(string journalPath, int lineNumber, Exception innerException)
    : Exception($"Line {lineNumber} of the journal '{journalPath}' cannot be read.", innerException)
{
    public int LineNumber { get; } = lineNumber;
}
