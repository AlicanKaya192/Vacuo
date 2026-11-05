using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Vacuo.Core.Hosting;

/// <summary>
/// When a Vacuo program stops on an error nobody caught, what happened is written to a file of its own
/// under <see cref="DefaultFolder"/>, so a crash leaves a trace. The file stays on this computer: nothing is
/// sent anywhere, and no log is ever removed by Vacuo.
/// </summary>
public sealed class CrashLog(string folder, TimeProvider time)
{
    public static string DefaultFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vacuo", "crash-logs");

    public static CrashLog ForThisComputer() => new(DefaultFolder, TimeProvider.System);

    /// <summary>The running program's version, as its build gives it.</summary>
    public static string EntryVersion => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Logs any error no thread of this program caught, just before the program stops.</summary>
    public static void CatchUnhandled(string program) =>
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                ForThisComputer().Write(program, EntryVersion, exception);
            }
        };

    /// <summary>
    /// Writes one crash to a new file and returns its path. A log that cannot be written is given up
    /// silently (null): the program is already stopping, and the log must not stop it a second way.
    /// </summary>
    public string? Write(string program, string version, Exception exception)
    {
        // In UTC, so the time reads the same whichever zone the computer is set to.
        DateTimeOffset now = time.GetUtcNow().ToUniversalTime();
        try
        {
            Directory.CreateDirectory(folder);
            string text = string.Join(
                Environment.NewLine,
                $"{program} {version}",
                now.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture),
                RuntimeInformation.OSDescription,
                RuntimeInformation.FrameworkDescription,
                string.Empty,
                exception.ToString(),
                string.Empty);
            return WriteNew(now, text);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // A new file every time, even for two crashes in the same millisecond; an earlier log is never overwritten.
    private string WriteNew(DateTimeOffset now, string text)
    {
        string stem = Path.Combine(folder, "crash-" + now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture));
        for (int attempt = 0; ; attempt++)
        {
            string path = attempt == 0 ? stem + ".txt" : $"{stem}-{attempt}.txt";
            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
                using var writer = new StreamWriter(stream);
                writer.Write(text);
                return path;
            }
            catch (IOException) when (File.Exists(path))
            {
                // Taken already; the next number is tried.
            }
        }
    }
}
