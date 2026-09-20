using System;
using System.Linq;
using Techibbie.TimecodeGenerator.Core.Ltc;
using Xunit;

namespace Techibbie.TimecodeGenerator.Core.Tests;

/// <summary>
/// End-to-end checks of what LtcGenerator actually emits: the audio is run through the
/// independent reference decoder, and the decoded timecode sequence and frame timing are
/// compared against an independent SMPTE oracle. This is what catches real-world problems
/// (drop-frame numbering, cumulative drift, wrong rates) that per-frame bit-pattern tests can't.
/// </summary>
public class LtcWaveformDecodeTests
{
    private static double ExactFps(double fps) =>
        Math.Abs(fps - 29.97) < 0.01 ? 30000.0 / 1001.0 : fps;

    [Theory]
    // Non-drop rates at both common sample rates.
    [InlineData(48000, 24.0, false, 0, 58)]
    [InlineData(48000, 25.0, false, 0, 58)]
    [InlineData(48000, 30.0, false, 0, 58)]
    [InlineData(44100, 25.0, false, 0, 58)]
    [InlineData(44100, 30.0, false, 0, 58)]
    // Drop-frame: crossing a normal minute boundary (frames :00/:01 must be skipped)...
    [InlineData(48000, 29.97, true, 0, 58)]
    [InlineData(44100, 29.97, true, 0, 58)]
    // ...and crossing a 10th-minute boundary (nothing is dropped there).
    [InlineData(48000, 29.97, true, 9, 58)]
    public void DecodedTimecodeSequence_MatchesTheSmpteOracle(int sampleRate, double fps, bool drop, int startMinute, int startSecond)
    {
        var nominal = (int)Math.Round(fps);
        var gen = new LtcGenerator(sampleRate);
        var audio = gen.GenerateContinuousLtc(0, startMinute, startSecond, 0, fps, drop, durationSeconds: 6.0);

        var decoded = LtcReferenceDecoder.Decode(audio, sampleRate, ExactFps(fps));
        Assert.True(decoded.Count > 100, $"Expected to decode >100 frames, got {decoded.Count}.");

        // The first decoded frame(s) may be lost while the decoder acquires bit sync, so find
        // where the decoded stream lines up with the oracle rather than assuming frame 0.
        var startNumber = LtcReferenceDecoder.FrameNumberFromTimecode(0, startMinute, startSecond, 0, nominal, drop);
        var first = decoded[0];
        var offset = -1;
        for (var candidate = 0; candidate < 4; candidate++)
        {
            var (h, m, s, f) = LtcReferenceDecoder.TimecodeFromFrameNumber(startNumber + candidate, nominal, drop);
            if ((first.Hour, first.Minute, first.Second, first.Frame) == (h, m, s, f)) { offset = candidate; break; }
        }
        Assert.True(offset >= 0, $"First decoded frame {first.Hour:D2}:{first.Minute:D2}:{first.Second:D2}:{first.Frame:D2} isn't one of the first frames of the requested start.");

        for (var j = 0; j < decoded.Count; j++)
        {
            var (h, m, s, f) = LtcReferenceDecoder.TimecodeFromFrameNumber(startNumber + offset + j, nominal, drop);
            var actual = decoded[j];
            Assert.True((actual.Hour, actual.Minute, actual.Second, actual.Frame) == (h, m, s, f),
                $"Frame #{j}: expected {h:D2}:{m:D2}:{s:D2}:{f:D2} but decoded {actual.Hour:D2}:{actual.Minute:D2}:{actual.Second:D2}:{actual.Frame:D2}");
            Assert.Equal(drop, actual.DropFrame);
        }
    }

    [Theory]
    // App rates above 30 fps have no LTC of their own; they must come out as standard
    // half-rate LTC that an ordinary decoder reads at 24/25/30 (see LtcRate).
    [InlineData(44100, 60.0, false, 30, false)]
    [InlineData(48000, 50.0, false, 25, false)]
    [InlineData(48000, 48.0, false, 24, false)]
    [InlineData(44100, 59.94, true, 30, true)]
    public void HighFrameRates_AreSentAsStandardHalfRateLtc(int sampleRate, double appFps, bool appDrop, int ltcNominal, bool ltcDrop)
    {
        var gen = new LtcGenerator(sampleRate);
        var audio = gen.GenerateContinuousLtc(0, 0, 58, 0, appFps, appDrop, durationSeconds: 6.0);

        var exactLtcFps = Techibbie.TimecodeGenerator.Core.Ltc.LtcRate.FromFrameRate(appFps, appDrop).ExactFps;
        var decoded = LtcReferenceDecoder.Decode(audio, sampleRate, exactLtcFps);

        Assert.True(decoded.Count > 100, $"Expected to decode >100 frames at the LTC rate, got {decoded.Count}.");
        Assert.All(decoded, d => Assert.True(d.Frame < ltcNominal, $"Frame {d.Frame} is out of range for {ltcNominal} fps LTC."));
        Assert.All(decoded, d => Assert.Equal(ltcDrop, d.DropFrame));

        // Consecutive decoded frames must be consecutive LTC frames (drop-frame aware).
        for (var i = 1; i < decoded.Count; i++)
        {
            var prev = decoded[i - 1];
            var cur = decoded[i];
            var prevN = LtcReferenceDecoder.FrameNumberFromTimecode(prev.Hour, prev.Minute, prev.Second, prev.Frame, ltcNominal, ltcDrop);
            var curN = LtcReferenceDecoder.FrameNumberFromTimecode(cur.Hour, cur.Minute, cur.Second, cur.Frame, ltcNominal, ltcDrop);
            Assert.Equal(prevN + 1, curN);
        }
    }

    [Theory]
    [InlineData(48000, 25.0, false)]
    [InlineData(44100, 30.0, false)]
    [InlineData(48000, 29.97, true)]
    [InlineData(44100, 29.97, true)]
    public void FrameTiming_TracksTheTrueFrameRateWithoutDrift(int sampleRate, double fps, bool drop)
    {
        var gen = new LtcGenerator(sampleRate);
        var audio = gen.GenerateContinuousLtc(0, 0, 0, 0, fps, drop, durationSeconds: 60.0);

        var decoded = LtcReferenceDecoder.Decode(audio, sampleRate, ExactFps(fps));
        var exactFps = ExactFps(fps);

        // Every decoded frame's start should sit within a sample or two of where an ideal
        // clock running at the true rate would put it. Rounding each frame to whole samples
        // independently would drift by many samples over a minute; this catches that.
        var last = decoded[^1];
        var nominal = (int)Math.Round(fps);
        var lastNumber = LtcReferenceDecoder.FrameNumberFromTimecode(last.Hour, last.Minute, last.Second, last.Frame, nominal, drop);
        var firstNumber = LtcReferenceDecoder.FrameNumberFromTimecode(decoded[0].Hour, decoded[0].Minute, decoded[0].Second, decoded[0].Frame, nominal, drop);

        var expectedSpan = (lastNumber - firstNumber) * sampleRate / exactFps;
        var actualSpan = last.StartSample - decoded[0].StartSample;

        Assert.InRange(actualSpan - expectedSpan, -3.0, 3.0);
    }
}
