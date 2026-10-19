using System.Text.Json;
using Vacuo.Core.Scanning;

namespace Vacuo.Elevated;

/// <summary>Starts a program and waits for its exit code; DISM runs behind this so tests can stand in for it.</summary>
public interface IProcessRunner
{
    int Run(string fileName, IReadOnlyList<string> arguments);
}

/// <summary>What the helper did, written to <c>%LocalAppData%\Vacuo\elevated\&lt;id&gt;.json</c> for Vacuo to read.</summary>
public sealed record ElevatedResult(
    string Operation, bool Succeeded, long DeletedBytes, int DeletedFiles, int Skipped, int? ExitCode, string? Error);

/// <summary>The helper's one run: check the request, do that single cleanup, write the result, end.</summary>
internal static class ElevatedApp
{
    public const int Succeeded = 0;
    public const int Failed = 1;
    public const int Refused = 2;

    private static readonly string[] DismArguments = ["/Online", "/Cleanup-Image", "/StartComponentCleanup"];

    public static int Run(IReadOnlyList<string> args, WindowsLocations locations, IProcessRunner runner, string resultFolder)
    {
        ElevatedRequest request;
        try
        {
            request = ElevatedRequest.Parse(args, locations);
        }
        catch (RequestRefusedException)
        {
            return Refused;
        }

        ElevatedResult result;
        try
        {
            result = request.Leftover.Kind == LeftoverKind.ComponentCleanup
                ? CleanComponents(request.Leftover.Id, locations, runner)
                : EmptyFolder(request.Leftover);
        }
        catch (Exception exception)
        {
            result = new ElevatedResult(request.Leftover.Id, false, 0, 0, 0, ExitCode: null, Error: exception.Message);
        }

        Directory.CreateDirectory(resultFolder);
        File.WriteAllText(Path.Combine(resultFolder, $"{request.RequestId:D}.json"), JsonSerializer.Serialize(result));
        return result.Succeeded ? Succeeded : Failed;
    }

    private static ElevatedResult EmptyFolder(WindowsLeftover leftover)
    {
        EmptyResult emptied = FolderEmptier.Empty(leftover.Path!);
        return new ElevatedResult(leftover.Id, true, emptied.DeletedBytes, emptied.DeletedFiles, emptied.Skipped, ExitCode: null, Error: null);
    }

    // Without /ResetBase, so installed updates can still be removed afterwards.
    private static ElevatedResult CleanComponents(string id, WindowsLocations locations, IProcessRunner runner)
    {
        int exitCode = runner.Run(Path.Combine(locations.Windows, "System32", "Dism.exe"), DismArguments);
        return new ElevatedResult(id, exitCode == 0, 0, 0, 0, exitCode, Error: null);
    }
}
