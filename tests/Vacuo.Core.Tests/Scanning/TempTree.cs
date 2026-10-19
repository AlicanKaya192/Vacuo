using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Vacuo.Core.Tests.Scanning;

/// <summary>
/// A throwaway folder tree under the system temp folder. Every test builds its fake caches
/// here, so the engine never touches real user files; the whole tree is removed on dispose.
/// </summary>
internal sealed class TempTree : IDisposable
{
    private readonly List<(DirectoryInfo Directory, FileSystemAccessRule Rule)> _deniedRules = [];
    private readonly List<string> _links = [];

    public TempTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "vacuo-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string PathOf(string relativePath) => Path.Combine(Root, relativePath);

    public string AddFile(string relativePath, int sizeBytes)
    {
        string path = PathOf(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }

    public string AddDirectory(string relativePath) => Directory.CreateDirectory(PathOf(relativePath)).FullName;

    /// <summary>Junctions need no admin rights or Developer Mode, unlike symbolic links.</summary>
    public string AddJunction(string relativeLinkPath, string relativeTargetPath)
    {
        string link = PathOf(relativeLinkPath);
        string target = PathOf(relativeTargetPath);
        var startInfo = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using Process process = Process.Start(startInfo)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"mklink /J failed: {error}");
        _links.Add(link);
        return link;
    }

    /// <summary>Returns false when Windows refuses (no Developer Mode and no admin rights).</summary>
    public bool TryAddDirectorySymbolicLink(string relativeLinkPath, string relativeTargetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(PathOf(relativeLinkPath), PathOf(relativeTargetPath));
            _links.Add(PathOf(relativeLinkPath));
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    public void DenyListing(string relativePath)
    {
        var directory = new DirectoryInfo(PathOf(relativePath));
        var rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        DirectorySecurity security = directory.GetAccessControl();
        security.AddAccessRule(rule);
        directory.SetAccessControl(security);
        _deniedRules.Add((directory, rule));
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            RestoreDeniedAccess();
        }

        // A recursive delete removes a junction but still throws "access denied" (.NET 10),
        // so links go first, one by one; removing a link never touches its target.
        foreach (string link in _links)
        {
            Directory.Delete(link);
        }

        Directory.Delete(Root, recursive: true);
    }

    [SupportedOSPlatform("windows")]
    private void RestoreDeniedAccess()
    {
        foreach ((DirectoryInfo directory, FileSystemAccessRule rule) in _deniedRules)
        {
            DirectorySecurity security = directory.GetAccessControl();
            security.RemoveAccessRule(rule);
            directory.SetAccessControl(security);
        }
    }
}
