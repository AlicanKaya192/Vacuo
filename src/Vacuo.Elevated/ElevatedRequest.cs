using Vacuo.Core.Scanning;

namespace Vacuo.Elevated;

/// <summary>Arguments the helper will not act on; nothing was touched.</summary>
internal sealed class RequestRefusedException(string message) : Exception(message);

/// <summary>
/// What the helper was asked to do: one or more operations from <see cref="WindowsLeftovers"/>,
/// done under one administrator prompt, and an id for the result file. Never a path: the helper
/// finds each folder in the fixed table itself.
/// </summary>
internal sealed record ElevatedRequest(IReadOnlyList<WindowsLeftover> Leftovers, Guid RequestId)
{
    public static ElevatedRequest Parse(IReadOnlyList<string> args, WindowsLocations locations)
    {
        if (args.Count < 2)
        {
            throw new RequestRefusedException($"Expected operations and a request id, got {args.Count} arguments.");
        }

        if (!Guid.TryParseExact(args[^1], "D", out Guid requestId))
        {
            throw new RequestRefusedException("The last argument is not a GUID request id.");
        }

        IReadOnlyList<WindowsLeftover> table = WindowsLeftovers.For(locations);
        var leftovers = new List<WindowsLeftover>();
        foreach (string operation in args.Take(args.Count - 1))
        {
            WindowsLeftover leftover = table.SingleOrDefault(item => item.Id == operation)
                ?? throw new RequestRefusedException($"'{operation}' is not an operation the helper knows.");
            if (leftover.Kind is not (LeftoverKind.AdminFolder or LeftoverKind.ComponentCleanup))
            {
                throw new RequestRefusedException($"'{leftover.Id}' is not done by the administrator helper.");
            }

            if (leftovers.Contains(leftover))
            {
                throw new RequestRefusedException($"'{leftover.Id}' was asked for twice.");
            }

            leftovers.Add(leftover);
        }

        return new ElevatedRequest(leftovers, requestId);
    }
}
