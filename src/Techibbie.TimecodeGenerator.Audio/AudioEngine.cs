using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using PortAudioSharp;
using Techibbie.TimecodeGenerator.Core.Ltc;
using PaStream = PortAudioSharp.Stream;

namespace Techibbie.TimecodeGenerator.Audio;

/// <summary>
/// Manages audio device enumeration and timecode audio output: a gapless
/// callback-driven LTC stream (SMPTE 12M) and a queued click-tone fallback,
/// both backed by PortAudio via PortAudioSharp2.
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private const int ClickSampleRate = 48000;
    private const int ClickQueueMaxSize = 10;

    private static readonly object InitLock = new();
    private static bool _initialized;

    private readonly object _clickStreamLock = new();
    private readonly ConcurrentQueue<float[]> _clickQueue = new();

    private PaStream? _ltcStream;
    private LtcStreamState? _ltcState;

    private PaStream? _clickStream;
    private int? _clickStreamDeviceId;
    private float[]? _clickCurrent;
    private int _clickCurrentPos;

    private bool _disposed;

    public AudioEngine()
    {
        EnsureInitialized();
    }

    private static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (InitLock)
        {
            if (_initialized) return;
            PortAudio.LoadNativeLibrary();
            PortAudio.Initialize();
            _initialized = true;
        }
    }

    /// <summary>List every audio device that has at least one output channel.</summary>
    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        var devices = new List<AudioDeviceInfo>();
        for (var i = 0; i < PortAudio.DeviceCount; i++)
        {
            var info = PortAudio.GetDeviceInfo(i);
            if (info.maxOutputChannels > 0)
            {
                devices.Add(new AudioDeviceInfo(i, info.name, info.maxOutputChannels, info.defaultSampleRate));
            }
        }
        return devices;
    }

    private static DeviceInfo GetDeviceInfoOrDefault(int? deviceId)
    {
        try
        {
            var id = deviceId ?? PortAudio.DefaultOutputDevice;
            return PortAudio.GetDeviceInfo(id);
        }
        catch (PortAudioException)
        {
            return new DeviceInfo { maxOutputChannels = 2, defaultSampleRate = 48000 };
        }
    }

    // ── Gapless LTC stream ──────────────────────────────────────────────────

    /// <summary>
    /// Start a continuous, gapless LTC stream using a PortAudio callback.
    /// The callback fills each hardware buffer directly from pre-generated LTC
    /// frame data, advancing the timecode state as each frame is consumed, and
    /// generates at the device's native sample rate to avoid resampling that
    /// would corrupt LTC bit timing.
    /// </summary>
    public void StartLtcStream(int hours, int minutes, int seconds, int frames, double fps, bool dropFrame, int? deviceId)
    {
        StopLtcStream();

        var device = GetDeviceInfoOrDefault(deviceId);
        var channels = device.maxOutputChannels;
        var nativeSampleRate = device.defaultSampleRate;

        // The shared encoder handles drop-frame numbering, half-rate LTC for high frame
        // rates, and drift-free frame lengths — the same code WAV export uses.
        var state = new LtcStreamState(new LtcSampleSource(new LtcStreamEncoder(
            (int)Math.Round(nativeSampleRate), fps, dropFrame, hours, minutes, seconds, frames)));
        _ltcState = state;

        var outputParams = new StreamParameters
        {
            device = deviceId ?? PortAudio.DefaultOutputDevice,
            channelCount = channels,
            sampleFormat = SampleFormat.Float32,
            suggestedLatency = device.defaultLowOutputLatency,
        };

        _ltcStream = new PaStream(
            inParams: null,
            outParams: outputParams,
            sampleRate: nativeSampleRate,
            framesPerBuffer: PortAudio.FramesPerBufferUnspecified,
            streamFlags: StreamFlags.NoFlag,
            callback: delegate (IntPtr input, IntPtr output, uint frameCount, ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags statusFlags, IntPtr userData)
            {
                unsafe { LtcCallback((float*)output, (int)frameCount, channels, state); }
                return StreamCallbackResult.Continue;
            },
            userData: null);
        _ltcStream.Start();
    }

    private static unsafe void LtcCallback(float* output, int frameCount, int channels, LtcStreamState state)
    {
        if (state.Scratch.Length < frameCount) state.Scratch = new float[frameCount];
        var mono = state.Scratch.AsSpan(0, frameCount);
        state.Source.Fill(mono);

        for (var i = 0; i < frameCount; i++)
        {
            // LTC goes out on every channel so any physical output the user has patched receives it.
            for (var ch = 0; ch < channels; ch++)
            {
                output[i * channels + ch] = mono[i];
            }
        }
    }

    public void StopLtcStream()
    {
        if (_ltcStream == null) return;
        try
        {
            _ltcStream.Stop();
            _ltcStream.Close();
        }
        finally
        {
            _ltcStream.Dispose();
            _ltcStream = null;
            _ltcState = null;
        }
    }

    // ── Click-tone fallback (queued, non-gapless) ───────────────────────────

    /// <summary>Generate a short click tone: a sine wave with a Hann envelope to avoid pops.</summary>
    public float[] GenerateClickTone(double frequency = 1000, int durationMs = 50)
    {
        var numSamples = (int)(ClickSampleRate * durationMs / 1000.0);
        var wave = new float[numSamples];
        for (var i = 0; i < numSamples; i++)
        {
            var t = i / (double)ClickSampleRate;
            var value = Math.Sin(2 * Math.PI * frequency * t) * 0.3;
            var envelope = numSamples > 1 ? 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (numSamples - 1)) : 1.0;
            wave[i] = (float)(value * envelope);
        }
        return wave;
    }

    /// <summary>Queue a timecode click for playback at <paramref name="frequency"/> Hz.</summary>
    public void PlayTimecodeClick(int? deviceId, double frequency = 1000)
    {
        EnqueueClick(GenerateClickTone(frequency, 80), deviceId);
    }

    /// <summary>Play a click whose pitch marks whether this frame starts a new second.</summary>
    public void PlayFrameClick(int frameNumber, double fps, int? deviceId)
    {
        var framesPerSecond = (int)fps;
        var frequency = frameNumber % framesPerSecond == 0 ? 800.0 : 1200.0;
        PlayTimecodeClick(deviceId, frequency);
    }

    private void EnqueueClick(float[] mono, int? deviceId)
    {
        EnsureClickStream(deviceId);
        if (_clickQueue.Count >= ClickQueueMaxSize) return; // drop when full, same as the bounded Python queue
        _clickQueue.Enqueue(mono);
    }

    private void EnsureClickStream(int? deviceId)
    {
        lock (_clickStreamLock)
        {
            if (_clickStream != null && _clickStreamDeviceId == deviceId) return;

            _clickStream?.Stop();
            _clickStream?.Close();
            _clickStream?.Dispose();
            _clickCurrent = null;
            _clickCurrentPos = 0;

            var device = GetDeviceInfoOrDefault(deviceId);
            var channels = device.maxOutputChannels > 0 ? device.maxOutputChannels : 2;
            _clickStreamDeviceId = deviceId;

            var outputParams = new StreamParameters
            {
                device = deviceId ?? PortAudio.DefaultOutputDevice,
                channelCount = channels,
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = device.defaultLowOutputLatency,
            };

            _clickStream = new PaStream(
                inParams: null,
                outParams: outputParams,
                sampleRate: ClickSampleRate,
                framesPerBuffer: PortAudio.FramesPerBufferUnspecified,
                streamFlags: StreamFlags.NoFlag,
                callback: delegate (IntPtr input, IntPtr output, uint frameCount, ref StreamCallbackTimeInfo timeInfo, StreamCallbackFlags statusFlags, IntPtr userData)
                {
                    unsafe { ClickCallback((float*)output, (int)frameCount, channels); }
                    return StreamCallbackResult.Continue;
                },
                userData: null);
            _clickStream.Start();
        }
    }

    private unsafe void ClickCallback(float* output, int frameCount, int channels)
    {
        for (var i = 0; i < frameCount; i++)
        {
            if (_clickCurrent == null || _clickCurrentPos >= _clickCurrent.Length)
            {
                _clickCurrent = _clickQueue.TryDequeue(out var next) ? next : null;
                _clickCurrentPos = 0;
            }

            // Route mono click/LTC audio to channel 0 only, silence elsewhere — same
            // channel routing the Python worker thread used for queued playback
            // (distinct from the always-fan-out-to-every-channel LTC stream above).
            var sample = 0f;
            if (_clickCurrent != null)
            {
                sample = _clickCurrent[_clickCurrentPos];
                _clickCurrentPos++;
            }

            output[i * channels] = sample;
            for (var ch = 1; ch < channels; ch++)
            {
                output[i * channels + ch] = 0f;
            }
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public void Shutdown()
    {
        StopLtcStream();
        lock (_clickStreamLock)
        {
            _clickStream?.Stop();
            _clickStream?.Close();
            _clickStream?.Dispose();
            _clickStream = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Shutdown();
    }

    private sealed class LtcStreamState(LtcSampleSource source)
    {
        public LtcSampleSource Source { get; } = source;
        public float[] Scratch = Array.Empty<float>();
    }
}
