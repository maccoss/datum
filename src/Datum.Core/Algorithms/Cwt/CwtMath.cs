//! Pure CWT math ported from Osprey (osprey-chromatography/src/cwt.rs).

namespace Datum.Core.Algorithms.Cwt;

/// <summary>
/// Continuous-wavelet-transform primitives ported from Osprey's <c>cwt.rs</c>: the
/// zero-mean Mexican-hat (Ricker) kernel, scale estimation from fragment FWHM, "same"-size
/// convolution, and the pointwise median used for multi-transition consensus. These are the
/// building blocks of <c>OspreyCwtDetector</c>.
/// </summary>
public static class CwtMath
{
    /// <summary>
    /// Discretized Mexican-hat (Ricker) wavelet kernel of radius
    /// <paramref name="kernelRadius"/> at the given <paramref name="sigma"/>, with a
    /// zero-mean correction so it removes any DC offset. Matches Osprey's
    /// <c>mexican_hat_kernel</c>.
    /// </summary>
    public static double[] MexicanHatKernel(double sigma, int kernelRadius)
    {
        int len = 2 * kernelRadius + 1;
        var kernel = new double[len];
        double center = kernelRadius;
        double norm = 2.0 / (System.Math.Sqrt(3.0 * sigma) * System.Math.Pow(System.Math.PI, 0.25));

        for (int i = 0; i < len; i++)
        {
            double t = (i - center) / sigma;
            kernel[i] = norm * (1.0 - t * t) * System.Math.Exp(-0.5 * t * t);
        }

        // Zero-mean correction (remove DC offset introduced by discretization).
        double mean = 0.0;
        foreach (double v in kernel)
        {
            mean += v;
        }

        mean /= len;
        for (int i = 0; i < len; i++)
        {
            kernel[i] -= mean;
        }

        return kernel;
    }

    /// <summary>
    /// Estimate the CWT scale (sigma, in samples) as the median fragment FWHM divided by
    /// 2.355, clamped to [2, 20]. Falls back to 4.0 when no FWHM can be measured. Matches
    /// Osprey's <c>estimate_cwt_scale</c>.
    /// </summary>
    public static double EstimateScale(IReadOnlyList<double[]> xics)
    {
        var fwhms = new List<double>();
        foreach (double[] xic in xics)
        {
            double? fwhm = FullWidthHalfMax(xic);
            if (fwhm is { } w && w > 0.0)
            {
                fwhms.Add(w);
            }
        }

        if (fwhms.Count == 0)
        {
            return 4.0;
        }

        double sigma = Median(fwhms.ToArray()) / 2.355;
        return System.Math.Clamp(sigma, 2.0, 20.0);
    }

    /// <summary>
    /// Estimate the CWT scale in <em>sample</em> units from the median fragment FWHM measured in
    /// <em>retention time</em>, divided by 2.355 and converted to samples via the average sample
    /// spacing. Unlike <see cref="EstimateScale"/>, this tracks the peak's true width regardless of
    /// how densely it is sampled (it is not clamped to an index-count range). Falls back to 4 samples.
    /// </summary>
    public static double EstimateScaleRt(IReadOnlyList<double[]> xics, double[] rt)
    {
        if (rt.Length < 2)
        {
            return 4.0;
        }

        double dt = (rt[^1] - rt[0]) / (rt.Length - 1);
        if (dt <= 0.0)
        {
            return 4.0;
        }

        double sigmaSamples = EstimateSigmaRt(xics, rt) / dt;
        return System.Math.Clamp(sigmaSamples, 1.0, 50.0);
    }

    /// <summary>
    /// Estimate the peak standard deviation in <em>retention-time</em> units as the median
    /// per-transition FWHM divided by 2.355. Taking the median across transitions rejects the width
    /// inflation of a single interfered transition, and measuring each transition's FWHM at its own
    /// half-maximum keeps the estimate well above the noise. This is the integration-boundary width
    /// estimate for the improved Osprey model; unlike <see cref="EstimateScaleRt"/> it is not clamped
    /// to a sample-count range, so it stays accurate at coarse sampling. Falls back to ~4 samples.
    /// </summary>
    public static double EstimateSigmaRt(IReadOnlyList<double[]> xics, double[] rt)
    {
        var fwhms = new List<double>();
        foreach (double[] xic in xics)
        {
            double? fwhm = FullWidthHalfMaxRt(rt, xic);
            if (fwhm is { } w && w > 0.0)
            {
                fwhms.Add(w);
            }
        }

        if (fwhms.Count == 0)
        {
            double dt = rt.Length >= 2 ? (rt[^1] - rt[0]) / (rt.Length - 1) : 1.0;
            return 4.0 * dt;
        }

        return Median(fwhms.ToArray()) / 2.355;
    }

    /// <summary>"Same"-size direct convolution with zero padding at the edges.</summary>
    public static double[] ConvolveSame(double[] signal, double[] kernel)
    {
        int n = signal.Length;
        int k = kernel.Length;
        int half = k / 2;
        var result = new double[n];
        for (int i = 0; i < n; i++)
        {
            double acc = 0.0;
            for (int j = 0; j < k; j++)
            {
                int idx = i + j - half;
                if (idx >= 0 && idx < n)
                {
                    acc += signal[idx] * kernel[j];
                }
            }

            result[i] = acc;
        }

        return result;
    }

    /// <summary>Median of a sample (sorts a copy).</summary>
    public static double Median(double[] values)
    {
        if (values.Length == 0)
        {
            return 0.0;
        }

        double[] copy = (double[])values.Clone();
        System.Array.Sort(copy);
        int mid = copy.Length / 2;
        return copy.Length % 2 == 1 ? copy[mid] : 0.5 * (copy[mid - 1] + copy[mid]);
    }

    /// <summary>Full width at half maximum of a single XIC, in sample units (linear interpolation).</summary>
    private static double? FullWidthHalfMax(double[] xic)
    {
        if (xic.Length < 3)
        {
            return null;
        }

        int apex = 0;
        double max = xic[0];
        double min = xic[0];
        for (int i = 1; i < xic.Length; i++)
        {
            if (xic[i] > max)
            {
                max = xic[i];
                apex = i;
            }

            if (xic[i] < min)
            {
                min = xic[i];
            }
        }

        if (max <= min)
        {
            return null;
        }

        // Half maximum measured ABOVE the baseline floor, so a constant chemical background does not
        // push the half-height up into the tails (which would inflate the FWHM and, through it, the
        // CWT scale and the integration boundaries).
        double half = min + 0.5 * (max - min);

        double? left = null;
        for (int i = apex; i > 0; i--)
        {
            if (xic[i] >= half && xic[i - 1] < half)
            {
                double t = (half - xic[i - 1]) / (xic[i] - xic[i - 1]);
                left = (i - 1) + t;
                break;
            }
        }

        double? right = null;
        for (int i = apex; i < xic.Length - 1; i++)
        {
            if (xic[i] >= half && xic[i + 1] < half)
            {
                double t = (half - xic[i]) / (xic[i + 1] - xic[i]);
                right = i + t;
                break;
            }
        }

        if (left is { } l && right is { } r)
        {
            return r - l;
        }

        return null;
    }

    /// <summary>Full width at half maximum of a single XIC, in retention-time units (linear interpolation).</summary>
    private static double? FullWidthHalfMaxRt(double[] rt, double[] xic)
    {
        if (xic.Length < 3)
        {
            return null;
        }

        int apex = 0;
        double max = xic[0];
        double min = xic[0];
        for (int i = 1; i < xic.Length; i++)
        {
            if (xic[i] > max)
            {
                max = xic[i];
                apex = i;
            }

            if (xic[i] < min)
            {
                min = xic[i];
            }
        }

        if (max <= min)
        {
            return null;
        }

        // Half maximum measured ABOVE the baseline floor (see FullWidthHalfMax) so that a constant
        // chemical background does not inflate the width estimate.
        double half = min + 0.5 * (max - min);

        double? left = null;
        for (int i = apex; i > 0; i--)
        {
            if (xic[i] >= half && xic[i - 1] < half)
            {
                double t = (half - xic[i - 1]) / (xic[i] - xic[i - 1]);
                left = rt[i - 1] + t * (rt[i] - rt[i - 1]);
                break;
            }
        }

        double? right = null;
        for (int i = apex; i < xic.Length - 1; i++)
        {
            if (xic[i] >= half && xic[i + 1] < half)
            {
                double t = (half - xic[i]) / (xic[i + 1] - xic[i]);
                right = rt[i] + t * (rt[i + 1] - rt[i]);
                break;
            }
        }

        if (left is { } l && right is { } r)
        {
            return r - l;
        }

        return null;
    }
}
