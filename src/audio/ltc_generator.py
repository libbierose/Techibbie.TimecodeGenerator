"""
Linear Timecode (LTC) audio generator.
Generates standard LTC audio that can be read by professional video software like DaVinci Resolve.
"""

import numpy as np
from typing import Tuple


class LTCGenerator:
    """Generates Linear Timecode (LTC) audio signal using SMPTE 12M standard."""
    
    def __init__(self, sample_rate: int = 48000):
        """
        Initialize LTC generator.
        
        Args:
            sample_rate: Audio sample rate in Hz (default 48000 Hz)
        """
        self.sample_rate = sample_rate
    
    def timecode_to_ltc_frame(self, hours: int, minutes: int, seconds: int, 
                              frames: int, fps: float, drop_frame: bool = False) -> np.ndarray:
        """
        Convert timecode to LTC frame (80 bits) per SMPTE 12M standard.
        
        SMPTE 12M bit layout:
          Bits 0-3:   Frame units (BCD, LSB first)
          Bits 4-7:   User bits group 1 (0)
          Bits 8-9:   Frame tens (BCD, LSB first)
          Bit  10:    Drop frame flag
          Bit  11:    Color frame flag (0)
          Bits 12-15: User bits group 2 (0)
          Bits 16-19: Seconds units (BCD, LSB first)
          Bits 20-23: User bits group 3 (0)
          Bits 24-26: Seconds tens (BCD, LSB first)
          Bit  27:    Biphase Mark Phase Correction (parity)
          Bits 28-31: User bits group 4 (0)
          Bits 32-35: Minutes units (BCD, LSB first)
          Bits 36-39: User bits group 5 (0)
          Bits 40-42: Minutes tens (BCD, LSB first)
          Bit  43:    Binary group flag 0 (0)
          Bits 44-47: User bits group 6 (0)
          Bits 48-51: Hours units (BCD, LSB first)
          Bits 52-55: User bits group 7 (0)
          Bits 56-57: Hours tens (BCD, LSB first)
          Bit  58:    Binary group flag 1 (0)
          Bit  59:    Reserved (0)
          Bits 60-63: User bits group 8 (0)
          Bits 64-79: Sync word = 0011111111111111
        
        Args:
            hours: Hours (0-23)
            minutes: Minutes (0-59)
            seconds: Seconds (0-59)
            frames: Frame number (0-fps-1)
            fps: Frames per second
            drop_frame: Whether to use drop frame timecode
            
        Returns:
            Numpy array of 80 bits (0s and 1s)
        """
        bits = np.zeros(80, dtype=np.uint8)
        
        # Frame units (bits 0-3)
        frame_units = frames % 10
        for i in range(4):
            bits[i] = (frame_units >> i) & 1
        
        # User bits group 1 (bits 4-7) = 0 (already zero)
        
        # Frame tens (bits 8-9)
        frame_tens = frames // 10
        for i in range(2):
            bits[8 + i] = (frame_tens >> i) & 1
        
        # Bit 10: Drop frame flag
        bits[10] = 1 if drop_frame else 0
        # Bit 11: Color frame flag = 0
        
        # User bits group 2 (bits 12-15) = 0
        
        # Seconds units (bits 16-19)
        sec_units = seconds % 10
        for i in range(4):
            bits[16 + i] = (sec_units >> i) & 1
        
        # User bits group 3 (bits 20-23) = 0
        
        # Seconds tens (bits 24-26)
        sec_tens = seconds // 10
        for i in range(3):
            bits[24 + i] = (sec_tens >> i) & 1
        
        # Bit 27: BMPPC — calculated after all other bits are set
        
        # User bits group 4 (bits 28-31) = 0
        
        # Minutes units (bits 32-35)
        min_units = minutes % 10
        for i in range(4):
            bits[32 + i] = (min_units >> i) & 1
        
        # User bits group 5 (bits 36-39) = 0
        
        # Minutes tens (bits 40-42)
        min_tens = minutes // 10
        for i in range(3):
            bits[40 + i] = (min_tens >> i) & 1
        
        # Bit 43: Binary group flag 0 = 0
        
        # User bits group 6 (bits 44-47) = 0
        
        # Hours units (bits 48-51)
        hour_units = hours % 10
        for i in range(4):
            bits[48 + i] = (hour_units >> i) & 1
        
        # User bits group 7 (bits 52-55) = 0
        
        # Hours tens (bits 56-57)
        hour_tens = hours // 10
        for i in range(2):
            bits[56 + i] = (hour_tens >> i) & 1
        
        # Bit 58: Binary group flag 1 = 0
        # Bit 59: Reserved = 0
        # User bits group 8 (bits 60-63) = 0
        
        # Sync word (bits 64-79): SMPTE 12M fixed pattern 0011111111111101
        sync = [0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1]
        for i, b in enumerate(sync):
            bits[64 + i] = b
        
        # Bit 27: BMPPC — set so total number of 1-bits in frame is even
        bits[27] = 0
        if np.sum(bits) % 2 != 0:
            bits[27] = 1
        
        return bits

    def bits_to_audio(self, bits: np.ndarray, fps: float) -> np.ndarray:
        """
        Convert LTC bit stream to audio waveform using Biphase Mark Code (BMC).

        BMC rules (SMPTE 12M):
          - There is ALWAYS a transition at the START of every bit cell.
          - A '1' bit has an ADDITIONAL transition at the MIDPOINT.
          - A '0' bit has NO additional transition.

        The bit rate is fps * 80 bits/second, so the duration of one complete
        LTC frame is exactly 1/fps seconds regardless of sample rate.

        Args:
            bits: Array of 80 bits
            fps:  Frames per second (determines bit timing)

        Returns:
            Audio waveform as float32 numpy array
        """
        samples_per_frame = self.sample_rate / fps
        total_samples = round(samples_per_frame)
        audio = np.empty(total_samples, dtype=np.float32)

        level = 1.0

        for i, bit in enumerate(bits):
            # Exact sample boundaries for this bit
            n_start = round(i * samples_per_frame / 80)
            n_end   = round((i + 1) * samples_per_frame / 80)
            n_end   = min(n_end, total_samples)  # clamp last bit

            # Transition at start of every bit cell
            level = -level

            if bit == 0:
                audio[n_start:n_end] = level
            else:
                # Additional transition at the midpoint
                n_mid = (n_start + n_end) // 2
                audio[n_start:n_mid] = level
                level = -level
                audio[n_mid:n_end] = level

        return audio * 0.9

    def generate_ltc_audio(self, hours: int, minutes: int, seconds: int,
                           frames: int, fps: float, drop_frame: bool = False) -> np.ndarray:
        """Generate one LTC frame of audio for the given timecode."""
        ltc_bits = self.timecode_to_ltc_frame(hours, minutes, seconds, frames, fps, drop_frame)
        return self.bits_to_audio(ltc_bits, fps)

    def generate_continuous_ltc(self, hours: int, minutes: int, seconds: int,
                                frames: int, fps: float, drop_frame: bool = False,
                                duration_seconds: float = 1.0) -> np.ndarray:
        """
        Generate continuous LTC audio spanning multiple consecutive frames.

        Each frame is generated individually and the chunks are concatenated
        so the resulting waveform can be written directly to a WAV file or
        streamed as a reference track.

        Args:
            hours: Starting hours (0-23)
            minutes: Starting minutes (0-59)
            seconds: Starting seconds (0-59)
            frames: Starting frame number
            fps: Frames per second
            drop_frame: Whether to use drop-frame timecode
            duration_seconds: How many seconds of LTC to generate

        Returns:
            Float32 numpy array containing the full LTC waveform
        """
        frames_per_second = round(fps)
        frames_to_generate = int(duration_seconds * fps)

        current_frame  = frames
        current_second = seconds
        current_minute = minutes
        current_hour   = hours

        chunks = []
        for _ in range(frames_to_generate):
            chunks.append(self.generate_ltc_audio(
                current_hour, current_minute, current_second,
                current_frame, fps, drop_frame
            ))

            current_frame += 1
            if current_frame >= frames_per_second:
                current_frame = 0
                current_second += 1
                if current_second >= 60:
                    current_second = 0
                    current_minute += 1
                    if current_minute >= 60:
                        current_minute = 0
                        current_hour += 1
                        if current_hour >= 24:
                            current_hour = 0

        return np.concatenate(chunks) if chunks else np.array([], dtype=np.float32)
