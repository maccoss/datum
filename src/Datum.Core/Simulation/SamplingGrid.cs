//! Uniform retention-time sampling parameterized by points-across-the-peak.

using Datum.Core.Models;

namespace Datum.Core.Simulation;

/// <summary>
/// A uniform retention-time sampling grid. The number of points across the reference peak sets
/// the sampling <em>interval</em> (<c>interval = peakRange / pointsAcrossPeak</c>); the grid is
/// then laid down at that fixed interval across the whole analysis window, so samples before and
/// after the peak appear automatically (as with a real fixed instrument cycle time). A fractional
/// <c>offset</c> phases the grid relative to the peak, and <c>contextFraction</c> sets how much
/// baseline (in peak-widths) is sampled on each side.
/// </summary>
public sealed class SamplingGrid
{
    private SamplingGrid(double[] rt, double interval, double referenceStart, double referenceEnd)
    {
        Rt = rt;
        Interval = interval;
        ReferenceStart = referenceStart;
        ReferenceEnd = referenceEnd;
    }

    /// <summary>All sample retention times (flanking points included), in increasing order.</summary>
    public double[] Rt { get; }

    /// <summary>Uniform spacing between samples in retention-time units.</summary>
    public double Interval { get; }

    /// <summary>Reference peak start (where the clean peak rises above the boundary fraction).</summary>
    public double ReferenceStart { get; }

    /// <summary>Reference peak end (where the clean peak falls below the boundary fraction).</summary>
    public double ReferenceEnd { get; }

    /// <summary>
    /// Build a sampling grid for a given number of points across the reference peak.
    /// </summary>
    /// <param name="peak">The analyte peak whose width defines the points-across-peak scale.</param>
    /// <param name="pointsAcrossPeak">Number of samples spanning the reference peak width (sets the interval).</param>
    /// <param name="offset">Fractional phase of the grid relative to the peak, in [0, 1).</param>
    /// <param name="boundaryFraction">
    /// Fraction of apex intensity defining the reference peak edges (0.01 ~ notebook rel_height=0.99).
    /// </param>
    /// <param name="contextFraction">
    /// Baseline sampled on each side of the peak, as a fraction of the reference peak width
    /// (0.5 = half a peak-width of context each side). Provides the off-peak points the detector
    /// needs without a separate flanking-points control.
    /// </param>
    public static SamplingGrid Create(
        IPeakModel peak,
        int pointsAcrossPeak,
        double offset,
        double boundaryFraction = 0.01,
        double contextFraction = 0.5)
    {
        if (pointsAcrossPeak < 1)
        {
            throw new System.ArgumentOutOfRangeException(nameof(pointsAcrossPeak), "Need at least one point across the peak.");
        }

        (double referenceStart, double referenceEnd) = ReferenceBounds(peak, boundaryFraction);
        double peakRange = referenceEnd - referenceStart;
        double interval = peakRange / pointsAcrossPeak;

        // Phase the grid relative to the peak, then fill the window with the context margins.
        double windowLo = referenceStart - contextFraction * peakRange;
        double windowHi = referenceEnd + contextFraction * peakRange;
        double phase = referenceStart + offset * interval;

        int before = (int)System.Math.Ceiling((phase - windowLo) / interval);
        double startRt = phase - before * interval;
        int total = before + 1 + (int)System.Math.Ceiling((windowHi - phase) / interval);

        var rt = new double[total];
        for (int i = 0; i < total; i++)
        {
            rt[i] = startRt + i * interval;
        }

        return new SamplingGrid(rt, interval, referenceStart, referenceEnd);
    }

    /// <summary>
    /// Locate the reference peak edges: the outermost retention times at which the clean
    /// peak exceeds <paramref name="boundaryFraction"/> of its apex intensity.
    /// </summary>
    public static (double Start, double End) ReferenceBounds(IPeakModel peak, double boundaryFraction)
    {
        double apex = peak.Evaluate(peak.ApexRt);
        double threshold = boundaryFraction * apex;

        // Scan a wide window around the peak at fine resolution.
        const int n = 4000;
        double a = peak.Center - 12.0 * peak.Sigma;
        double b = peak.ApexRt + 24.0 * peak.Sigma; // generous for tailing shapes
        double dx = (b - a) / (n - 1);

        double start = peak.ApexRt;
        double end = peak.ApexRt;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            double rt = a + i * dx;
            if (peak.Evaluate(rt) >= threshold)
            {
                if (!found)
                {
                    start = rt;
                    found = true;
                }

                end = rt;
            }
        }

        return (start, end);
    }
}
