using System;
using System.IO;
using Techibbie.TimecodeGenerator.Core.Wav;
using Xunit;

namespace Techibbie.TimecodeGenerator.Core.Tests;

public class WavWriterTests
{
    [Fact]
    public void WriteMono16_ProducesValidRiffHeaderAndSampleCount()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wavwriter_test_{Guid.NewGuid():N}.wav");
        try
        {
            var samples = new float[] { 0f, 1f, -1f, 0.5f, -0.5f };
            WavWriter.WriteMono16(path, samples, 48000);

            var bytes = File.ReadAllBytes(path);

            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
            Assert.Equal("fmt ", System.Text.Encoding.ASCII.GetString(bytes, 12, 4));
            Assert.Equal("data", System.Text.Encoding.ASCII.GetString(bytes, 36, 4));

            var dataSize = BitConverter.ToInt32(bytes, 40);
            Assert.Equal(samples.Length * 2, dataSize);
            Assert.Equal(44 + dataSize, bytes.Length);

            var channels = BitConverter.ToInt16(bytes, 22);
            var sampleRate = BitConverter.ToInt32(bytes, 24);
            var bitsPerSample = BitConverter.ToInt16(bytes, 34);
            Assert.Equal(1, channels);
            Assert.Equal(48000, sampleRate);
            Assert.Equal(16, bitsPerSample);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteMono16_ClampsOutOfRangeSamples()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wavwriter_test_{Guid.NewGuid():N}.wav");
        try
        {
            WavWriter.WriteMono16(path, new float[] { 2.0f, -2.0f }, 48000);

            var bytes = File.ReadAllBytes(path);
            var first = BitConverter.ToInt16(bytes, 44);
            var second = BitConverter.ToInt16(bytes, 46);

            Assert.Equal(32767, first);
            Assert.Equal(-32767, second);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
