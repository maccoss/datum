//! Port of scipy.signal.find_peaks + peak_widths, as used in the reference notebook.

namespace Datum.Core.Algorithms.Detectors;

/// <summary>
/// Detects peaks the way the reference notebook does: local maxima filtered by a height
/// threshold (<c>HeightFraction * max</c>) and topographic prominence, with integration
/// boundaries from <c>peak_widths</c> at the requested relative height. The boundary edges
/// use scipy's fractional-index interpolation. This is a faithful port of the relevant
/// parts of <c>scipy.signal.find_peaks</c> and <c>scipy.signal.peak_widths</c>.
/// </summary>
public sealed class FindPeaksDetector : IPeakDetector
{
    /// <inheritdoc/>
    public string Name => "Peak finder (scipy)";

    /// <inheritdoc/>
    public bool ProducesFractionalBoundaries => true;

    /// <inheritdoc/>
    public PeakBounds Detect(double[] rt, double[] intensity, DetectorParams p)
    {
        int n = intensity.Length;
        if (n < 3)
        {
            return PeakBounds.NotFound;
        }

        double max = 0.0;
        foreach (double v in intensity)
        {
            if (v > max)
            {
                max = v;
            }
        }

        if (max <= 0.0)
        {
            return PeakBounds.NotFound;
        }

        double heightThreshold = p.HeightFraction * max;

        // 1. Local maxima (centers of plateaus), filtered by absolute height.
        var candidates = new List<int>();
        int i = 1;
        while (i < n - 1)
        {
            if (intensity[i - 1] < intensity[i])
            {
                int ahead = i + 1;
                while (ahead < n - 1 && intensity[ahead] == intensity[i])
                {
                    ahead++;
                }

                if (intensity[ahead] < intensity[i])
                {
                    int mid = (i + ahead - 1) / 2;
                    if (intensity[mid] >= heightThreshold)
                    {
                        candidates.Add(mid);
                    }

                    i = ahead;
                    continue;
                }
            }

            i++;
        }

        if (candidates.Count == 0)
        {
            return PeakBounds.NotFound;
        }

        // 2. Filter by prominence, then pick the tallest surviving peak (notebook: argmax of heights).
        int best = -1;
        double bestHeight = double.NegativeInfinity;
        double bestProminence = 0.0;
        foreach (int peak in candidates)
        {
            double prominence = Prominence(intensity, peak);
            if (prominence < p.Prominence)
            {
                continue;
            }

            if (intensity[peak] > bestHeight)
            {
                bestHeight = intensity[peak];
                bestProminence = prominence;
                best = peak;
            }
        }

        if (best < 0)
        {
            return PeakBounds.NotFound;
        }

        // 3. peak_widths: evaluation height = apex - prominence * relHeight.
        double evalHeight = intensity[best] - bestProminence * p.BoundaryRelHeight;
        double leftIp = LeftIntersection(intensity, best, evalHeight);
        double rightIp = RightIntersection(intensity, best, evalHeight);

        double startRt = FractionalRt(rt, leftIp);
        double endRt = FractionalRt(rt, rightIp);
        int startIndex = (int)System.Math.Ceiling(leftIp);
        int endIndex = (int)System.Math.Floor(rightIp);

        return new PeakBounds(true, startIndex, endIndex, best, startRt, endRt, rt[best]);
    }

    /// <summary>
    /// Topographic prominence (scipy algorithm): scan outward to the nearest higher signal
    /// (or the array edge) on each side, take the minimum within each interval, and subtract
    /// the higher of the two minima from the peak height.
    /// </summary>
    private static double Prominence(double[] y, int peak)
    {
        double height = y[peak];

        int iLeft = peak;
        double leftMin = height;
        while (iLeft > 0 && y[iLeft] <= height)
        {
            iLeft--;
            if (y[iLeft] < leftMin)
            {
                leftMin = y[iLeft];
            }
        }

        int iRight = peak;
        double rightMin = height;
        while (iRight < y.Length - 1 && y[iRight] <= height)
        {
            iRight++;
            if (y[iRight] < rightMin)
            {
                rightMin = y[iRight];
            }
        }

        double baseline = System.Math.Max(leftMin, rightMin);
        return height - baseline;
    }

    private static double LeftIntersection(double[] y, int peak, double height)
    {
        int i = peak;
        while (i > 0 && y[i] > height)
        {
            i--;
        }

        if (y[i] >= height)
        {
            return i;
        }

        // Crossing between i and i+1.
        return i + (height - y[i]) / (y[i + 1] - y[i]);
    }

    private static double RightIntersection(double[] y, int peak, double height)
    {
        int i = peak;
        while (i < y.Length - 1 && y[i] > height)
        {
            i++;
        }

        if (y[i] >= height)
        {
            return i;
        }

        return i - (height - y[i]) / (y[i - 1] - y[i]);
    }

    /// <summary>Linear interpolation of retention time at a fractional array index.</summary>
    private static double FractionalRt(double[] rt, double index)
    {
        if (index <= 0.0)
        {
            return rt[0];
        }

        if (index >= rt.Length - 1)
        {
            return rt[^1];
        }

        int lo = (int)System.Math.Floor(index);
        double frac = index - lo;
        return rt[lo] + frac * (rt[lo + 1] - rt[lo]);
    }
}
