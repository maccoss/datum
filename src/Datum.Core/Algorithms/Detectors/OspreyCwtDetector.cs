//! Osprey-style CWT consensus peak detector (ported from osprey-chromatography/src/cwt.rs).

using Datum.Core.Algorithms.Cwt;
using Datum.Core.Algorithms.Integrators;
using Datum.Core.Math;

namespace Datum.Core.Algorithms.Detectors;

/// <summary>
/// Continuous-wavelet-transform peak detector following Osprey's consensus approach: convolve
/// each transition XIC with a Mexican-hat wavelet whose scale is estimated from the peak FWHM,
/// take the pointwise median across transitions (rejecting single-transition interference),
/// locate the consensus maximum, and place boundaries at the zero-crossings extended to ~2 sigma
/// with a valley guard that prevents bleeding into adjacent peaks. With a single transition the
/// consensus reduces to that one wavelet response.
///
/// The "improved" variant keeps the interference-robust consensus apex but, instead of snapping the
/// boundaries to sampled points, estimates the peak standard deviation in retention-time units and
/// places the integration boundaries at apex +/- a fixed multiple of sigma as exact fractional
/// retention times, so an edge-estimating integrator integrates to the true boundary regardless of
/// sampling density. This is the integration model intended for Skyline / Osprey.
/// </summary>
public sealed class OspreyCwtDetector : IPeakDetector
{
    private const double ValleyRiseFraction = 0.05; // 5% of apex, per Osprey.
    private readonly bool _improved;

    /// <summary>
    /// Create the detector. When <paramref name="improved"/> is true the detector acts as an
    /// integration model: the wavelet scale and peak width are computed in retention-time units and
    /// the integration boundaries are placed at <see cref="DetectorParams.BoundarySigmaMultiple"/>
    /// times the estimated peak standard deviation as exact, fractional retention times rather than
    /// snapped to a sampled point, so an edge-estimating integrator can integrate to the true
    /// boundary regardless of sampling density. Otherwise the faithful index-based Osprey logic is
    /// used and the sigma multiple is ignored.
    /// </summary>
    public OspreyCwtDetector(bool improved = false) => _improved = improved;

    /// <inheritdoc/>
    public string Name => _improved ? "Osprey CWT (improved)" : "Osprey CWT";

    /// <inheritdoc/>
    public bool ProducesFractionalBoundaries => _improved;

    /// <inheritdoc/>
    public PeakBounds Detect(double[] rt, double[] intensity, DetectorParams p) =>
        DetectFromXics(rt, new[] { intensity }, p);

    /// <summary>
    /// Multi-transition consensus detection: median CWT response across the supplied XICs.
    /// The raw reference signal (used for the valley guard and apex refinement) is the sum
    /// of the XICs. Reused by the multi-transition (Koina) pipeline.
    /// </summary>
    public PeakBounds DetectFromXics(double[] rt, double[][] xics, DetectorParams p)
    {
        int n = rt.Length;
        if (n < 3 || xics.Length == 0)
        {
            return PeakBounds.NotFound;
        }

        double sigma = _improved ? CwtMath.EstimateScaleRt(xics, rt) : CwtMath.EstimateScale(xics);
        int radius = System.Math.Max(1, (int)System.Math.Ceiling(5.0 * sigma));
        double[] kernel = CwtMath.MexicanHatKernel(sigma, radius);

        // Median CWT consensus across transitions.
        var cwts = new double[xics.Length][];
        for (int t = 0; t < xics.Length; t++)
        {
            cwts[t] = CwtMath.ConvolveSame(xics[t], kernel);
        }

        var consensus = new double[n];
        var column = new double[xics.Length];
        for (int i = 0; i < n; i++)
        {
            for (int t = 0; t < xics.Length; t++)
            {
                column[t] = cwts[t][i];
            }

            consensus[i] = CwtMath.Median(column);
        }

        // Reference signal for apex refinement and boundary extension. The faithful (index) mode
        // sums the transitions; the improved integration model takes the per-point MEDIAN across
        // transitions instead, which (like the median CWT consensus) rejects interference that is
        // present in only a minority of transitions, so a single interfered transition cannot drag
        // the shared boundary into its interference.
        var reference = new double[n];
        if (_improved)
        {
            for (int i = 0; i < n; i++)
            {
                for (int t = 0; t < xics.Length; t++)
                {
                    column[t] = xics[t][i];
                }

                reference[i] = CwtMath.Median(column);
            }
        }
        else
        {
            for (int i = 0; i < n; i++)
            {
                double sum = 0.0;
                foreach (double[] xic in xics)
                {
                    sum += xic[i];
                }

                reference[i] = sum;
            }
        }

        // Consensus maximum.
        int cwtApex = 0;
        double cwtMax = consensus[0];
        for (int i = 1; i < n; i++)
        {
            if (consensus[i] > cwtMax)
            {
                cwtMax = consensus[i];
                cwtApex = i;
            }
        }

        if (cwtMax <= 0.0)
        {
            return PeakBounds.NotFound;
        }

        // Zero crossings of the consensus around the apex (~ +/- 1 sigma).
        int leftZero = cwtApex;
        while (leftZero > 0 && consensus[leftZero] > 0.0)
        {
            leftZero--;
        }

        int rightZero = cwtApex;
        while (rightZero < n - 1 && consensus[rightZero] > 0.0)
        {
            rightZero++;
        }

        int leftSigma = System.Math.Max(1, cwtApex - leftZero);
        int rightSigma = System.Math.Max(1, rightZero - cwtApex);
        int targetStart = System.Math.Max(0, cwtApex - 2 * leftSigma);
        int targetEnd = System.Math.Min(n - 1, cwtApex + 2 * rightSigma);

        // Refine the apex to the raw-signal maximum within the zero-crossing window.
        int apex = cwtApex;
        double apexVal = reference[cwtApex];
        for (int i = leftZero; i <= rightZero; i++)
        {
            if (reference[i] > apexVal)
            {
                apexVal = reference[i];
                apex = i;
            }
        }

        double apexIntensity = reference[apex];
        int start;
        int end;
        double startRt;
        double endRt;

        if (_improved)
        {
            // Integration model. The boundaries are placed where the peak returns to a fixed fraction
            // of its apex height, measured PER SIDE so a tailed peak gets an asymmetric boundary (the
            // trailing edge reaches farther than the leading edge). The fraction maps from the same
            // BoundarySigmaMultiple knob k as exp(-k^2/2), so on a symmetric (Gaussian) peak this
            // reproduces apex +/- k*sigma exactly. To find the trailing edge stably (it sits near
            // baseline on a shallow slope, where a raw crossing is noisy), an EMG shape is fitted to
            // the smoothed median consensus and the crossing is taken on the noise-free analytic model.
            // The valley guard still truncates at adjacent interference, and the median reference and
            // baseline subtraction keep the placement interference- and background-independent.
            double[] smoothed = Smoothing.SavitzkyGolay5(reference);

            // Refine the apex to the maximum of the SAME smoothed reference the boundary walk uses,
            // within the consensus zero-crossing window. This keeps the walk outward monotonic from
            // the apex, so the valley guard fires only at a genuine adjacent peak rather than on the
            // climb back to a slightly noise-displaced apex (which would collapse one boundary).
            for (int i = leftZero; i <= rightZero; i++)
            {
                if (smoothed[i] > smoothed[apex])
                {
                    apex = i;
                }
            }

            double smoothApex = smoothed[apex];
            double sigmaRt = CwtMath.EstimateSigmaRt(xics, rt);
            double apexRt = rt[apex];
            double k = System.Math.Clamp(p.BoundarySigmaMultiple, 1.0, 6.0);

            // Symmetric targets are the fallback if the shape fit fails.
            double startTarget = apexRt - k * sigmaRt;
            double endTarget = apexRt + k * sigmaRt;

            EmgFit.Result? shape = FitConsensusShape(smoothed, rt, apex, sigmaRt, smoothApex);
            if (shape is { Tau: > 0.0 } s)
            {
                (double leftRt, double rightRt) = EmgFit.HeightCrossings(
                    s.Mu, s.Sigma, s.Tau, System.Math.Exp(-0.5 * k * k));
                startTarget = leftRt;
                endTarget = rightRt;
            }

            startRt = BoundaryRt(smoothed, rt, apex, -1, startTarget, smoothApex);
            endRt = BoundaryRt(smoothed, rt, apex, +1, endTarget, smoothApex);
            start = IndexAtOrAfter(rt, startRt, apex);
            end = IndexAtOrBefore(rt, endRt, apex);
        }
        else
        {
            // Faithful Osprey: extend to ~2 sigma with the valley guard, then the asymmetric FWHM
            // cap (cap_factor = 2.0) so the scale cannot over-extend the boundaries.
            start = ExtendLeft(reference, leftZero, targetStart, apexIntensity);
            end = ExtendRight(reference, rightZero, targetEnd, apexIntensity);
            (int capStart, int capEnd) = FwhmCap(reference, apex, apexIntensity, CapFactor);
            start = System.Math.Max(start, capStart);
            end = System.Math.Min(end, capEnd);
            startRt = rt[start];
            endRt = rt[end];
        }

        return new PeakBounds(true, start, end, apex, startRt, endRt, rt[apex]);
    }

    private const double CapFactor = 2.0;       // faithful index-based FWHM cap (2 x HWHM ~ 2.35 sigma)

    /// <summary>Smallest index at or after the target retention time, not past the apex.</summary>
    private static int IndexAtOrAfter(double[] rt, double target, int apex)
    {
        int i = 0;
        while (i < apex && rt[i] < target)
        {
            i++;
        }

        return i;
    }

    /// <summary>Largest index at or before the target retention time, not before the apex.</summary>
    private static int IndexAtOrBefore(double[] rt, double target, int apex)
    {
        int i = rt.Length - 1;
        while (i > apex && rt[i] > target)
        {
            i--;
        }

        return i;
    }

    /// <summary>
    /// Fit an EMG shape to the smoothed median consensus over a valley-bounded window around the apex
    /// (generous on the trailing side so the tail is included), with the window's baseline subtracted
    /// so a constant background does not bias the shape. Returns null when there are too few points or
    /// the fit is unusable, in which case the caller falls back to the symmetric k*sigma boundary.
    /// </summary>
    private static EmgFit.Result? FitConsensusShape(double[] smoothed, double[] rt, int apex, double sigmaRt, double apexIntensity)
    {
        double apexRt = rt[apex];
        double loRt = BoundaryRt(smoothed, rt, apex, -1, apexRt - 4.0 * sigmaRt, apexIntensity);
        double hiRt = BoundaryRt(smoothed, rt, apex, +1, apexRt + 12.0 * sigmaRt, apexIntensity);
        int lo = IndexAtOrAfter(rt, loRt, apex);
        int hi = IndexAtOrBefore(rt, hiRt, apex);
        if (hi - lo + 1 < 5)
        {
            return null;
        }

        double baseline = smoothed[lo];
        for (int i = lo + 1; i <= hi; i++)
        {
            if (smoothed[i] < baseline)
            {
                baseline = smoothed[i];
            }
        }

        int count = hi - lo + 1;
        var x = new double[count];
        var y = new double[count];
        for (int j = 0; j < count; j++)
        {
            x[j] = rt[lo + j];
            y[j] = smoothed[lo + j] - baseline;
        }

        return EmgFit.Fit(x, y);
    }

    /// <summary>
    /// Boundary retention time walking from the apex toward the (fractional) target retention time.
    /// Returns the exact target RT if it is reached before any valley, the valley RT if the signal
    /// rises again first (an adjacent peak / interference present across transitions), or the array
    /// edge RT if the target lies beyond it.
    /// </summary>
    private static double BoundaryRt(double[] smoothed, double[] rt, int apex, int step, double targetRt, double apexIntensity)
    {
        double runningMin = smoothed[apex];
        int valley = apex;
        int i = apex;
        while (true)
        {
            int next = i + step;
            if (next < 0 || next >= smoothed.Length)
            {
                return rt[i];
            }

            if ((step < 0 && rt[next] <= targetRt) || (step > 0 && rt[next] >= targetRt))
            {
                return targetRt; // reached the fractional boundary before any valley
            }

            if (smoothed[next] < runningMin)
            {
                runningMin = smoothed[next];
                valley = next;
            }
            else if (smoothed[next] - runningMin > ValleyRiseFraction * apexIntensity)
            {
                return rt[valley]; // rose past a valley: stop before the adjacent peak
            }

            i = next;
        }
    }

    /// <summary>Cap boundaries to apex +/- capFactor * half-width-at-half-maximum (index units).</summary>
    private static (int Start, int End) FwhmCap(double[] reference, int apex, double apexIntensity, double capFactor)
    {
        double half = 0.5 * apexIntensity;

        int leftHalf = apex;
        while (leftHalf > 0 && reference[leftHalf] > half)
        {
            leftHalf--;
        }

        int rightHalf = apex;
        while (rightHalf < reference.Length - 1 && reference[rightHalf] > half)
        {
            rightHalf++;
        }

        int leftHw = System.Math.Max(1, apex - leftHalf);
        int rightHw = System.Math.Max(1, rightHalf - apex);
        int capStart = System.Math.Max(0, apex - (int)System.Math.Round(capFactor * leftHw));
        int capEnd = System.Math.Min(reference.Length - 1, apex + (int)System.Math.Round(capFactor * rightHw));
        return (capStart, capEnd);
    }

    /// <summary>Extend left toward the target, stopping at a valley if the signal rises again.</summary>
    private static int ExtendLeft(double[] reference, int from, int target, double apexIntensity)
    {
        double runningMin = reference[from];
        int valley = from;
        for (int i = from; i >= target; i--)
        {
            if (reference[i] < runningMin)
            {
                runningMin = reference[i];
                valley = i;
            }
            else if (reference[i] - runningMin > ValleyRiseFraction * apexIntensity)
            {
                return valley;
            }
        }

        return target;
    }

    /// <summary>Extend right toward the target, stopping at a valley if the signal rises again.</summary>
    private static int ExtendRight(double[] reference, int from, int target, double apexIntensity)
    {
        double runningMin = reference[from];
        int valley = from;
        for (int i = from; i <= target; i++)
        {
            if (reference[i] < runningMin)
            {
                runningMin = reference[i];
                valley = i;
            }
            else if (reference[i] - runningMin > ValleyRiseFraction * apexIntensity)
            {
                return valley;
            }
        }

        return target;
    }
}
