"""
LTC diagnostic: verify timecode generation produces correct bit patterns.
Generates a sample LTC frame and prints the bit layout for inspection.
"""

import sys
import os

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'src'))

import numpy as np
from audio.ltc_generator import LTCGenerator


def test_ltc_generation():
    """Test LTC generation for a known timecode."""

    ltc_gen = LTCGenerator(48000)

    hours, minutes, seconds, frames = 0, 0, 0, 0
    fps = 30.0
    drop_frame = False

    ltc_bits = ltc_gen.timecode_to_ltc_frame(hours, minutes, seconds, frames, fps, drop_frame)

    print("LTC Bit Frame for 00:00:00:00:")
    print("=" * 80)

    # Print bits in groups of 8
    bit_string = ''.join(str(b) for b in ltc_bits)
    for i in range(0, 80, 8):
        byte_group = bit_string[i:i+8]
        print(f"Bits {i:2d}-{i+7:2d}: {byte_group} ({int(byte_group, 2):3d})")

    print("\n" + "=" * 80)
    print("LTC Frame Structure Check:")
    print(f"Bits 0-3   (Frame units):        {bit_string[0:4]}")
    print(f"Bits 4-9   (Frame tens + flags): {bit_string[4:10]}")
    print(f"Bits 10-17 (Seconds):            {bit_string[10:18]}")
    print(f"Bits 18-25 (Minutes):            {bit_string[18:26]}")
    print(f"Bits 26-33 (Hours):              {bit_string[26:34]}")
    print(f"Bits 34-39 (User bits):          {bit_string[34:40]}")
    print(f"Bits 40-63 (Sync word pt1):      {bit_string[40:64]}")
    print(f"Bits 64-79 (Sync word pt2):      {bit_string[64:80]}")

    audio = ltc_gen.bits_to_audio(ltc_bits, fps)
    print("\n" + "=" * 80)
    print("LTC Audio Properties:")
    print(f"Sample rate:   {ltc_gen.sample_rate} Hz")
    print(f"Total samples: {len(audio)}")
    print(f"Duration:      {len(audio) / ltc_gen.sample_rate * 1000:.2f} ms")
    print(f"Amplitude:     {audio.min():.3f} to {audio.max():.3f}")

    print("\n" + "=" * 80)
    print("Testing frame increment (bits 0-9):")
    for frame in [0, 1, 5, 10, 29]:
        ltc_bits = ltc_gen.timecode_to_ltc_frame(0, 0, 0, frame, fps, drop_frame)
        bit_string = ''.join(str(b) for b in ltc_bits)
        print(f"Frame {frame:2d}: Bits 0-9: {bit_string[0:10]}")


if __name__ == "__main__":
    test_ltc_generation()
