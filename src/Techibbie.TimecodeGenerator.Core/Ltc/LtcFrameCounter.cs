namespace Techibbie.TimecodeGenerator.Core.Ltc;

/// <summary>
/// Frame-by-frame timecode counter with correct SMPTE drop-frame numbering: in drop-frame,
/// frame numbers :00 and :01 are skipped at the start of every minute except each tenth minute.
/// </summary>
public sealed class LtcFrameCounter
{
    private readonly int _nominalFps;
    private readonly bool _dropFrame;
    private readonly int _dropPerMinute;

    public int Hour { get; private set; }
    public int Minute { get; private set; }
    public int Second { get; private set; }
    public int Frame { get; private set; }

    public LtcFrameCounter(int hour, int minute, int second, int frame, int nominalFps, bool dropFrame)
    {
        _nominalFps = nominalFps;
        _dropFrame = dropFrame;
        _dropPerMinute = nominalFps >= 60 ? 4 : 2;

        Hour = hour % 24;
        Minute = minute % 60;
        Second = second % 60;
        Frame = frame >= nominalFps ? nominalFps - 1 : frame;
        SkipDroppedFrameNumbers();
    }

    public void Advance()
    {
        Frame++;
        if (Frame < _nominalFps) return;

        Frame = 0;
        Second++;
        if (Second >= 60)
        {
            Second = 0;
            Minute++;
            if (Minute >= 60)
            {
                Minute = 0;
                Hour++;
                if (Hour >= 24) Hour = 0;
            }
        }
        SkipDroppedFrameNumbers();
    }

    private void SkipDroppedFrameNumbers()
    {
        if (_dropFrame && Second == 0 && Minute % 10 != 0 && Frame < _dropPerMinute)
        {
            Frame = _dropPerMinute;
        }
    }
}
