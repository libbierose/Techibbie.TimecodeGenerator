using System;
using Techibbie.TimecodeGenerator.Core.Ltc;
using Xunit;

namespace Techibbie.TimecodeGenerator.Core.Tests;

public class LtcRateAndCounterTests
{
    [Theory]
    // Standard LTC rates pass straight through.
    [InlineData(24.0, false, 24, false, 1)]
    [InlineData(25.0, false, 25, false, 1)]
    [InlineData(29.97, true, 30, true, 1)]
    [InlineData(30.0, false, 30, false, 1)]
    // Higher rates have no LTC of their own: sent at half/quarter rate.
    [InlineData(48.0, false, 24, false, 2)]
    [InlineData(50.0, false, 25, false, 2)]
    [InlineData(59.94, true, 30, true, 2)]
    [InlineData(60.0, false, 30, false, 2)]
    [InlineData(120.0, false, 30, false, 4)]
    public void FromFrameRate_MapsToAStandardLtcRate(double appFps, bool appDrop, int nominal, bool drop, int divisor)
    {
        var rate = LtcRate.FromFrameRate(appFps, appDrop);

        Assert.Equal(nominal, rate.NominalFps);
        Assert.Equal(drop, rate.DropFrame);
        Assert.Equal(divisor, rate.Divisor);
    }

    [Fact]
    public void FromFrameRate_Uses30000Over1001ForTheDropFrameFamily()
    {
        Assert.Equal(30000.0 / 1001.0, LtcRate.FromFrameRate(29.97, true).ExactFps, precision: 9);
        Assert.Equal(30000.0 / 1001.0, LtcRate.FromFrameRate(59.94, true).ExactFps, precision: 9);
    }

    [Fact]
    public void Counter_NonDropFrame_CountsEveryFrameNumber()
    {
        var c = new LtcFrameCounter(0, 0, 59, 29, nominalFps: 30, dropFrame: false);
        c.Advance();

        Assert.Equal((0, 1, 0, 0), (c.Hour, c.Minute, c.Second, c.Frame));
    }

    [Fact]
    public void Counter_DropFrame_SkipsFramesZeroAndOneAtANormalMinute()
    {
        var c = new LtcFrameCounter(0, 0, 59, 29, nominalFps: 30, dropFrame: true);
        c.Advance();

        Assert.Equal((0, 1, 0, 2), (c.Hour, c.Minute, c.Second, c.Frame));
    }

    [Fact]
    public void Counter_DropFrame_DoesNotSkipAtEveryTenthMinute()
    {
        var c = new LtcFrameCounter(0, 9, 59, 29, nominalFps: 30, dropFrame: true);
        c.Advance();

        Assert.Equal((0, 10, 0, 0), (c.Hour, c.Minute, c.Second, c.Frame));
    }

    [Fact]
    public void Counter_DropFrame_NormalisesAnInvalidStartingFrame()
    {
        // 00:01:00:00 doesn't exist in drop-frame; the first real frame of that minute is :02.
        var c = new LtcFrameCounter(0, 1, 0, 0, nominalFps: 30, dropFrame: true);

        Assert.Equal(2, c.Frame);
    }

    [Fact]
    public void Counter_WrapsAtTwentyFourHours()
    {
        var c = new LtcFrameCounter(23, 59, 59, 24, nominalFps: 25, dropFrame: false);
        c.Advance();

        Assert.Equal((0, 0, 0, 0), (c.Hour, c.Minute, c.Second, c.Frame));
    }

    [Fact]
    public void Counter_DropFrame_AnHourIs107892Frames()
    {
        // A real drop-frame hour has 107,892 frames; counting them must land on exactly 01:00:00:00.
        var c = new LtcFrameCounter(0, 0, 0, 0, nominalFps: 30, dropFrame: true);
        for (var i = 0; i < 107_892; i++) c.Advance();

        Assert.Equal((1, 0, 0, 0), (c.Hour, c.Minute, c.Second, c.Frame));
    }

    [Fact]
    public void Encoder_ScalesAHighFrameRateStartFrameDownToTheLtcRate()
    {
        // App frame 45 of 60 is frame 22 of 30 in half-rate LTC.
        var encoder = new LtcStreamEncoder(48000, appFps: 60.0, appDropFrame: false, 0, 0, 0, frame: 45);
        var frame = encoder.NextFrame();

        Assert.Equal(1600, frame.Length); // 48000 / 30
    }

    [Fact]
    public void Encoder_FrameLengthsAverageOutToTheExactFrameRate()
    {
        var encoder = new LtcStreamEncoder(48000, appFps: 29.97, appDropFrame: true, 0, 0, 0, 0);
        long total = 0;
        const int frames = 30_000;
        for (var i = 0; i < frames; i++) total += encoder.NextFrame().Length;

        // 30,000 frames at 30000/1001 fps is exactly 1001 seconds = 48,048,000 samples at 48 kHz.
        Assert.Equal(48_048_000L, total);
    }
}
