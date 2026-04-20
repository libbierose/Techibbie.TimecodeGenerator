"""
Detailed LTC encoding verification.
Checks that BCD fields, sync word, and audio waveform are all correct per SMPTE 12M.
"""

import sys
import os

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'src'))

import numpy as np
from audio.ltc_generator import LTCGenerator


def decode_bcd(bits, start, length):
    """Decode a BCD value from a slice of the LTC bit array."""
    value = 0
    for i in range(length):
        if start + i < len(bits):
            value += bits[start + i] * (2 ** i)
    return value


def test_timecodes():
    """Verify a set of known timecodes encode and round-trip correctly."""
    ltc_gen = LTCGenerator(48000)
    fps = 30.0

    test_cases = [
        (0,  0,  0,  0,  "00:00:00:00"),
        (0,  0,  0,  5,  "00:00:00:05"),
        (0,  0,  1,  0,  "00:00:01:00"),
        (0,  1,  0,  0,  "00:01:00:00"),
        (1,  0,  0,  0,  "01:00:00:00"),
        (12, 34, 56, 15, "12:34:56:15"),
    ]

    print("LTC Encoding Verification")
    print("=" * 80)

    all_pass = True
    for hours, minutes, seconds, frames, expected_tc in test_cases:
        ltc_bits = ltc_gen.timecode_to_ltc_frame(hours, minutes, seconds, frames, fps, False)
        bit_string = ''.join(str(b) for b in ltc_bits)

        decoded_frames  = decode_bcd(ltc_bits, 0,  4) + decode_bcd(ltc_bits, 4,  3) * 10
        decoded_secs    = decode_bcd(ltc_bits, 10, 4) + decode_bcd(ltc_bits, 14, 3) * 10
        decoded_mins    = decode_bcd(ltc_bits, 18, 4) + decode_bcd(ltc_bits, 22, 3) * 10
        decoded_hours   = decode_bcd(ltc_bits, 26, 4) + decode_bcd(ltc_bits, 30, 2) * 10

        decoded_tc = f"{decoded_hours:02d}:{decoded_mins:02d}:{decoded_secs:02d}:{decoded_frames:02d}"
        match = decoded_tc == expected_tc
        if not match:
            all_pass = False
        status = "PASS" if match else "FAIL"
        print(f"  {expected_tc}  ->  {decoded_tc}  {status}")

        if not match:
            print(f"    Expected: H={hours} M={minutes} S={seconds} F={frames}")
            print(f"    Got:      H={decoded_hours} M={decoded_mins} S={decoded_secs} F={decoded_frames}")
            print(f"    Bits 0-9: {bit_string[0:10]}")

    print()

    # Sync word check
    ltc_bits = ltc_gen.timecode_to_ltc_frame(0, 0, 0, 0, fps, False)
    bit_string = ''.join(str(b) for b in ltc_bits)
    sync_word     = bit_string[64:80]
    expected_sync = "0011111111111101"
    sync_ok = sync_word == expected_sync
    if not sync_ok:
        all_pass = False
    print("Sync Word Check:")
    print(f"  Generated: {sync_word}")
    print(f"  Expected:  {expected_sync}")
    print(f"  {'OK' if sync_ok else 'MISMATCH'}")
    print()

    # Audio waveform sanity check
    audio = ltc_gen.bits_to_audio(ltc_bits, fps)
    print("Audio Waveform Check:")
    print(f"  Samples: {len(audio)}  ({len(audio) / 48000 * 1000:.2f} ms at 48 kHz)")
    print(f"  Amplitude: {audio.min():.3f} to {audio.max():.3f}")

    print()
    print("=" * 80)
    print("Result:", "ALL PASS" if all_pass else "FAILURES DETECTED")


if __name__ == "__main__":
    test_timecodes()
