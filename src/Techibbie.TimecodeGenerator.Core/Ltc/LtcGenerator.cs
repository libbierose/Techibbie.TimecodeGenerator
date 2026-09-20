using System;
using System.Collections.Generic;

namespace Techibbie.TimecodeGenerator.Core.Ltc;

/// <summary>
/// Generates Linear Timecode (LTC) audio per the SMPTE 12M standard: an 80-bit
/// BCD-encoded frame transmitted as a self-clocking Biphase Mark Code (BMC) signal.
/// </summary>
public sealed class LtcGenerator
{
    private static readonly byte[] SyncWord = { 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1 };

    public int SampleRate { get; }

    public LtcGenerator(int sampleRate = 48000)
    {
        SampleRate = sampleRate;
    }

    /// <summary>
    /// Build the 80-bit SMPTE 12M frame for the given timecode.
    ///
    /// Bit layout: frame units (0-3), user bits 1 (4-7), frame tens (8-9),
    /// drop-frame flag (10), color-frame flag (11), user bits 2 (12-15),
    /// second units (16-19), user bits 3 (20-23), second tens (24-26),
    /// BMPC parity (27), user bits 4 (28-31), minute units (32-35), user bits 5
    /// (36-39), minute tens (40-42), binary group flag 0 (43), user bits 6
    /// (44-47), hour units (48-51), user bits 7 (52-55), hour tens (56-57),
    /// binary group flag 1 (58), reserved (59), user bits 8 (60-63), sync word (64-79).
    /// </summary>
    public byte[] TimecodeToLtcFrame(int hours, int minutes, int seconds, int frames, double fps, bool dropFrame = false)
    {
        var bits = new byte[80];

        SetBcd(bits, 0, 4, frames % 10);
        SetBcd(bits, 8, 2, frames / 10);

        bits[10] = dropFrame ? (byte)1 : (byte)0;
        // Bit 11 (color frame flag) stays 0.

        SetBcd(bits, 16, 4, seconds % 10);
        SetBcd(bits, 24, 3, seconds / 10);

        // Bit 27 (BMPC parity) is set below once every other bit is known.

        SetBcd(bits, 32, 4, minutes % 10);
        SetBcd(bits, 40, 3, minutes / 10);

        SetBcd(bits, 48, 4, hours % 10);
        SetBcd(bits, 56, 2, hours / 10);

        Array.Copy(SyncWord, 0, bits, 64, SyncWord.Length);

        bits[27] = 0;
        var ones = 0;
        foreach (var b in bits) ones += b;
        if (ones % 2 != 0) bits[27] = 1;

        return bits;
    }

    private static void SetBcd(byte[] bits, int offset, int count, int value)
    {
        for (var i = 0; i < count; i++)
        {
            bits[offset + i] = (byte)((value >> i) & 1);
        }
    }

    /// <summary>
    /// Convert an 80-bit LTC frame to a Biphase Mark Code audio waveform.
    ///
    /// There is always a transition at the start of every bit cell; a '1' bit
    /// has an additional transition at the midpoint, a '0' bit does not. The
    /// frame occupies exactly 1/fps seconds regardless of sample rate.
    /// </summary>
    public float[] BitsToAudio(byte[] bits, double fps) =>
        BitsToAudio(bits, (int)Math.Round(SampleRate / fps));

    /// <summary>Same as above, but with the frame length in samples chosen by the caller (see <see cref="LtcStreamEncoder"/>).</summary>
    public float[] BitsToAudio(byte[] bits, int totalSamples)
    {
        var samplesPerFrame = (double)totalSamples;
        var audio = new float[totalSamples];

        float level = 1.0f;

        for (var i = 0; i < bits.Length; i++)
        {
            var nStart = (int)Math.Round(i * samplesPerFrame / 80.0);
            var nEnd = (int)Math.Round((i + 1) * samplesPerFrame / 80.0);
            nEnd = Math.Min(nEnd, totalSamples);

            level = -level;

            if (bits[i] == 0)
            {
                for (var n = nStart; n < nEnd; n++) audio[n] = level;
            }
            else
            {
                var nMid = (nStart + nEnd) / 2;
                for (var n = nStart; n < nMid; n++) audio[n] = level;
                level = -level;
                for (var n = nMid; n < nEnd; n++) audio[n] = level;
            }
        }

        for (var n = 0; n < audio.Length; n++) audio[n] *= Amplitude;
        return audio;
    }

    /// <summary>
    /// Peak level, about -6 dBFS. LTC readers (Resolve's "Update Timecode from Audio" included)
    /// are documented to struggle with signals that are either too quiet or too hot, and a
    /// full-scale square wave is very hot once it passes through a DAC, mixer or camera input.
    /// </summary>
    public const float Amplitude = 0.5f;

    /// <summary>Generate one LTC frame of audio for the given timecode.</summary>
    public float[] GenerateLtcAudio(int hours, int minutes, int seconds, int frames, double fps, bool dropFrame = false)
    {
        var bits = TimecodeToLtcFrame(hours, minutes, seconds, frames, fps, dropFrame);
        return BitsToAudio(bits, fps);
    }

    /// <summary>
    /// Generate continuous LTC audio spanning multiple consecutive frames, concatenated
    /// into one waveform suitable for writing to a WAV file or streaming as a reference track.
    /// </summary>
    public float[] GenerateContinuousLtc(int hours, int minutes, int seconds, int frames, double fps,
        bool dropFrame = false, double durationSeconds = 1.0)
    {
        var encoder = new LtcStreamEncoder(SampleRate, fps, dropFrame, hours, minutes, seconds, frames);
        var framesToGenerate = (int)(durationSeconds * encoder.Rate.ExactFps);

        var chunks = new List<float[]>(framesToGenerate);
        for (var i = 0; i < framesToGenerate; i++)
        {
            chunks.Add(encoder.NextFrame());
        }

        if (chunks.Count == 0) return Array.Empty<float>();

        var total = 0;
        foreach (var c in chunks) total += c.Length;
        var result = new float[total];
        var pos = 0;
        foreach (var c in chunks)
        {
            Array.Copy(c, 0, result, pos, c.Length);
            pos += c.Length;
        }
        return result;
    }
}
