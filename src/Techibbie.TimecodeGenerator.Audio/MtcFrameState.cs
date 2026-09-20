using System;
using Melanchall.DryWetMidi.Core;
using Techibbie.TimecodeGenerator.Core.Ltc;

namespace Techibbie.TimecodeGenerator.Audio;

/// <summary>
/// The pure MTC quarter-frame protocol logic (BCD/nibble splitting, rate-flag mapping,
/// 2-frame-per-cycle advancing), with no MIDI I/O — kept separate from <see cref="MtcGenerator"/>
/// so the protocol correctness can be unit tested without a real MIDI output device.
///
/// MTC only defines 24/25/30-drop/30 rates, so higher app frame rates are sent at half/quarter
/// rate exactly like LTC (see <see cref="LtcRate"/>), and drop-frame numbering is honoured.
/// </summary>
public sealed class MtcFrameState
{
    private readonly LtcFrameCounter _counter;

    public int Hour => _counter.Hour;
    public int Minute => _counter.Minute;
    public int Second => _counter.Second;
    public int Frame => _counter.Frame;

    /// <summary>The actual MTC frame rate (after half/quarter-rate reduction) — quarter-frame messages go out at 4x this.</summary>
    public double ExactFps { get; }
    public MidiTimeCodeType RateType { get; }

    public MtcFrameState(int hour, int minute, int second, int frame, double fps, bool dropFrame)
    {
        var rate = LtcRate.FromFrameRate(fps, dropFrame);
        var appNominal = Math.Max(1, (int)Math.Round(fps));
        var scaledFrame = Math.Min(frame * rate.NominalFps / appNominal, rate.NominalFps - 1);

        _counter = new LtcFrameCounter(hour, minute, second, scaledFrame, rate.NominalFps, rate.DropFrame);
        ExactFps = rate.ExactFps;
        RateType = MapRate(fps, dropFrame);
    }

    public static MidiTimeCodeType MapRate(double fps, bool dropFrame)
    {
        var rate = LtcRate.FromFrameRate(fps, dropFrame);
        if (rate.NominalFps == 24) return MidiTimeCodeType.TwentyFour;
        if (rate.NominalFps == 25) return MidiTimeCodeType.TwentyFive;
        return rate.DropFrame ? MidiTimeCodeType.ThirtyDrop : MidiTimeCodeType.Thirty;
    }

    /// <summary>Build quarter-frame piece <paramref name="piece"/> (0-7) for the current H:M:S:F.</summary>
    public (MidiTimeCodeComponent Component, int Value) BuildPiece(int piece) => piece switch
    {
        0 => (MidiTimeCodeComponent.FramesLsb, Frame & 0xF),
        1 => (MidiTimeCodeComponent.FramesMsb, (Frame >> 4) & 0x1),
        2 => (MidiTimeCodeComponent.SecondsLsb, Second & 0xF),
        3 => (MidiTimeCodeComponent.SecondsMsb, (Second >> 4) & 0x3),
        4 => (MidiTimeCodeComponent.MinutesLsb, Minute & 0xF),
        5 => (MidiTimeCodeComponent.MinutesMsb, (Minute >> 4) & 0x3),
        6 => (MidiTimeCodeComponent.HoursLsb, Hour & 0xF),
        7 => (MidiTimeCodeComponent.HoursMsbAndTimeCodeType, ((Hour >> 4) & 0x1) | ((int)RateType << 1)),
        _ => throw new ArgumentOutOfRangeException(nameof(piece), piece, "MTC quarter-frame piece index must be 0-7."),
    };

    /// <summary>Advance the clock by <paramref name="frameCount"/> frames, with drop-frame skipping and wrapping at 24:00:00.</summary>
    public void Advance(int frameCount)
    {
        for (var i = 0; i < frameCount; i++) _counter.Advance();
    }
}
