using System;

namespace Techibbie.TimecodeGenerator.Core.Ltc;

/// <summary>
/// The rate LTC is actually transmitted at for a given app frame rate.
///
/// SMPTE 12M only defines LTC for 24/25/30 fps (with 30 optionally drop-frame). Higher
/// rates (48/50/59.94/60/120) have no LTC representation of their own; the standard practice
/// is to send LTC at half (or quarter) rate — 50→25, 59.94→29.97 DF, 60→30 — so receivers
/// like DaVinci Resolve can decode it. <see cref="Divisor"/> says how many app frames make one LTC frame.
/// </summary>
public readonly record struct LtcRate(int NominalFps, double ExactFps, bool DropFrame, int Divisor)
{
    public static LtcRate FromFrameRate(double appFps, bool appDropFrame)
    {
        var fps = appFps;
        var divisor = 1;
        while (fps > 30.5)
        {
            fps /= 2;
            divisor *= 2;
        }

        var nominal = Math.Max(1, (int)Math.Round(fps));
        var drop = appDropFrame && nominal == 30;

        // The "29.97" family really runs at 30000/1001; using the exact ratio keeps a
        // long-running generator from drifting against a real 29.97 video clock.
        double exact;
        if (Math.Abs(fps - 29.97) < 0.01) exact = 30000.0 / 1001.0;
        else if (Math.Abs(fps - 23.976) < 0.01) exact = 24000.0 / 1001.0;
        else exact = fps;

        return new LtcRate(nominal, exact, drop, divisor);
    }
}
