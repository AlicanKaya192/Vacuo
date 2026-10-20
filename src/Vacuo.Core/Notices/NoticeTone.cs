namespace Vacuo.Core.Notices;

/// <summary>
/// Vacuo's own notification sound (vacuo-kurallar 16): two soft, rising sine notes, made in code, so there is
/// no sound file and nothing to download. Quiet on purpose: the loudest sample is <see cref="Peak"/> of full
/// scale. Each note rises over a few milliseconds and dies away like a small bell, and the wave starts and
/// ends at zero, so it never clicks. Returned as a whole 16-bit mono PCM .wav file, ready to play from memory.
/// </summary>
public static class NoticeTone
{
    public const int SampleRate = 44_100;
    public const double Peak = 0.2;

    private const short Channels = 1;
    private const short BitsPerSample = 16;
    private const short PcmFormat = 1;
    private const double AttackSeconds = 0.008;
    private const double ReleaseSeconds = 0.012;
    // How fast a note dies away; at this rate it is about a fifth as loud after 0.1 s.
    private const double DecaySeconds = 0.065;

    // E5 then B5, overlapping a little: a calm "done" rather than an alert.
    private static readonly Note[] Notes =
    [
        new(Frequency: 659.25, Start: 0.0, Length: 0.24),
        new(Frequency: 987.77, Start: 0.11, Length: 0.33),
    ];

    public static byte[] Wave()
    {
        double length = Notes.Max(note => note.Start + note.Length);
        int count = (int)(length * SampleRate);
        double perNote = Peak / Notes.Length;
        var samples = new short[count];
        for (int index = 0; index < count; index++)
        {
            double time = (double)index / SampleRate;
            double value = Notes.Sum(note => note.ValueAt(time) * perNote);
            samples[index] = (short)Math.Round(value * short.MaxValue);
        }

        return WithHeader(samples);
    }

    private static byte[] WithHeader(short[] samples)
    {
        int dataBytes = samples.Length * sizeof(short);
        short blockAlign = Channels * BitsPerSample / 8;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write(PcmFormat);
        writer.Write(Channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * blockAlign);
        writer.Write(blockAlign);
        writer.Write(BitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (short sample in samples)
        {
            writer.Write(sample);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private sealed record Note(double Frequency, double Start, double Length)
    {
        public double ValueAt(double time)
        {
            double t = time - Start;
            if (t < 0 || t >= Length)
            {
                return 0;
            }

            double attack = Math.Min(1, t / AttackSeconds);
            double release = Math.Min(1, (Length - t) / ReleaseSeconds);
            double decay = Math.Exp(-t / DecaySeconds);
            return Math.Sin(2 * Math.PI * Frequency * t) * attack * release * decay;
        }
    }
}
