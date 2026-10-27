namespace Vacuo.Core.Organize;

/// <summary>
/// Reads one frame out of the folder icons <c>tools\make-folder-icons.ps1</c> writes, whose frames are PNGs,
/// so the Düzen preview can show the frame drawn for its size instead of a scaled one (Aşama 3h).
/// </summary>
public static class IconFrames
{
    private const int HeaderBytes = 6;
    private const int EntryBytes = 16;
    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G'];

    /// <summary>
    /// The PNG of the smallest frame at least <paramref name="size"/> pixels wide, else of the largest;
    /// <see langword="null"/> when the file holds no PNG frame or is not an icon.
    /// </summary>
    public static byte[]? Png(byte[] icon, int size)
    {
        if (icon.Length < HeaderBytes || BitConverter.ToUInt16(icon, 2) != 1)
        {
            return null;
        }

        int count = BitConverter.ToUInt16(icon, 4);
        if (icon.Length < HeaderBytes + count * EntryBytes)
        {
            return null;
        }

        List<(int Size, byte[] Png)> frames = Enumerable.Range(0, count)
            .Select(index => FrameAt(icon, HeaderBytes + index * EntryBytes))
            .OfType<(int, byte[])>()
            .OrderBy(frame => frame.Item1)
            .ToList();
        if (frames.Count == 0)
        {
            return null;
        }

        return frames.FirstOrDefault(frame => frame.Size >= size, frames[^1]).Png;
    }

    // An ICONDIRENTRY: width (0 means 256), height, colours, reserved, planes, bit count, byte count, offset.
    private static (int, byte[])? FrameAt(byte[] icon, int entry)
    {
        int size = icon[entry] == 0 ? 256 : icon[entry];
        long length = BitConverter.ToUInt32(icon, entry + 8);
        long offset = BitConverter.ToUInt32(icon, entry + 12);
        if (offset + length > icon.Length || length < PngSignature.Length
            || !icon.AsSpan((int)offset, PngSignature.Length).SequenceEqual(PngSignature))
        {
            return null;
        }

        return (size, icon[(int)offset..(int)(offset + length)]);
    }
}
