using System.Text.Json;

namespace Vacuo.Core.Leftovers;

/// <summary>What the administrator helper did for one operation (ADR-003).</summary>
public sealed record ElevatedResult(
    string Operation, bool Succeeded, long DeletedBytes, int DeletedFiles, int Skipped, int? ExitCode, string? Error);

/// <summary>
/// Where the helper leaves its results: one file per run, <c>%LocalAppData%\Vacuo\elevated\&lt;id&gt;.json</c>,
/// holding a list of <see cref="ElevatedResult"/>.
/// </summary>
public static class ElevatedResults
{
    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vacuo", "elevated");

    public static string FileFor(string folder, Guid requestId) => Path.Combine(folder, $"{requestId:D}.json");

    public static void Write(string folder, Guid requestId, IReadOnlyList<ElevatedResult> results)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(FileFor(folder, requestId), JsonSerializer.Serialize(results));
    }

    public static IReadOnlyList<ElevatedResult> Read(string folder, Guid requestId) =>
        JsonSerializer.Deserialize<List<ElevatedResult>>(File.ReadAllText(FileFor(folder, requestId)))
        ?? throw new InvalidDataException($"The result file for request {requestId:D} is empty.");
}

/// <summary>Starts the administrator helper; the real one shows the Windows administrator prompt.</summary>
public interface IElevatedLauncher
{
    /// <summary>Runs these operations under one administrator prompt and returns what the helper did.</summary>
    /// <exception cref="ElevationDeclinedException">The user said no at the prompt; nothing ran.</exception>
    IReadOnlyList<ElevatedResult> Run(IReadOnlyList<string> operations);
}

public sealed class ElevationDeclinedException() : Exception("The administrator prompt was declined; nothing ran.");
