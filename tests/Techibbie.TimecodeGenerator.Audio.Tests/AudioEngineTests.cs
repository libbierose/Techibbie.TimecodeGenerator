using System;
using System.Linq;
using Techibbie.TimecodeGenerator.Audio;
using Xunit;

namespace Techibbie.TimecodeGenerator.Audio.Tests;

public class AudioEngineTests
{
    // AudioEngine.GenerateClickTone and GetOutputDevices only need PortAudio's host API to
    // initialize — they don't need a real playback device, so they're safe to run in CI.
    // Actually opening/streaming to a device (StartLtcStream, PlayFrameClick) needs real
    // audio hardware and isn't covered here for that reason.

    [Fact]
    public void GenerateClickTone_ProducesRequestedDuration()
    {
        var engine = new AudioEngine();
        var click = engine.GenerateClickTone(1000, 50);

        // 48000 Hz (the fixed click sample rate) * 50ms / 1000 = 2400 samples.
        Assert.Equal(2400, click.Length);
    }

    [Fact]
    public void GenerateClickTone_StaysWithinAmplitudeBounds()
    {
        var engine = new AudioEngine();
        var click = engine.GenerateClickTone(1000, 50);

        // Sine amplitude 0.3 times a Hann envelope (<=1) — never exceeds 0.3 in magnitude.
        Assert.All(click, sample => Assert.InRange(sample, -0.3f, 0.3f));
    }

    [Fact]
    public void GenerateClickTone_EnvelopeFadesInAndOutAtTheEdges()
    {
        var engine = new AudioEngine();
        var click = engine.GenerateClickTone(1000, 50);

        // A Hann envelope means the very first/last samples are at (or essentially at) zero,
        // while samples in the middle of the click reach much higher amplitude.
        var edgeMagnitude = Math.Abs(click[0]);
        var maxMagnitude = click.Max(s => Math.Abs(s));

        Assert.True(edgeMagnitude < maxMagnitude * 0.05,
            $"Expected the envelope to fade near zero at the start; got {edgeMagnitude} vs peak {maxMagnitude}.");
    }

    [Fact]
    public void GenerateClickTone_HigherFrequencyProducesMoreZeroCrossingsThanLower()
    {
        var engine = new AudioEngine();
        var lowClick = engine.GenerateClickTone(400, 50);
        var highClick = engine.GenerateClickTone(2000, 50);

        static int CountSignChanges(float[] samples)
        {
            var count = 0;
            for (var i = 1; i < samples.Length; i++)
            {
                if (Math.Sign(samples[i - 1]) != 0 && Math.Sign(samples[i]) != 0 && Math.Sign(samples[i - 1]) != Math.Sign(samples[i]))
                {
                    count++;
                }
            }
            return count;
        }

        Assert.True(CountSignChanges(highClick) > CountSignChanges(lowClick),
            "A higher-frequency tone should cross zero more often than a lower-frequency one of the same duration.");
    }

    [Fact]
    public void GetOutputDevices_DoesNotThrowAndReturnsOnlyOutputCapableDevices()
    {
        var engine = new AudioEngine();

        // Smoke test only: CI machines may have zero, one, or many audio devices, and we
        // can't assert a specific device exists. What we can assert is that enumeration
        // itself doesn't throw, and that anything returned genuinely has output channels
        // (AudioEngine.GetOutputDevices is documented to filter for that).
        var devices = engine.GetOutputDevices();

        Assert.All(devices, d => Assert.True(d.Channels > 0));
    }
}
