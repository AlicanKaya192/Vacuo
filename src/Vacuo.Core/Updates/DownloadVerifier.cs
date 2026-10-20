using System.Security.Cryptography;

namespace Vacuo.Core.Updates;

public enum DownloadVerdict
{
    Accepted,
    WrongSize,
    NotAProgram,
    ChecksumUnreadable,
    ChecksumMismatch,
}

/// <summary>
/// Whether a downloaded setup program may be started: the size the release lists, a Windows program
/// (<c>MZ</c>), and the SHA-256 in the release's <c>.sha256</c> file ("hash  name", written by
/// tools/build-installer.ps1). Odyssey checked only the first two; the checksum catches a changed file.
/// </summary>
public static class DownloadVerifier
{
    private const int Sha256HexLength = 64;

    public static DownloadVerdict Verify(string path, long expectedSize, string checksumText, string fileName)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != expectedSize)
        {
            return DownloadVerdict.WrongSize;
        }

        if (ReadChecksum(checksumText, fileName) is not { } expected)
        {
            return DownloadVerdict.ChecksumUnreadable;
        }

        using FileStream stream = file.OpenRead();
        if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
        {
            return DownloadVerdict.NotAProgram;
        }

        stream.Position = 0;
        return Convert.ToHexStringLower(SHA256.HashData(stream)) == expected
            ? DownloadVerdict.Accepted
            : DownloadVerdict.ChecksumMismatch;
    }

    /// <summary>The hash alone, or the hash and this file's name; anything else is not this file's checksum.</summary>
    private static string? ReadChecksum(string text, string fileName)
    {
        string[] fields = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length is 0 or > 2 || (fields.Length == 2 && fields[1].TrimStart('*') != fileName))
        {
            return null;
        }

        string hash = fields[0];
        return hash.Length == Sha256HexLength && hash.All(char.IsAsciiHexDigit) ? hash.ToLowerInvariant() : null;
    }
}
