"""
SMPTE 12M LTC frame structure analyzer.
Generates a reference LTC frame and prints a human-readable breakdown of every
field — useful for verifying compliance with the SMPTE 12M standard.
"""

import sys
import os

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'src'))

from audio.ltc_generator import LTCGenerator


def analyze_ltc_frame():
    """Print a field-by-field breakdown of an LTC frame for 01:15:30:00 at 30fps."""

    ltc_gen = LTCGenerator(48000)

    # Use a non-trivial timecode so every BCD field has a non-zero value
    ltc_bits = ltc_gen.timecode_to_ltc_frame(1, 15, 30, 0, 30.0, False)
    bit_string = ''.join(str(b) for b in ltc_bits)

    print("=" * 80)
    print("SMPTE 12M LTC Frame Structure Analysis — 01:15:30:00 @ 30fps NDF")
    print("=" * 80)

    print("\nFrame field:")
    print(f"  Bits  0- 3  Frame units  (BCD): {bit_string[0:4]}  = {int(bit_string[0:4][::-1], 2)}")
    print(f"  Bits  4- 7  User bits 1:        {bit_string[4:8]}")
    print(f"  Bits  8- 9  Frame tens   (BCD): {bit_string[8:10]} = {int(bit_string[8:10][::-1], 2)}")
    print(f"  Bit  10     Drop-frame flag:     {bit_string[10]}")
    print(f"  Bit  11     Color-frame flag:    {bit_string[11]}")
    print(f"  Bits 12-15  User bits 2:         {bit_string[12:16]}")

    print("\nSeconds field:")
    print(f"  Bits 16-19  Seconds units (BCD): {bit_string[16:20]} = {int(bit_string[16:20][::-1], 2)}")
    print(f"  Bits 20-23  User bits 3:         {bit_string[20:24]}")
    print(f"  Bits 24-26  Seconds tens  (BCD): {bit_string[24:27]} = {int(bit_string[24:27][::-1], 2)}")
    print(f"  Bit  27     BMPC parity:         {bit_string[27]}")
    print(f"  Bits 28-31  User bits 4:         {bit_string[28:32]}")

    print("\nMinutes field:")
    print(f"  Bits 32-35  Minutes units (BCD): {bit_string[32:36]} = {int(bit_string[32:36][::-1], 2)}")
    print(f"  Bits 36-39  User bits 5:         {bit_string[36:40]}")
    print(f"  Bits 40-42  Minutes tens  (BCD): {bit_string[40:43]} = {int(bit_string[40:43][::-1], 2)}")
    print(f"  Bit  43     BGF 0:               {bit_string[43]}")
    print(f"  Bits 44-47  User bits 6:         {bit_string[44:48]}")

    print("\nHours field:")
    print(f"  Bits 48-51  Hours units   (BCD): {bit_string[48:52]} = {int(bit_string[48:52][::-1], 2)}")
    print(f"  Bits 52-55  User bits 7:         {bit_string[52:56]}")
    print(f"  Bits 56-57  Hours tens    (BCD): {bit_string[56:58]} = {int(bit_string[56:58][::-1], 2)}")
    print(f"  Bit  58     BGF 1:               {bit_string[58]}")
    print(f"  Bit  59     Reserved:            {bit_string[59]}")
    print(f"  Bits 60-63  User bits 8:         {bit_string[60:64]}")

    print("\nSync word (bits 64-79):")
    print(f"  Generated: {bit_string[64:80]}")
    print(f"  Expected:  0011111111111101")
    match = bit_string[64:80] == "0011111111111101"
    print(f"  {'Match' if match else 'MISMATCH'}")

    # Decode and cross-check
    def get_bcd(start, count):
        return sum(ltc_bits[start + i] * (2 ** i) for i in range(count))

    print("\n" + "=" * 80)
    print("Decoded values:")
    h = get_bcd(56, 2) * 10 + get_bcd(48, 4)
    m = get_bcd(40, 3) * 10 + get_bcd(32, 4)
    s = get_bcd(24, 3) * 10 + get_bcd(16, 4)
    f = get_bcd(8,  2) * 10 + get_bcd(0,  4)
    print(f"  Timecode: {h:02d}:{m:02d}:{s:02d}:{f:02d}  {'OK' if (h,m,s,f)==(1,15,30,0) else 'MISMATCH'}")
    print(f"  Total 1-bits: {sum(ltc_bits)}  (BMPC parity: {'even OK' if sum(ltc_bits) % 2 == 0 else 'odd FAIL'})")


if __name__ == "__main__":
    analyze_ltc_frame()
