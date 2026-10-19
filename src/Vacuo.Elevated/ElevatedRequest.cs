using Vacuo.Core.Scanning;

namespace Vacuo.Elevated;

/// <summary>Arguments the helper will not act on; nothing was touched.</summary>
internal sealed class RequestRefusedException(string message) : Exception(message);

/// <summary>
/// What the helper was asked to do: an operation from <see cref="WindowsLeftovers"/> and an id
/// for its result file. Never a path: the helper finds the folder in the fixed table itself.
/// </summary>
internal sealed record ElevatedRequest(WindowsLeftover Leftover, Guid RequestId)
{
    public static ElevatedRequest Parse(IReadOnlyList<string> args, WindowsLocations locations)
    {
        if (args.Count != 2)
        {
            throw new RequestRefusedException($"Expected an operation and a request id, got {args.Count} arguments.");
        }

        WindowsLeftover leftover = WindowsLeftovers.For(locations).SingleOrDefault(item => item.Id == args[0])
            ?? throw new RequestRefusedException($"'{args[0]}' is not an operation the helper knows.");
        if (leftover.Kind is not (LeftoverKind.AdminFolder or LeftoverKind.ComponentCleanup))
        {
            throw new RequestRefusedException($"'{leftover.Id}' is not done by the administrator helper.");
        }

        if (!Guid.TryParseExact(args[1], "D", out Guid requestId))
        {
            throw new RequestRefusedException("The request id is not a GUID.");
        }

        return new ElevatedRequest(leftover, requestId);
    }
}
