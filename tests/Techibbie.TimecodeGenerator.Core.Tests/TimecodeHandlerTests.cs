using Techibbie.TimecodeGenerator.Core.Timecode;
using Xunit;

namespace Techibbie.TimecodeGenerator.Core.Tests;

public class TimecodeHandlerTests
{
    [Theory]
    [InlineData(30.0, 0, "00:00:00:00")]
    [InlineData(30.0, 1, "00:00:00:01")]
    [InlineData(30.0, 29, "00:00:00:29")]
    [InlineData(30.0, 30, "00:00:01:00")]
    [InlineData(30.0, 1800, "00:01:00:00")]
    [InlineData(30.0, 108000, "01:00:00:00")]
    [InlineData(24.0, 24, "00:00:01:00")]
    [InlineData(25.0, 25, "00:00:01:00")]
    public void NonDropFrame_ConvertsElapsedFramesToTimecode(double fps, int frame, string expected)
    {
        var handler = new TimecodeHandler(fps);
        var elapsedSeconds = frame / fps;

        Assert.Equal(expected, handler.ElapsedTimeToTimecode(elapsedSeconds));
        Assert.False(handler.IsDropFrame());
    }

    [Fact]
    public void DropFrame_2997fps_IsFlaggedAsDropFrame()
    {
        var handler = new TimecodeHandler(29.97);
        Assert.True(handler.IsDropFrame());
    }

    // Expected values below were captured by running the original Python
    // TimecodeHandler.elapsed_time_to_timecode() for the same (frame / nominal-fps)
    // inputs, so the C# port is verified bit-for-bit against the reference
    // implementation rather than against a hand-derived drop-frame table.
    [Theory]
    [InlineData(0, "00:00:00:00")]
    [InlineData(29, "00:00:00:28")]
    [InlineData(30, "00:00:00:29")]
    [InlineData(1798, "00:00:59:26")]
    [InlineData(1799, "00:00:59:27")]
    [InlineData(1800, "00:00:59:28")]
    [InlineData(1801, "00:00:59:29")]
    [InlineData(17981, "00:09:59:09")]
    [InlineData(17982, "00:09:59:10")]
    [InlineData(17983, "00:09:59:11")]
    [InlineData(17984, "00:09:59:12")]
    [InlineData(35964, "00:19:58:22")]
    [InlineData(107892, "00:59:56:10")]
    [InlineData(107964, "00:59:58:22")]
    public void DropFrame_2997fps_MatchesReferencePythonImplementation(int frame, string expected)
    {
        var handler = new TimecodeHandler(29.97);
        Assert.Equal(expected, handler.ElapsedTimeToTimecode(frame / 30.0));
    }

    [Theory]
    [InlineData(0, "00:00:00:00")]
    [InlineData(59, "00:00:00:58")]
    [InlineData(60, "00:00:00:59")]
    [InlineData(3596, "00:00:59:52")]
    [InlineData(3597, "00:00:59:53")]
    [InlineData(3598, "00:00:59:54")]
    [InlineData(3599, "00:00:59:55")]
    [InlineData(3600, "00:00:59:56")]
    [InlineData(3601, "00:00:59:57")]
    [InlineData(35964, "00:09:59:20")]
    [InlineData(35970, "00:09:59:26")]
    public void DropFrame_5994fps_MatchesReferencePythonImplementation(int frame, string expected)
    {
        var handler = new TimecodeHandler(59.94);
        Assert.Equal(expected, handler.ElapsedTimeToTimecode(frame / 60.0));
    }

    [Fact]
    public void GetSupportedFps_IncludesAllStandardRates()
    {
        var handler = new TimecodeHandler(30.0);
        var fps = handler.GetSupportedFps();

        Assert.Contains(24.0, fps);
        Assert.Contains(25.0, fps);
        Assert.Contains(29.97, fps);
        Assert.Contains(30.0, fps);
        Assert.Contains(48.0, fps);
        Assert.Contains(50.0, fps);
        Assert.Contains(59.94, fps);
        Assert.Contains(60.0, fps);
        Assert.Contains(120.0, fps);
    }
}
