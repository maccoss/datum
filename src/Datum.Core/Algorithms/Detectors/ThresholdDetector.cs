//! Simple threshold-crossing peak detector (the notebook's fallback method).

namespace Datum.Core.Algorithms.Detectors;

/// <summary>
/// Detects the dominant peak as the global maximum, then walks outward to the first
/// retention times where the intensity falls below a fraction of the apex. The fraction is
/// <c>1 - BoundaryRelHeight</c> (so the notebook default of 0.99 yields a 1%-of-apex edge).
/// Boundary retention times are linearly interpolated at the exact crossing.
/// </summary>
public sealed class ThresholdDetector : IPeakDetector
{
    /// <inheritdoc/>
    public string Name => "Threshold";

    /// <inheritdoc/>
    public bool ProducesFractionalBoundaries => true;

    /// <inheritdoc/>
    public PeakBounds Detect(double[] rt, double[] intensity, DetectorParams p)
    {
        if (intensity.Length < 2)
        {
            return PeakBounds.NotFound;
        }

        int apex = 0;
        double max = intensity[0];
        for (int i = 1; i < intensity.Length; i++)
        {
            if (intensity[i] > max)
            {
                max = intensity[i];
                apex = i;
            }
        }

        if (max <= 0.0)
        {
            return PeakBounds.NotFound;
        }

        double threshold = (1.0 - p.BoundaryRelHeight) * max;

        // Walk left to the first crossing below threshold, interpolate the exact retention time.
        int left = apex;
        while (left > 0 && intensity[left] >= threshold)
        {
            left--;
        }

        double startRt = InterpolateCrossing(rt, intensity, left, left + 1, threshold);
        int startIndex = intensity[left] >= threshold ? left : left + 1;

        int right = apex;
        while (right < intensity.Length - 1 && intensity[right] >= threshold)
        {
            right++;
        }

        double endRt = InterpolateCrossing(rt, intensity, right - 1, right, threshold);
        int endIndex = intensity[right] >= threshold ? right : right - 1;

        return new PeakBounds(true, startIndex, endIndex, apex, startRt, endRt, rt[apex]);
    }

    /// <summary>Interpolate the retention time at which the trace crosses <paramref name="level"/>.</summary>
    private static double InterpolateCrossing(double[] rt, double[] intensity, int lo, int hi, double level)
    {
        if (lo < 0)
        {
            return rt[0];
        }

        if (hi >= intensity.Length)
        {
            return rt[^1];
        }

        double denom = intensity[hi] - intensity[lo];
        if (System.Math.Abs(denom) < 1e-12)
        {
            return rt[lo];
        }

        double t = (level - intensity[lo]) / denom;
        t = System.Math.Clamp(t, 0.0, 1.0);
        return rt[lo] + t * (rt[hi] - rt[lo]);
    }
}
