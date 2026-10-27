using Vacuo.Core.Organize;

namespace Vacuo.Core.Tests.Organize;

public class IconFramesTests
{
    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    // A frame is the PNG signature followed by its own size, so a test can tell which one came back.
    private static byte[] Frame(int size, bool png = true) =>
        [.. png ? PngSignature : [(byte)'B', (byte)'M', 0, 0, 0, 0, 0, 0], .. BitConverter.GetBytes(size)];

    private static byte[] Icon(params byte[][] frames)
    {
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Length);
        int offset = 6 + 16 * frames.Length;
        foreach (byte[] frame in frames)
        {
            int size = BitConverter.ToInt32(frame, frame.Length - 4);
            byte edge = size >= 256 ? (byte)0 : (byte)size;
            writer.Write(edge);
            writer.Write(edge);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(frame.Length);
            writer.Write(offset);
            offset += frame.Length;
        }

        foreach (byte[] frame in frames)
        {
            writer.Write(frame);
        }

        return stream.ToArray();
    }

    private static int SizeOf(byte[]? png) => BitConverter.ToInt32(png!, png!.Length - 4);

    [Theory]
    [InlineData(24, 24)]
    [InlineData(30, 32)]
    [InlineData(10, 16)]
    [InlineData(300, 256)]
    public void Png_PicksTheSmallestFrameThatIsBigEnough(int wanted, int expected)
    {
        byte[] icon = Icon(Frame(16), Frame(24), Frame(32), Frame(256));

        Assert.Equal(expected, SizeOf(IconFrames.Png(icon, wanted)));
    }

    [Fact]
    public void Png_FramesInAnyOrder_StillPicksBySize()
    {
        byte[] icon = Icon(Frame(256), Frame(16), Frame(48));

        Assert.Equal(48, SizeOf(IconFrames.Png(icon, 40)));
    }

    [Fact]
    public void Png_BitmapFramesOnly_IsNull()
    {
        Assert.Null(IconFrames.Png(Icon(Frame(32, png: false)), 32));
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0, 0, 1, 0, 9, 0 })]
    public void Png_NotAnIconOrCutShort_IsNull(byte[] icon)
    {
        Assert.Null(IconFrames.Png(icon, 32));
    }

    [Fact]
    public void Png_FrameReachingPastTheEnd_IsNull()
    {
        byte[] icon = Icon(Frame(32));

        Assert.Null(IconFrames.Png(icon[..^2], 32));
    }
}
