using Melanchall.DryWetMidi.Core;
using Techibbie.TimecodeGenerator.Audio;
using Xunit;

namespace Techibbie.TimecodeGenerator.Audio.Tests;

public class MtcFrameStateTests
{
    // Decodes the 8 quarter-frame pieces back into H:M:S:F, the same way a real MTC
    // receiver would — this is what actually proves the bit-splitting in BuildPiece is
    // correct, independent of any real MIDI hardware.
    private static (int Hour, int Minute, int Second, int Frame) DecodeAllPieces(MtcFrameState state)
    {
        var values = new int[8];
        for (var i = 0; i < 8; i++)
        {
            var (_, value) = state.BuildPiece(i);
            values[i] = value;
        }

        var frame = values[0] + (values[1] << 4);
        var second = values[2] + (values[3] << 4);
        var minute = values[4] + (values[5] << 4);
        var hour = values[6] + ((values[7] & 0x1) << 4);
        return (hour, minute, second, frame);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, 0, 0, 29)]
    [InlineData(1, 0, 0, 0)]
    [InlineData(12, 34, 56, 15)]
    [InlineData(23, 59, 59, 29)]
    public void BuildPiece_RoundTripsThroughAllEightPieces(int hour, int minute, int second, int frame)
    {
        var state = new MtcFrameState(hour, minute, second, frame, fps: 30.0, dropFrame: false);

        var decoded = DecodeAllPieces(state);

        Assert.Equal((hour, minute, second, frame), decoded);
    }

    [Theory]
    [InlineData(24.0, false, MidiTimeCodeType.TwentyFour)]
    [InlineData(25.0, false, MidiTimeCodeType.TwentyFive)]
    [InlineData(29.97, true, MidiTimeCodeType.ThirtyDrop)]
    [InlineData(30.0, false, MidiTimeCodeType.Thirty)]
    // MTC has no flag for these rates; they're sent at half rate, like LTC.
    [InlineData(48.0, false, MidiTimeCodeType.TwentyFour)]
    [InlineData(50.0, false, MidiTimeCodeType.TwentyFive)]
    [InlineData(59.94, true, MidiTimeCodeType.ThirtyDrop)]
    [InlineData(60.0, false, MidiTimeCodeType.Thirty)]
    [InlineData(120.0, false, MidiTimeCodeType.Thirty)]
    public void MapRate_MatchesExpectedMtcRateFlag(double fps, bool dropFrame, MidiTimeCodeType expected)
    {
        Assert.Equal(expected, MtcFrameState.MapRate(fps, dropFrame));
    }

    [Fact]
    public void BuildPiece_Piece7EncodesRateTypeInBits1And2()
    {
        var state = new MtcFrameState(0, 0, 0, 0, fps: 30.0, dropFrame: false);
        var (component, value) = state.BuildPiece(7);

        Assert.Equal(MidiTimeCodeComponent.HoursMsbAndTimeCodeType, component);
        // Thirty = 3 (0b11), shifted left by 1 -> bits 1-2 = 0b110 = 6; bit 0 (hours MSB) is 0 for hour 0.
        Assert.Equal(6, value);
    }

    [Theory]
    [InlineData(0, 2)]  // 0 + 2 = 2, no rollover at 30fps
    [InlineData(29, 1)] // 29 + 2 = 31, wraps once (30fps) -> frame 1
    public void Advance_RollsOverSecondsAtFrameBoundary(int startFrame, int expectedFrameAfterOneCycle)
    {
        var state = new MtcFrameState(0, 0, 0, startFrame, fps: 30.0, dropFrame: false);
        state.Advance(2);

        Assert.Equal(expectedFrameAfterOneCycle, state.Frame);
    }

    [Fact]
    public void Advance_RollsSecondsIntoMinutes()
    {
        var state = new MtcFrameState(0, 0, 59, 29, fps: 30.0, dropFrame: false);
        state.Advance(2);

        Assert.Equal((0, 1, 0, 1), (state.Hour, state.Minute, state.Second, state.Frame));
    }

    [Fact]
    public void Advance_RollsMinutesIntoHours()
    {
        var state = new MtcFrameState(0, 59, 59, 29, fps: 30.0, dropFrame: false);
        state.Advance(2);

        Assert.Equal((1, 0, 0, 1), (state.Hour, state.Minute, state.Second, state.Frame));
    }

    [Fact]
    public void Advance_WrapsHourAtMidnight()
    {
        var state = new MtcFrameState(23, 59, 59, 29, fps: 30.0, dropFrame: false);
        state.Advance(2);

        Assert.Equal((0, 0, 0, 1), (state.Hour, state.Minute, state.Second, state.Frame));
    }

    [Fact]
    public void Advance_DropFrame_SkipsFramesZeroAndOneAtANormalMinute()
    {
        var state = new MtcFrameState(0, 0, 59, 28, fps: 29.97, dropFrame: true);
        state.Advance(2); // 28 -> 29 -> next minute, where :00 and :01 don't exist

        Assert.Equal((0, 1, 0, 2), (state.Hour, state.Minute, state.Second, state.Frame));
    }

    [Fact]
    public void Advance_DropFrame_DoesNotSkipAtEveryTenthMinute()
    {
        var state = new MtcFrameState(0, 9, 59, 28, fps: 29.97, dropFrame: true);
        state.Advance(2);

        Assert.Equal((0, 10, 0, 0), (state.Hour, state.Minute, state.Second, state.Frame));
    }

    [Fact]
    public void HighFrameRate_IsSentAtHalfRateWithTheStartFrameScaledDown()
    {
        // 60 fps app rate -> 30 fps MTC; app frame 45 is MTC frame 22.
        var state = new MtcFrameState(0, 0, 0, 45, fps: 60.0, dropFrame: false);

        Assert.Equal(30.0, state.ExactFps);
        Assert.Equal(22, state.Frame);
    }

    [Fact]
    public void DropFrameFamily_UsesTheExact30000Over1001Rate()
    {
        var state = new MtcFrameState(0, 0, 0, 0, fps: 29.97, dropFrame: true);

        Assert.Equal(30000.0 / 1001.0, state.ExactFps, precision: 9);
    }
}
