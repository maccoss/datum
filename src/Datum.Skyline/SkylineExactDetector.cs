//! Byte-identical Skyline peak-boundary detector (wraps vendored pwiz PeakFinder).

#nullable enable

using System.Collections.Generic;
using Datum.Core.Algorithms;
using pwiz.Common.PeakFinding;

namespace Datum.Skyline;

/// <summary>
/// Peak-boundary detector that delegates to Skyline's actual managed peak finder (vendored from
/// ProteoWizard/pwiz, <c>pwiz.Common.PeakFinding</c>). The chromatogram is handed to the finder
/// as <see langword="float"/> arrays (as Skyline stores it) and the highest-area peak's
/// boundaries are returned, so the boundaries are byte-identical to what Skyline would report for
/// the same chromatogram.
/// </summary>
public sealed class SkylineExactDetector : IPeakDetector
{
    private const int MaxPeaks = 20;

    /// <inheritdoc/>
    public string Name => "Skyline (boundaries only)";

    /// <inheritdoc/>
    public PeakBounds Detect(double[] rt, double[] intensity, DetectorParams p)
    {
        int n = intensity.Length;
        if (n < 3)
        {
            return PeakBounds.NotFound;
        }

        using IPeakFinder finder = PeakFinders.NewDefaultPeakFinder();
        finder.SetChromatogram(SkylineExactIntegrator.ToFloat(rt), SkylineExactIntegrator.ToFloat(intensity));

        IList<IFoundPeak> peaks = finder.CalcPeaks(MaxPeaks, System.Array.Empty<int>());
        if (peaks.Count == 0)
        {
            return PeakBounds.NotFound;
        }

        // CalcPeaks returns peaks sorted best-first (identified, then descending area).
        IFoundPeak best = peaks[0];
        int start = Clamp(best.StartIndex, n);
        int end = Clamp(best.EndIndex, n);
        int apex = Clamp(best.TimeIndex, n);

        foreach (IFoundPeak peak in peaks)
        {
            peak.Dispose();
        }

        if (end <= start)
        {
            return PeakBounds.NotFound;
        }

        return new PeakBounds(true, start, end, apex, rt[start], rt[end], rt[apex]);
    }

    private static int Clamp(int index, int n) => index < 0 ? 0 : index >= n ? n - 1 : index;
}
