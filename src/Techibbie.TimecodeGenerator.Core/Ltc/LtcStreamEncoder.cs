using System;

namespace Techibbie.TimecodeGenerator.Core.Ltc;

/// <summary>
/// Produces a continuous LTC stream one frame at a time — used both by the live audio
/// callback and by WAV export so they can never disagree.
///
/// Each frame's length in samples comes from cumulative rounding of the ideal position
/// (frame n starts at round(n * sampleRate / fps)) rather than rounding every frame to a
/// whole number of samples independently. Per-frame rounding is what made 29.97 fps drift
/// (1601.6 samples/frame rounded to 1602 runs 0.025% slow — about a second per hour).
/// </summary>
public sealed class LtcStreamEncoder
{
    private readonly LtcGenerator _generator;
    private readonly LtcFrameCounter _counter;
    private readonly double _samplesPerFrame;
    private long _frameIndex;

    public LtcRate Rate { get; }

    /// <param name="frame">Start frame, in <b>app</b> frame-rate terms; scaled down for half/quarter-rate LTC.</param>
    public LtcStreamEncoder(int sampleRate, double appFps, bool appDropFrame, int hour, int minute, int second, int frame)
    {
        _generator = new LtcGenerator(sampleRate);
        Rate = LtcRate.FromFrameRate(appFps, appDropFrame);

        var appNominal = Math.Max(1, (int)Math.Round(appFps));
        var ltcFrame = Math.Min(frame * Rate.NominalFps / appNominal, Rate.NominalFps - 1);

        _counter = new LtcFrameCounter(hour, minute, second, ltcFrame, Rate.NominalFps, Rate.DropFrame);
        _samplesPerFrame = sampleRate / Rate.ExactFps;
    }

    /// <summary>Audio for the current timecode; advances the counter afterwards.</summary>
    public float[] NextFrame()
    {
        var start = (long)Math.Round(_frameIndex * _samplesPerFrame, MidpointRounding.AwayFromZero);
        var end = (long)Math.Round((_frameIndex + 1) * _samplesPerFrame, MidpointRounding.AwayFromZero);

        var bits = _generator.TimecodeToLtcFrame(
            _counter.Hour, _counter.Minute, _counter.Second, _counter.Frame, Rate.ExactFps, Rate.DropFrame);
        var audio = _generator.BitsToAudio(bits, (int)(end - start));

        _counter.Advance();
        _frameIndex++;
        return audio;
    }
}
