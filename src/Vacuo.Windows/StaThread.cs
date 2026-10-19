using System.Runtime.ExceptionServices;

namespace Vacuo.Windows;

/// <summary>
/// Runs work on a fresh single-threaded apartment thread: IFileOperation works only in an
/// STA, and callers (thread pool, tests) are usually in the multithreaded apartment.
/// </summary>
internal static class StaThread
{
    public static T Run<T>(Func<T> work)
    {
        T result = default!;
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        failure?.Throw();
        return result;
    }
}
