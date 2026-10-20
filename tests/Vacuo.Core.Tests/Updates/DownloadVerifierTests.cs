using System.Security.Cryptography;
using Vacuo.Core.Tests.Scanning;
using Vacuo.Core.Updates;

namespace Vacuo.Core.Tests.Updates;

public class DownloadVerifierTests
{
    private const string Name = "Vacuo-0.5.0-setup.exe";

    [Fact]
    public void MatchingProgram_IsAccepted()
    {
        using var tree = new TempTree();
        (string path, byte[] content) = Program(tree);

        DownloadVerdict verdict = DownloadVerifier.Verify(path, content.Length, $"{Hash(content)}  {Name}", Name);

        Assert.Equal(DownloadVerdict.Accepted, verdict);
    }

    [Fact]
    public void ChecksumWithoutAName_OrInCapitals_IsAccepted()
    {
        using var tree = new TempTree();
        (string path, byte[] content) = Program(tree);

        Assert.Equal(DownloadVerdict.Accepted, DownloadVerifier.Verify(path, content.Length, Hash(content), Name));
        Assert.Equal(DownloadVerdict.Accepted, DownloadVerifier.Verify(path, content.Length, Hash(content).ToUpperInvariant() + "\r\n", Name));
    }

    [Fact]
    public void SizeOtherThanTheReleaseSays_IsRejected()
    {
        using var tree = new TempTree();
        (string path, byte[] content) = Program(tree);

        Assert.Equal(DownloadVerdict.WrongSize, DownloadVerifier.Verify(path, content.Length + 1, Hash(content), Name));
    }

    [Fact]
    public void MissingFile_IsRejected()
    {
        using var tree = new TempTree();

        Assert.Equal(DownloadVerdict.WrongSize, DownloadVerifier.Verify(tree.PathOf(Name), 10, new string('0', 64), Name));
    }

    [Fact]
    public void FileThatIsNotAProgram_IsRejected()
    {
        using var tree = new TempTree();
        byte[] content = "<html>Not Found</html>"u8.ToArray();
        string path = tree.PathOf(Name);
        File.WriteAllBytes(path, content);

        Assert.Equal(DownloadVerdict.NotAProgram, DownloadVerifier.Verify(path, content.Length, Hash(content), Name));
    }

    [Fact]
    public void ChangedProgram_IsRejected()
    {
        using var tree = new TempTree();
        (string path, byte[] content) = Program(tree);
        string expected = Hash(content);
        content[^1] ^= 0xFF;
        File.WriteAllBytes(path, content);

        Assert.Equal(DownloadVerdict.ChecksumMismatch, DownloadVerifier.Verify(path, content.Length, expected, Name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a checksum")]
    [InlineData("abc123  Vacuo-0.5.0-setup.exe")]
    public void UnreadableChecksum_IsRejected(string checksum)
    {
        using var tree = new TempTree();
        (string path, byte[] content) = Program(tree);

        Assert.Equal(DownloadVerdict.ChecksumUnreadable, DownloadVerifier.Verify(path, content.Length, checksum, Name));
    }

    [Fact]
    public void ChecksumForAnotherFile_IsRejected()
    {
        using var tree = new TempTree();
        (string path, byte[] content) = Program(tree);

        DownloadVerdict verdict = DownloadVerifier.Verify(path, content.Length, $"{Hash(content)}  Vacuo-0.4.0-setup.exe", Name);

        Assert.Equal(DownloadVerdict.ChecksumUnreadable, verdict);
    }

    private static (string Path, byte[] Content) Program(TempTree tree)
    {
        byte[] content = new byte[4096];
        Random.Shared.NextBytes(content);
        content[0] = (byte)'M';
        content[1] = (byte)'Z';
        string path = tree.PathOf(Name);
        File.WriteAllBytes(path, content);
        return (path, content);
    }

    private static string Hash(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));
}
