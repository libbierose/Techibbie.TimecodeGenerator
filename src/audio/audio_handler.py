"""
Audio handler for managing audio devices and generating timecode audio output.
"""

import sounddevice as sd
import numpy as np
from typing import List, Tuple, Optional
import threading
import queue
import time
from .ltc_generator import LTCGenerator


class AudioHandler:
    """Handles audio device management and timecode audio generation."""
    
    def __init__(self, sample_rate: int = 48000):
        """
        Initialize audio handler.
        
        Args:
            sample_rate: Audio sample rate in Hz (default 48000 Hz)
        """
        self.sample_rate = sample_rate
        self.is_playing = False
        self.stream = None
        self.current_frame = 0
        self.audio_queue = queue.Queue(maxsize=10)
        self.audio_thread = None
        self.stop_audio_thread = False
        self.ltc_generator = LTCGenerator(sample_rate)
        self.ltc_mode = False  # False = click tones, True = LTC
        self._start_audio_thread()
        
    def get_audio_devices(self) -> List[dict]:
        """
        Get list of available audio output devices.
        
        Returns:
            List of dicts with device info {id, name, channels}
        """
        devices = []
        for i, device in enumerate(sd.query_devices()):
            if device['max_output_channels'] > 0:  # Only output devices
                devices.append({
                    'id': i,
                    'name': device['name'],
                    'channels': device['max_output_channels'],
                    'sample_rate': device['default_samplerate']
                })
        return devices
    
    def _get_device_info(self, device_id: Optional[int] = None) -> dict:
        """Get device info dict for the given device id (or default if None)."""
        try:
            if device_id is None:
                return sd.query_devices(sd.default.device[1])
            return sd.query_devices(device_id)
        except Exception:
            return {'max_output_channels': 2, 'default_samplerate': 48000}

    def _get_device_channel_count(self, device_id: Optional[int] = None) -> int:
        """Get the number of output channels for a device."""
        return int(self._get_device_info(device_id)['max_output_channels'])

    def generate_click_tone(self, frequency: float = 1000, duration_ms: int = 50) -> np.ndarray:
        """
        Generate a click tone audio signal.
        
        Args:
            frequency: Frequency in Hz
            duration_ms: Duration in milliseconds
            
        Returns:
            Numpy array of audio samples
        """
        num_samples = int(self.sample_rate * duration_ms / 1000)
        t = np.linspace(0, duration_ms / 1000, num_samples, False)
        
        # Generate sine wave
        wave = np.sin(2 * np.pi * frequency * t) * 0.3
        
        # Apply envelope (fade in/out) to avoid clicks
        envelope = np.hanning(num_samples)
        wave = wave * envelope
        
        return wave.astype(np.float32)
    
    def generate_timecode_audio(self, timecode_str: str, fps: float) -> np.ndarray:
        """
        Generate audio representation of timecode.
        Uses clicks: 1 click for ones place, 2 for tens place, 3 for hundreds place.
        
        Args:
            timecode_str: Timecode string (HH:MM:SS:FF)
            fps: Frames per second
            
        Returns:
            Numpy array of audio samples
        """
        # Parse timecode
        parts = timecode_str.split(':')
        hours, minutes, seconds, frames = map(int, parts)
        
        # Create silence buffer
        audio = np.array([], dtype=np.float32)
        
        # Generate audio cues for each timecode component
        # Frame cue (single click for reference)
        if frames == 0:  # Only at frame 0
            audio = np.concatenate([audio, self.generate_click_tone(1000, 50)])
            audio = np.concatenate([audio, np.zeros(int(0.1 * self.sample_rate), dtype=np.float32)])
        
        return audio
    
    def play_timecode_click(self, device_id: Optional[int] = None, frequency: float = 1000):
        """
        Queue a timecode click for playback.
        
        Args:
            device_id: Audio device ID (None for default)
            frequency: Click frequency in Hz
        """
        try:
            click = self.generate_click_tone(frequency, 80)
            self.audio_queue.put((click, device_id), timeout=0.1)
        except queue.Full:
            pass  # Skip if queue is full
        except Exception as e:
            print(f"Error queueing audio: {e}")
    
    def play_ltc_timecode(self, hours: int, minutes: int, seconds: int, frames: int,
                         fps: float, drop_frame: bool = False, device_id: Optional[int] = None):
        """
        Queue LTC audio for the given timecode.
        
        Args:
            hours: Hours (0-23)
            minutes: Minutes (0-59)
            seconds: Seconds (0-59)
            frames: Frame number
            fps: Frames per second
            drop_frame: Whether to use drop frame timecode
            device_id: Audio device ID
        """
        try:
            ltc_audio = self.ltc_generator.generate_ltc_audio(
                hours, minutes, seconds, frames, fps, drop_frame
            )
            self.audio_queue.put((ltc_audio, device_id), timeout=0.1)
        except queue.Full:
            pass  # Skip if queue is full
        except Exception as e:
            print(f"Error queueing LTC audio: {e}")
    
    def play_frame_click(self, frame_number: int, fps: float, device_id: Optional[int] = None):
        """
        Play different click patterns based on frame position.
        Supports various frame rate click patterns.
        
        Args:
            frame_number: Current frame number
            fps: Frames per second
            device_id: Audio device ID
        """
        frames_per_second = int(fps)
        
        # Determine click frequency based on frame position
        if frame_number % frames_per_second == 0:
            # Start of second: low freq click
            frequency = 800
        else:
            # Regular frame: higher freq click
            frequency = 1200
        
        self.play_timecode_click(device_id, frequency)
    
    def set_ltc_mode(self, enabled: bool):
        """
        Enable or disable LTC mode.
        
        Args:
            enabled: True for LTC mode, False for click tones
        """
        self.ltc_mode = enabled

    def start_ltc_stream(self, hours: int, minutes: int, seconds: int,
                         frames: int, fps: float, drop_frame: bool = False,
                         device_id: Optional[int] = None):
        """
        Start a continuous, gapless LTC stream using an audio callback.

        The PortAudio callback fills each hardware buffer directly from
        pre-generated LTC frame data, advancing the timecode state as each
        frame is consumed.  This guarantees a perfectly gapless bitstream
        that LTC decoders (including DaVinci Resolve) can lock onto.
        """
        self.stop_ltc_stream()

        self._ltc_fps     = fps
        self._ltc_drop    = drop_frame
        self._ltc_frame   = frames
        self._ltc_second  = seconds
        self._ltc_minute  = minutes
        self._ltc_hour    = hours
        self._ltc_buf     = np.array([], dtype=np.float32)
        self._ltc_buf_pos = 0

        device_info = self._get_device_info(device_id)
        channels    = int(device_info['max_output_channels'])
        # Use the device's NATIVE sample rate to avoid WASAPI resampling,
        # which would corrupt LTC bit timing. Create a fresh LTC generator
        # tuned to this rate.
        native_sr = int(device_info['default_samplerate'])
        ltc_gen = LTCGenerator(native_sr)

        def _advance():
            self._ltc_frame += 1
            if self._ltc_frame >= round(self._ltc_fps):
                self._ltc_frame = 0
                self._ltc_second += 1
                if self._ltc_second >= 60:
                    self._ltc_second = 0
                    self._ltc_minute += 1
                    if self._ltc_minute >= 60:
                        self._ltc_minute = 0
                        self._ltc_hour += 1
                        if self._ltc_hour >= 24:
                            self._ltc_hour = 0

        def _callback(outdata, num_frames, time_info, status):
            mono = np.empty(num_frames, dtype=np.float32)
            out_pos = 0
            while out_pos < num_frames:
                if self._ltc_buf_pos >= len(self._ltc_buf):
                    self._ltc_buf = ltc_gen.generate_ltc_audio(
                        self._ltc_hour, self._ltc_minute, self._ltc_second,
                        self._ltc_frame, self._ltc_fps, self._ltc_drop
                    )
                    self._ltc_buf_pos = 0
                    _advance()
                take = min(num_frames - out_pos, len(self._ltc_buf) - self._ltc_buf_pos)
                mono[out_pos:out_pos + take] = (
                    self._ltc_buf[self._ltc_buf_pos:self._ltc_buf_pos + take]
                )
                self._ltc_buf_pos += take
                out_pos += take

            outdata.fill(0.0)
            # Put LTC on ALL channels so every output (including camera inputs) receives it
            for ch in range(outdata.shape[1]):
                outdata[:, ch] = mono

        self._ltc_callback_stream = sd.OutputStream(
            device=device_id,
            samplerate=native_sr,
            channels=channels,
            dtype='float32',
            callback=_callback,
            blocksize=0,  # let PortAudio choose optimal block size
        )
        self._ltc_callback_stream.start()

    def stop_ltc_stream(self):
        """Stop and close the callback-based LTC stream."""
        stream = getattr(self, '_ltc_callback_stream', None)
        if stream is not None:
            try:
                stream.stop()
                stream.close()
            except Exception as e:
                print(f"Error stopping LTC stream: {e}")
            self._ltc_callback_stream = None
    
    def _start_audio_thread(self):
        """Start the background audio playback thread."""
        self.stop_audio_thread = False
        self.audio_thread = threading.Thread(target=self._audio_worker, daemon=True)
        self.audio_thread.start()
    
    def _audio_worker(self):
        """
        Background worker thread for audio playback.

        Pulls (audio, device_id) tuples off the queue and writes them to a
        sounddevice OutputStream.  The stream is recreated whenever the target
        device changes so we never block the GUI thread with device I/O.
        """
        current_device = None
        current_channels = None
        
        while not self.stop_audio_thread:
            try:
                # Get audio from queue with timeout
                audio, device_id = self.audio_queue.get(timeout=0.1)
                
                # Recreate the stream if the requested output device has changed
                if current_device != device_id:
                    if self.stream:
                        self.stream.close()
                    
                    # Get actual channel count the device supports
                    channels = self._get_device_channel_count(device_id)
                    
                    # Create stream with the device's native channel count
                    self.stream = sd.OutputStream(
                        device=device_id,
                        samplerate=self.sample_rate,
                        channels=channels
                    )
                    self.stream.start()
                    current_device = device_id
                    current_channels = channels
                
                # Route mono LTC audio to all available output channels so the
                # signal reaches any physical output the user has patched.
                if self.stream and current_channels:
                    if audio.ndim == 1:  # Mono audio
                        if current_channels == 1:
                            # Device is mono — write directly
                            self.stream.write(audio)
                        elif current_channels == 2:
                            # Stereo device — LTC on left, silence on right so we
                            # don't accidentally blast tone into a mix bus
                            stereo_audio = np.zeros((len(audio), 2), dtype=np.float32)
                            stereo_audio[:, 0] = audio  # Left channel = LTC
                            stereo_audio[:, 1] = 0      # Right channel = silence
                            self.stream.write(stereo_audio)
                        else:
                            # Multi-channel device (e.g. audio interface) — LTC on
                            # channel 0, silence on the rest
                            multi_channel = np.zeros((len(audio), current_channels), dtype=np.float32)
                            multi_channel[:, 0] = audio
                            self.stream.write(multi_channel)
                    else:
                        # Audio is already shaped for multi-channel output
                        self.stream.write(audio)
                    
            except queue.Empty:
                continue
            except Exception as e:
                print(f"Audio playback error: {e}")
    
    def stop_playback(self):
        """Stop audio playback."""
        try:
            if self.stream:
                self.stream.close()
                self.stream = None
        except Exception as e:
            print(f"Error stopping audio: {e}")
    
    def shutdown(self):
        """Shutdown audio handler completely."""
        try:
            self.stop_ltc_stream()
            self.stop_audio_thread = True
            if self.stream:
                self.stream.close()
            if self.audio_thread:
                self.audio_thread.join(timeout=1)
        except Exception as e:
            print(f"Error shutting down audio: {e}")
