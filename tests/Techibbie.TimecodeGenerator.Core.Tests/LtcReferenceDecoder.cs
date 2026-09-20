using System;
using System.Collections.Generic;

namespace Techibbie.TimecodeGenerator.Core.Tests;

/// <summary>
/// A from-scratch LTC decoder written directly from SMPTE 12M (zero-crossing → biphase-mark
/// bits → sync-word search → BCD fields), deliberately sharing no code with LtcGenerator so
/// it can act as an independent check on what the generator actually puts on the wire.
/// </summary>
public static class LtcReferenceDecoder
{
    public sealed record DecodedFrame(int Hour, int Minute, int Second, int Frame, bool DropFrame, double StartSample);

    private static readonly int[] SyncWord = { 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1 };

    public static List<DecodedFrame> Decode(float[] samples, int sampleRate, double nominalFps)
    {
        // 1. Transition positions (sign changes).
        var transitions = new List<int>();
        for (var i = 1; i < samples.Length; i++)
        {
            if ((samples[i - 1] > 0) != (samples[i] > 0)) transitions.Add(i);
        }

        var cell = sampleRate / (nominalFps * 80.0);

        // 2. Biphase-mark decode: a full-cell interval is a 0; two half-cell intervals are a 1.
        var bits = new List<int>();
        var bitStart = new List<double>();
        for (var k = 0; k + 1 < transitions.Count;)
        {
            var d = transitions[k + 1] - transitions[k];
            if (d > cell * 0.75)
            {
                bits.Add(0);
                bitStart.Add(transitions[k]);
                k += 1;
            }
            else if (k + 2 < transitions.Count && transitions[k + 2] - transitions[k + 1] <= cell * 0.75)
            {
                bits.Add(1);
                bitStart.Add(transitions[k]);
                k += 2;
            }
            else
            {
                k += 1; // lost alignment; resync on the next interval
            }
        }

        // 3. Find sync words; each ends an 80-bit frame.
        var frames = new List<DecodedFrame>();
        for (var i = 64; i + 16 <= bits.Count; i++)
        {
            var isSync = true;
            for (var s = 0; s < 16; s++)
            {
                if (bits[i + s] != SyncWord[s]) { isSync = false; break; }
            }
            if (!isSync) continue;

            var f = i - 64;
            int Field(int start, int count)
            {
                var v = 0;
                for (var b = 0; b < count; b++) v |= bits[f + start + b] << b;
                return v;
            }

            var frame = Field(0, 4) + 10 * Field(8, 2);
            var second = Field(16, 4) + 10 * Field(24, 3);
            var minute = Field(32, 4) + 10 * Field(40, 3);
            var hour = Field(48, 4) + 10 * Field(56, 2);
            frames.Add(new DecodedFrame(hour, minute, second, frame, bits[f + 10] == 1, bitStart[f]));
            i += 15; // skip past this sync word
        }

        return frames;
    }

    // ── Independent timecode ↔ frame-number oracle (standard SMPTE algorithms) ──

    public static long FrameNumberFromTimecode(int h, int m, int s, int f, int nominal, bool drop)
    {
        var totalMinutes = 60L * h + m;
        var number = ((totalMinutes * 60 + s) * nominal) + f;
        if (drop)
        {
            var dropPerMinute = nominal == 30 ? 2 : 4;
            number -= dropPerMinute * (totalMinutes - totalMinutes / 10);
        }
        return number;
    }

    public static (int H, int M, int S, int F) TimecodeFromFrameNumber(long frameNumber, int nominal, bool drop)
    {
        if (drop)
        {
            var dropPerMinute = nominal == 30 ? 2 : 4;
            var framesPer10Min = nominal * 600 - dropPerMinute * 9;
            var framesPerMin = nominal * 60 - dropPerMinute;
            var d = frameNumber / framesPer10Min;
            var mod = frameNumber % framesPer10Min;
            frameNumber += 9L * dropPerMinute * d;
            if (mod > dropPerMinute) frameNumber += dropPerMinute * ((mod - dropPerMinute) / framesPerMin);
        }

        var ff = (int)(frameNumber % nominal);
        var ss = (int)(frameNumber / nominal % 60);
        var mm = (int)(frameNumber / (nominal * 60L) % 60);
        var hh = (int)(frameNumber / (nominal * 3600L) % 24);
        return (hh, mm, ss, ff);
    }
}
