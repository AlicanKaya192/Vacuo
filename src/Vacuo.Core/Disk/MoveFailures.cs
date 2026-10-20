using Vacuo.Core.Actions;

namespace Vacuo.Core.Disk;

/// <summary>Turns a refused move to the Recycle Bin into the reason the screens show.</summary>
public static class MoveFailures
{
    // Same code as UnauthorizedAccessException and the Shell's E_ACCESSDENIED.
    private const int AccessDenied = unchecked((int)0x8007_0005);

    public static MoveFailureReason ReasonFor(ActionRefusedException refusal) => refusal switch
    {
        FileInUseException => MoveFailureReason.InUse,
        NotRecyclableException => MoveFailureReason.NotRecyclable,
        { InnerException.HResult: AccessDenied } => MoveFailureReason.NoAccess,
        _ => MoveFailureReason.Refused,
    };
}
