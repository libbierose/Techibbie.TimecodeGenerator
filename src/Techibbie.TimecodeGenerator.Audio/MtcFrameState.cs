using System;
using Melanchall.DryWetMidi.Core;

namespace Techibbie.TimecodeGenerator.Audio;

/// <summary>
/// The pure MTC quarter-frame protocol logic (BCD/nibble splitting, rate-flag mapping,
/// 2-frame-per-cycle advancing), with no MIDI I/O — kept separate from <see cref="MtcGenerator"/>
/// so the protocol correctness can be unit tested without a real MIDI output device.
/// </summary>
public sealed class MtcFrameState
{
    public int Hour { get; private set; }
    public int Minute { get; private set; }
    public int Second { get; private set; }
    public int Frame { get; private set; }
    public double Fps { get; }
    public MidiTimeCodeType RateType { get; }

    public MtcFrameState(int hour, int minute, int second, int frame, double fps, bool dropFrame)
    {
        Hour = hour;
        Minute = minute;
        Second = second;
        Frame = frame;
        Fps = fps;
        RateType = MapRate(fps, dropFrame);
    }

    public static MidiTimeCodeType MapRate(double fps, bool dropFrame)
    {
        if (Math.Abs(fps - 24) < 0.01) return MidiTimeCodeType.TwentyFour;
        if (Math.Abs(fps - 25) < 0.01) return MidiTimeCodeType.TwentyFive;
        return dropFrame ? MidiTimeCodeType.ThirtyDrop : MidiTimeCodeType.Thirty;
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

    /// <summary>Advance the clock by <paramref name="frameCount"/> frames, rolling seconds/minutes/hours (wrapping at 24:00:00) as needed.</summary>
    public void Advance(int frameCount)
    {
        var framesPerSecond = (int)Math.Round(Fps);
        Frame += frameCount;
        while (Frame >= framesPerSecond)
        {
            Frame -= framesPerSecond;
            Second++;
            if (Second >= 60)
            {
                Second = 0;
                Minute++;
                if (Minute >= 60)
                {
                    Minute = 0;
                    Hour++;
                    if (Hour >= 24) Hour = 0;
                }
            }
        }
    }
}
