"""
List all available audio output devices with their sample rates and channel counts.
Run this to find the device ID to use in Settings.
"""

import sys
import os

sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', 'src'))

import sounddevice as sd

for i, d in enumerate(sd.query_devices()):
    if d['max_output_channels'] > 0:
        print(f"[{i}] {d['name']} | default_sr={d['default_samplerate']} Hz | ch={d['max_output_channels']}")
