using System;

namespace Techibbie.TimecodeGenerator.Core.Ltc;

/// <summary>
/// Turns the frame-at-a-time <see cref="LtcStreamEncoder"/> into a gapless stream that can be
/// pulled in blocks of any size — which is what an audio callback needs, since hardware
/// buffer sizes bear no relation to LTC frame lengths.
/// </summary>
public sealed class LtcSampleSource
{
    private readonly LtcStreamEncoder _encoder;
    private float[] _buffer = Array.Empty<float>();
    private int _position;

    public LtcSampleSource(LtcStreamEncoder encoder)
    {
        _encoder = encoder;
    }

    public void Fill(Span<float> destination)
    {
        var written = 0;
        while (written < destination.Length)
        {
            if (_position >= _buffer.Length)
            {
                _buffer = _encoder.NextFrame();
                _position = 0;
            }

            var take = Math.Min(destination.Length - written, _buffer.Length - _position);
            _buffer.AsSpan(_position, take).CopyTo(destination.Slice(written, take));
            _position += take;
            written += take;
        }
    }
}
