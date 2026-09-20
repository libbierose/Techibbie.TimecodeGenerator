using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
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
/// Timing: quarter-frame messages are scheduled against absolute Stopwatch deadlines (not a
/// fixed integer-millisecond timer period, which would run ~4% fast at 30 fps), and any that
/// are overdue are sent immediately, so the long-run rate is exact even if a sleep overshoots.
/// It's still OS-scheduler-bound rather than sample-accurate like the LTC audio path.
/// </summary>
public sealed class MtcGenerator : IDisposable
{
    private OutputDevice? _device;
    private Thread? _thread;
    private CancellationTokenSource? _cts;
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

        var device = OutputDevice.GetByName(deviceName);
        var state = new MtcFrameState(hour, minute, second, frame, fps, dropFrame);
        lock (_lock)
        {
            _device = device;
            _state = state;
            _pieceIndex = 0;
        }

        var cts = new CancellationTokenSource();
        var quarterFrameTicks = Stopwatch.Frequency / (state.ExactFps * 4.0);
        _cts = cts;
        _thread = new Thread(() => Run(cts.Token, quarterFrameTicks))
        {
            IsBackground = true,
            Name = "MTC quarter-frame sender",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
    }

    private void Run(CancellationToken token, double quarterFrameTicks)
    {
        // Windows' default ~15.6 ms timer granularity is far coarser than the ~8 ms quarter-frame spacing.
        var raisedResolution = OperatingSystem.IsWindows() && NativeMethods.timeBeginPeriod(1) == 0;
        try
        {
            var clock = Stopwatch.StartNew();
            long sent = 0;
            while (!token.IsCancellationRequested)
            {
                var due = (long)(sent * quarterFrameTicks);
                var remaining = due - clock.ElapsedTicks;
                if (remaining <= 0)
                {
                    SendNextPiece();
                    sent++;
                }
                else if (remaining * 1000.0 / Stopwatch.Frequency > 2.0)
                {
                    Thread.Sleep(1);
                }
                else
                {
                    Thread.SpinWait(40);
                }
            }
        }
        finally
        {
            if (raisedResolution) NativeMethods.timeEndPeriod(1);
        }
    }

    public void Stop()
    {
        var thread = _thread;
        _cts?.Cancel();
        if (thread is not null && thread != Thread.CurrentThread) thread.Join(500);
        _thread = null;
        _cts?.Dispose();
        _cts = null;

        lock (_lock)
        {
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

    private static class NativeMethods
    {
        [DllImport("winmm.dll")]
        public static extern uint timeBeginPeriod(uint milliseconds);

        [DllImport("winmm.dll")]
        public static extern uint timeEndPeriod(uint milliseconds);
    }
}
