using Vacuo.Core.Leftovers;
using Vacuo.Core.Scanning;

namespace Vacuo.Elevated;

/// <summary>Starts a program and waits for its exit code; DISM runs behind this so tests can stand in for it.</summary>
public interface IProcessRunner
{
    int Run(string fileName, IReadOnlyList<string> arguments);
}

/// <summary>The helper's one run: check the request, do the cleanups under this one prompt, write the results, end.</summary>
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

        // One failure does not stop the others; each is reported on its own.
        List<ElevatedResult> results = request.Leftovers.Select(leftover => RunOne(leftover, locations, runner)).ToList();

        ElevatedResults.Write(resultFolder, request.RequestId, results);
        return results.All(result => result.Succeeded) ? Succeeded : Failed;
    }

    private static ElevatedResult RunOne(WindowsLeftover leftover, WindowsLocations locations, IProcessRunner runner)
    {
        try
        {
            return leftover.Kind == LeftoverKind.ComponentCleanup
                ? CleanComponents(leftover.Id, locations, runner)
                : EmptyFolder(leftover);
        }
        catch (Exception exception)
        {
            return new ElevatedResult(leftover.Id, false, 0, 0, 0, ExitCode: null, Error: exception.Message);
        }
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
