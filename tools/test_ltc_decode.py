"""
Round-trip test: encode timecode to LTC bits, then decode and verify the values match.
Tests 24 fps and 30 fps NDF progressions.
"""

import sys
import os

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'src'))

from audio.ltc_generator import LTCGenerator


def decode_ltc_frame(bits):
    """Decode an LTC frame's BCD fields back to (hours, minutes, seconds, frames)."""
    def get_bcd(start, count):
        value = 0
        for i in range(count):
            value += bits[start + i] * (2 ** i)
        return value

    frames  = get_bcd(0, 4)  + get_bcd(4, 3)  * 10
    seconds = get_bcd(10, 4) + get_bcd(14, 3) * 10
    minutes = get_bcd(18, 4) + get_bcd(22, 3) * 10
    hours   = get_bcd(26, 4) + get_bcd(30, 2) * 10

    return hours, minutes, seconds, frames


def test_multiple_frames():
    """Generate and decode multiple consecutive frames at 24 and 30 fps."""
    ltc_gen = LTCGenerator(48000)

    for fps, label in [(24.0, "24fps"), (30.0, "30fps NDF")]:
        print(f"Testing {label} timecode progression:")
        print("=" * 80)
        count = 30
        for frame_num in range(count):
            seconds     = frame_num // int(fps)
            frame_in_sec = frame_num % int(fps)

            ltc_bits = ltc_gen.timecode_to_ltc_frame(0, 0, seconds, frame_in_sec, fps, False)
            h, m, s, f = decode_ltc_frame(ltc_bits)

            expected = f"00:00:{seconds:02d}:{frame_in_sec:02d}"
            decoded  = f"{h:02d}:{m:02d}:{s:02d}:{f:02d}"
            status   = "✓" if decoded == expected else "✗"
            print(f"  Frame {frame_num:2d}  Expected: {expected}  Got: {decoded}  {status}")
        print()


if __name__ == "__main__":
    test_multiple_frames()
