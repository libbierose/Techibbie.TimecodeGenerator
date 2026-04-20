"""
Timecode handler for converting elapsed time to timecode format.
Supports various frame rates including drop-frame and non-drop-frame timecode.
"""

from typing import Tuple


class TimecodeHandler:
    """Handles timecode calculations for various frame rates."""
    
    # Standard frame rates (fps: drop_frame)
    FRAME_RATES = {
        24: False,      # 24fps (no drop frame)
        25: False,      # 25fps (no drop frame)
        29.97: True,    # 29.97fps (drop frame)
        30: False,      # 30fps (no drop frame)
        48: False,      # 48fps (no drop frame)
        50: False,      # 50fps (no drop frame)
        59.94: True,    # 59.94fps (drop frame)
        60: False,      # 60fps (no drop frame)
        120: False,     # 120fps (no drop frame)
    }
    
    def __init__(self, fps: float = 30.0):
        """
        Initialize timecode handler.
        
        Args:
            fps: Frames per second (must be in FRAME_RATES or a valid custom fps)
        """
        if fps not in self.FRAME_RATES:
            # For custom FPS, default to no drop frame
            self.FRAME_RATES[fps] = False
        
        self.fps = fps
        self.drop_frame = self.FRAME_RATES.get(fps, False)
        
    def elapsed_time_to_timecode(self, elapsed_seconds: float) -> str:
        """
        Convert elapsed time in seconds to timecode format HH:MM:SS:FF
        
        Args:
            elapsed_seconds: Elapsed time in seconds
            
        Returns:
            Timecode string in format HH:MM:SS:FF (Hours:Minutes:Seconds:Frames)
        """
        total_frames = int(elapsed_seconds * self.fps)
        
        if self.drop_frame:
            return self._frames_to_dropframe(total_frames)
        else:
            return self._frames_to_nondropframe(total_frames)
    
    def _frames_to_nondropframe(self, total_frames: int) -> str:
        """
        Convert an absolute frame count to non-drop-frame timecode.

        Args:
            total_frames: Total frame count from 00:00:00:00

        Returns:
            Timecode string in HH:MM:SS:FF format
        """
        frames_per_second = int(self.fps)
        frames_per_minute = frames_per_second * 60
        frames_per_hour = frames_per_minute * 60
        
        hours = total_frames // frames_per_hour
        remaining = total_frames % frames_per_hour
        
        minutes = remaining // frames_per_minute
        remaining = remaining % frames_per_minute
        
        seconds = remaining // frames_per_second
        frames = remaining % frames_per_second
        
        return f"{hours:02d}:{minutes:02d}:{seconds:02d}:{frames:02d}"
    
    def _frames_to_dropframe(self, total_frames: int) -> str:
        """
        Convert an absolute frame count to drop-frame timecode.

        Drop-frame timecode compensates for the fractional frame rate of
        29.97 / 59.94 fps by skipping 2 (or 4) frame numbers at the start
        of every minute that is not a multiple of 10.  This keeps wall-clock
        time and timecode in sync over long durations.

        Args:
            total_frames: Total frame count from 00:00:00;00

        Returns:
            Timecode string in HH:MM:SS:FF format (semicolon convention not
            applied here — caller receives a colon-delimited string)
        """
        frames_per_second = round(self.fps)  # Use rounded fps for drop frame
        
        # For drop frame, we need to account for dropped frames
        # 29.97 fps: drop 2 frames at start of each minute except minutes 0, 10, 20, etc.
        # 59.94 fps: drop 4 frames
        
        frames_to_drop_per_10_minutes = 2 if self.fps < 50 else 4
        
        # Work with frame count including the dropped frames
        frames_per_10_minutes = frames_per_second * 60 * 10 - frames_to_drop_per_10_minutes * 9
        frames_per_minute = frames_per_second * 60 - frames_to_drop_per_10_minutes
        
        # Calculate 10-minute blocks
        blocks_10min = total_frames // frames_per_10_minutes
        remaining_frames = total_frames % frames_per_10_minutes
        
        # Add back the frames that were "dropped" for timecode purposes
        hours = blocks_10min // 6
        remaining_10min_blocks = blocks_10min % 6
        
        # Calculate minutes within the 10-minute block
        # First minute of each 10-minute block is full (no drops)
        if remaining_frames < frames_per_second * 60:
            minutes_in_block = 0
            frames_in_minute = remaining_frames
        else:
            remaining_frames -= frames_per_second * 60
            minutes_in_block = 1 + (remaining_frames // frames_per_minute)
            frames_in_minute = remaining_frames % frames_per_minute
        
        minutes = remaining_10min_blocks * 10 + minutes_in_block
        seconds = frames_in_minute // frames_per_second
        frames = frames_in_minute % frames_per_second
        
        return f"{hours:02d}:{minutes:02d}:{seconds:02d}:{frames:02d}"
    
    def get_supported_fps(self) -> list:
        """Get list of supported frame rates."""
        return sorted(list(self.FRAME_RATES.keys()))
    
    def is_drop_frame(self) -> bool:
        """Check if current fps uses drop-frame timecode."""
        return self.drop_frame
