using Vacuo.Core.Hosting;
using Vacuo.Core.Tests.Actions;
using Vacuo.Core.Tests.Scanning;

namespace Vacuo.Core.Tests.Hosting;

public class CrashLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 15, 30, 12, TimeSpan.FromHours(3));

    private static Exception Thrown()
    {
        try
        {
            throw new InvalidOperationException("The disk scan stopped halfway.", new IOException("Access denied."));
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }

    [Fact]
    public void Write_PutsProgramVersionTimeAndTheWholeErrorInAFile()
    {
        using var tree = new TempTree();
        var log = new CrashLog(tree.PathOf("crash-logs"), new FixedTimeProvider(Now));

        string? path = log.Write("Vacuo", "1.3.0", Thrown());

        Assert.NotNull(path);
        Assert.StartsWith(tree.PathOf("crash-logs"), path);
        string text = File.ReadAllText(path);
        Assert.Contains("Vacuo 1.3.0", text);
        Assert.Contains("2026-11-05T12:30:12Z", text);
        Assert.Contains("System.InvalidOperationException: The disk scan stopped halfway.", text);
        Assert.Contains("System.IO.IOException: Access denied.", text);
        Assert.Contains(nameof(Thrown), text);
    }

    [Fact]
    public void Write_TwoCrashesAtTheSameMoment_KeepsBoth()
    {
        using var tree = new TempTree();
        var log = new CrashLog(tree.PathOf("crash-logs"), new FixedTimeProvider(Now));

        string? first = log.Write("Vacuo", "1.3.0", Thrown());
        string? second = log.Write("Vacuo.Tray", "1.3.0", Thrown());

        Assert.NotEqual(first, second);
        Assert.Equal(2, Directory.GetFiles(tree.PathOf("crash-logs")).Length);
    }

    [Fact]
    public void Write_FolderCannotBeMade_ReturnsNullWithoutThrowing()
    {
        using var tree = new TempTree();
        string blocked = tree.AddFile("crash-logs", 1);
        var log = new CrashLog(blocked, new FixedTimeProvider(Now));

        string? path = log.Write("Vacuo", "1.3.0", Thrown());

        Assert.Null(path);
    }
}
