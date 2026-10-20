using System.Text;
using Vacuo.Core.Notices;

namespace Vacuo.Core.Tests.Notices;

public class NoticeToneTests
{
    private const int HeaderBytes = 44;

    private static short[] Samples(byte[] wave)
    {
        var samples = new short[(wave.Length - HeaderBytes) / 2];
        Buffer.BlockCopy(wave, HeaderBytes, samples, 0, samples.Length * 2);
        return samples;
    }

    [Fact]
    public void Wave_IsMono16BitPcm()
    {
        byte[] wave = NoticeTone.Wave();

        Assert.Equal("RIFF", Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal(wave.Length - 8, BitConverter.ToInt32(wave, 4));
        Assert.Equal("WAVEfmt ", Encoding.ASCII.GetString(wave, 8, 8));
        Assert.Equal(1, BitConverter.ToInt16(wave, 20));
        Assert.Equal(1, BitConverter.ToInt16(wave, 22));
        Assert.Equal(NoticeTone.SampleRate, BitConverter.ToInt32(wave, 24));
        Assert.Equal(16, BitConverter.ToInt16(wave, 34));
        Assert.Equal("data", Encoding.ASCII.GetString(wave, 36, 4));
        Assert.Equal(wave.Length - HeaderBytes, BitConverter.ToInt32(wave, 40));
    }

    [Fact]
    public void Wave_IsShort()
    {
        double seconds = (double)Samples(NoticeTone.Wave()).Length / NoticeTone.SampleRate;

        Assert.InRange(seconds, 0.25, 0.6);
    }

    [Fact]
    public void Wave_StaysQuiet()
    {
        int loudest = Samples(NoticeTone.Wave()).Max(sample => Math.Abs((int)sample));

        Assert.True(loudest > 0);
        Assert.True(loudest <= NoticeTone.Peak * short.MaxValue + 1, $"loudest sample {loudest}");
    }

    // A wave that starts or ends away from zero clicks.
    [Fact]
    public void Wave_StartsAndEndsInSilence()
    {
        short[] samples = Samples(NoticeTone.Wave());

        Assert.InRange(samples[0], -1, 1);
        Assert.InRange(samples[^1], -1, 1);
    }
}
