using Techibbie.TimecodeGenerator.Core.Ltc;
using Xunit;

namespace Techibbie.TimecodeGenerator.Core.Tests;

public class LtcGeneratorTests
{
    // Decodes the BCD fields back out of an 80-bit LTC frame, using the same
    // bit offsets the encoder writes them at (frame units 0-3/tens 8-9, second
    // units 16-19/tens 24-26, minute units 32-35/tens 40-42, hour units 48-51/tens 56-57).
    private static (int Hours, int Minutes, int Seconds, int Frames) Decode(byte[] bits)
    {
        int Bcd(int start, int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++) value += bits[start + i] * (1 << i);
            return value;
        }

        var frames = Bcd(0, 4) + Bcd(8, 2) * 10;
        var seconds = Bcd(16, 4) + Bcd(24, 3) * 10;
        var minutes = Bcd(32, 4) + Bcd(40, 3) * 10;
        var hours = Bcd(48, 4) + Bcd(56, 2) * 10;
        return (hours, minutes, seconds, frames);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(0, 0, 0, 5)]
    [InlineData(0, 0, 1, 0)]
    [InlineData(0, 1, 0, 0)]
    [InlineData(1, 0, 0, 0)]
    [InlineData(12, 34, 56, 15)]
    public void TimecodeToLtcFrame_RoundTripsThroughBcdFields(int hours, int minutes, int seconds, int frames)
    {
        var gen = new LtcGenerator(48000);
        var bits = gen.TimecodeToLtcFrame(hours, minutes, seconds, frames, 30.0);

        var decoded = Decode(bits);

        Assert.Equal((hours, minutes, seconds, frames), decoded);
    }

    [Fact]
    public void TimecodeToLtcFrame_SyncWordMatchesSmpte12M()
    {
        var gen = new LtcGenerator(48000);
        var bits = gen.TimecodeToLtcFrame(0, 0, 0, 0, 30.0);

        var syncBits = new byte[16];
        System.Array.Copy(bits, 64, syncBits, 0, 16);
        var syncString = string.Concat(syncBits);

        Assert.Equal("0011111111111101", syncString);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 1)]
    [InlineData(0, 0, 0, 5, 1)]
    [InlineData(0, 0, 1, 0, 0)]
    [InlineData(0, 1, 0, 0, 0)]
    [InlineData(1, 0, 0, 0, 0)]
    [InlineData(12, 34, 56, 15, 1)]
    public void TimecodeToLtcFrame_BmpcParityMatchesReferencePythonImplementation(
        int hours, int minutes, int seconds, int frames, int expectedParity)
    {
        var gen = new LtcGenerator(48000);
        var bits = gen.TimecodeToLtcFrame(hours, minutes, seconds, frames, 30.0);

        Assert.Equal(expectedParity, bits[27]);
    }

    [Fact]
    public void TimecodeToLtcFrame_SetsDropFrameFlagBit()
    {
        var gen = new LtcGenerator(48000);
        var bits = gen.TimecodeToLtcFrame(1, 2, 3, 4, 29.97, dropFrame: true);

        Assert.Equal(1, bits[10]);
    }

    [Fact]
    public void TimecodeToLtcFrame_LeavesDropFrameFlagClearWhenNotDropFrame()
    {
        var gen = new LtcGenerator(48000);
        var bits = gen.TimecodeToLtcFrame(1, 2, 3, 4, 30.0, dropFrame: false);

        Assert.Equal(0, bits[10]);
    }

    [Fact]
    public void BitsToAudio_ProducesOneFrameDurationAtGivenSampleRateAndFps()
    {
        var gen = new LtcGenerator(48000);
        var bits = gen.TimecodeToLtcFrame(1, 2, 3, 4, 29.97, dropFrame: true);

        var audio = gen.BitsToAudio(bits, 29.97);

        // 48000 / 29.97 ≈ 1601.6 samples/frame, rounds to 1602 — verified against the Python reference.
        Assert.Equal(1602, audio.Length);
        Assert.All(audio, sample => Assert.InRange(sample, -0.9f, 0.9f));
    }

    [Fact]
    public void GenerateContinuousLtc_ConcatenatesOneFramePerRequestedFrameCount()
    {
        var gen = new LtcGenerator(48000);
        var audio = gen.GenerateContinuousLtc(1, 0, 0, 0, 30.0, dropFrame: false, durationSeconds: 2.0);

        // 30 fps * 2s = 60 frames, each frame = 48000/30 = 1600 samples exactly.
        Assert.Equal(60 * 1600, audio.Length);
    }
}
