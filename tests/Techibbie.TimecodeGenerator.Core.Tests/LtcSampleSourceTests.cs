using System;
using System.Collections.Generic;
using System.Linq;
using Techibbie.TimecodeGenerator.Core.Ltc;
using Xunit;

namespace Techibbie.TimecodeGenerator.Core.Tests;

public class LtcSampleSourceTests
{
    private static float[] PullInBlocks(LtcSampleSource source, int totalSamples, IReadOnlyList<int> blockSizes)
    {
        var result = new float[totalSamples];
        var pos = 0;
        var i = 0;
        while (pos < totalSamples)
        {
            var size = Math.Min(blockSizes[i++ % blockSizes.Count], totalSamples - pos);
            source.Fill(result.AsSpan(pos, size));
            pos += size;
        }
        return result;
    }

    [Theory]
    [InlineData(new[] { 1 })]
    [InlineData(new[] { 7, 13, 1024 })]
    [InlineData(new[] { 480 })]
    [InlineData(new[] { 441, 4096, 3, 512 })]
    public void AnyBlockSizeProducesTheSameWaveformAsContiguousEncoding(int[] blockSizes)
    {
        const int rate = 44100;
        const int total = rate * 3;

        var reference = PullInBlocks(
            new LtcSampleSource(new LtcStreamEncoder(rate, 29.97, true, 0, 0, 58, 0)), total, new[] { total });
        var chunked = PullInBlocks(
            new LtcSampleSource(new LtcStreamEncoder(rate, 29.97, true, 0, 0, 58, 0)), total, blockSizes);

        Assert.True(reference.SequenceEqual(chunked), "Block boundaries changed the waveform.");
    }

    [Fact]
    public void StitchedBlocksStillDecodeAsCleanConsecutiveLtc()
    {
        const int rate = 48000;
        var source = new LtcSampleSource(new LtcStreamEncoder(rate, 25.0, false, 1, 0, 0, 0));
        var audio = PullInBlocks(source, rate * 5, new[] { 256, 480, 1000, 333 });

        var decoded = LtcReferenceDecoder.Decode(audio, rate, 25.0);

        Assert.True(decoded.Count > 100);
        for (var i = 1; i < decoded.Count; i++)
        {
            var prev = LtcReferenceDecoder.FrameNumberFromTimecode(decoded[i - 1].Hour, decoded[i - 1].Minute, decoded[i - 1].Second, decoded[i - 1].Frame, 25, false);
            var cur = LtcReferenceDecoder.FrameNumberFromTimecode(decoded[i].Hour, decoded[i].Minute, decoded[i].Second, decoded[i].Frame, 25, false);
            Assert.Equal(prev + 1, cur);
        }
    }
}
