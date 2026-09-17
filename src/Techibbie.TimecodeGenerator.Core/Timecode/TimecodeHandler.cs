using System.Collections.Generic;
using System.Linq;

namespace Techibbie.TimecodeGenerator.Core.Timecode;

/// <summary>Handles timecode calculations for various frame rates (NDF and drop-frame).</summary>
public sealed class TimecodeHandler
{
    private static readonly Dictionary<double, bool> FrameRates = new()
    {
        [24] = false,
        [25] = false,
        [29.97] = true,
        [30] = false,
        [48] = false,
        [50] = false,
        [59.94] = true,
        [60] = false,
        [120] = false,
    };

    public double Fps { get; }
    public bool DropFrame { get; }

    public TimecodeHandler(double fps = 30.0)
    {
        if (!FrameRates.ContainsKey(fps))
        {
            // Custom FPS values default to non-drop-frame.
            FrameRates[fps] = false;
        }

        Fps = fps;
        DropFrame = FrameRates[fps];
    }

    /// <summary>Convert elapsed time in seconds to a timecode string HH:MM:SS:FF.</summary>
    public string ElapsedTimeToTimecode(double elapsedSeconds)
    {
        var totalFrames = (long)(elapsedSeconds * Fps);
        return DropFrame ? FramesToDropFrame(totalFrames) : FramesToNonDropFrame(totalFrames);
    }

    private string FramesToNonDropFrame(long totalFrames)
    {
        var framesPerSecond = (long)Fps;
        var framesPerMinute = framesPerSecond * 60;
        var framesPerHour = framesPerMinute * 60;

        var hours = totalFrames / framesPerHour;
        var remaining = totalFrames % framesPerHour;

        var minutes = remaining / framesPerMinute;
        remaining %= framesPerMinute;

        var seconds = remaining / framesPerSecond;
        var frames = remaining % framesPerSecond;

        return $"{hours:D2}:{minutes:D2}:{seconds:D2}:{frames:D2}";
    }

    private string FramesToDropFrame(long totalFrames)
    {
        var framesPerSecond = (long)System.Math.Round(Fps);
        var framesToDropPer10Minutes = Fps < 50 ? 2 : 4;

        var framesPer10Minutes = framesPerSecond * 60 * 10 - framesToDropPer10Minutes * 9;
        var framesPerMinute = framesPerSecond * 60 - framesToDropPer10Minutes;

        var blocks10Min = totalFrames / framesPer10Minutes;
        var remainingFrames = totalFrames % framesPer10Minutes;

        var hours = blocks10Min / 6;
        var remaining10MinBlocks = blocks10Min % 6;

        long minutesInBlock;
        long framesInMinute;
        if (remainingFrames < framesPerSecond * 60)
        {
            minutesInBlock = 0;
            framesInMinute = remainingFrames;
        }
        else
        {
            remainingFrames -= framesPerSecond * 60;
            minutesInBlock = 1 + remainingFrames / framesPerMinute;
            framesInMinute = remainingFrames % framesPerMinute;
        }

        var minutes = remaining10MinBlocks * 10 + minutesInBlock;
        var seconds = framesInMinute / framesPerSecond;
        var frames = framesInMinute % framesPerSecond;

        return $"{hours:D2}:{minutes:D2}:{seconds:D2}:{frames:D2}";
    }

    public IReadOnlyList<double> GetSupportedFps() => FrameRates.Keys.OrderBy(f => f).ToList();

    public bool IsDropFrame() => DropFrame;
}
