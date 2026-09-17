using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;

namespace Techibbie.TimecodeGenerator.Audio;

/// <summary>
/// Generates MIDI Time Code (MTC) quarter-frame messages on a MIDI output port — a second,
/// independent sync format alongside LTC, for DAWs/hardware that read MIDI rather than audio.
/// The actual protocol logic (BCD splitting, rate flags, frame advancing) lives in the
/// pure, directly-testable <see cref="MtcFrameState"/>; this class is just the MIDI I/O
/// plumbing around it.
///
/// Timing is a plain <see cref="Timer"/> firing 4x the frame rate (one quarter-frame message
/// per tick); this is standard practice for MTC generators, but it is timer-resolution-bound,
/// not sample-accurate like the LTC audio path — fine for DAW sync, not broadcast-grade.
/// </summary>
public sealed class MtcGenerator : IDisposable
{
    private OutputDevice? _device;
    private Timer? _timer;
    private readonly object _lock = new();

    private MtcFrameState? _state;
    private int _pieceIndex;

    public static IReadOnlyList<string> GetOutputDeviceNames()
    {
        try
        {
            return OutputDevice.GetAll().Select(d => d.Name).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void Start(string deviceName, int hour, int minute, int second, int frame, double fps, bool dropFrame)
    {
        Stop();

        _device = OutputDevice.GetByName(deviceName);
        _state = new MtcFrameState(hour, minute, second, frame, fps, dropFrame);
        _pieceIndex = 0;

        var quarterFrameMs = 1000.0 / (fps * 4.0);
        _timer = new Timer(_ => SendNextPiece(), null, 0, Math.Max(1, (int)Math.Round(quarterFrameMs)));
    }

    public void Stop()
    {
        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
            _device?.Dispose();
            _device = null;
            _state = null;
        }
    }

    private void SendNextPiece()
    {
        lock (_lock)
        {
            if (_device is null || _state is null) return;

            var (component, value) = _state.BuildPiece(_pieceIndex);
            try
            {
                _device.SendEvent(new MidiTimeCodeEvent(component, new FourBitNumber((byte)value)));
            }
            catch
            {
                // MIDI device may have been unplugged/closed mid-stream; drop this tick.
            }

            _pieceIndex++;
            if (_pieceIndex >= 8)
            {
                _pieceIndex = 0;
                // A full 8-piece cycle spans 2 frame periods (4 quarter-frames/frame), so the
                // frame counter advances by 2 once the cycle completes, per the MTC spec.
                _state.Advance(2);
            }
        }
    }

    public void Dispose() => Stop();
}
