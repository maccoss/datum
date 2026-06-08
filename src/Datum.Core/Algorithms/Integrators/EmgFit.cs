//! Shared exponentially-modified Gaussian (EMG) model and Levenberg-Marquardt fit, used by the
//! per-transition EMG-fit integrator and the median-shape consensus integrator.

using Datum.Core.Math;

namespace Datum.Core.Algorithms.Integrators;

/// <summary>
/// The area-normalized EMG density and a Levenberg-Marquardt fit of it to sampled points. The model
/// is <c>y(t) = A * g(t; mu, sigma, tau)</c> where <c>g</c> integrates to 1, so the fitted amplitude
/// <c>A</c> is itself the peak area. The density uses the same numerically stable factorization as
/// <c>EmgPeak</c>.
/// </summary>
internal static class EmgFit
{
    /// <summary>Result of a successful fit: the analytic area and the shape parameters.</summary>
    public readonly record struct Result(double Area, double Mu, double Sigma, double Tau);

    /// <summary>
    /// Fit an area-normalized EMG to the points <paramref name="x"/>, <paramref name="y"/> by
    /// Levenberg-Marquardt. Returns null if there are too few points, the fit fails to converge to a
    /// usable shape, or the recovered area is non-finite, non-positive, or wildly inflated relative to
    /// the trapezoid estimate (the caller then reports NA).
    /// </summary>
    public static Result? Fit(double[] x, double[] y)
    {
        // A 4-parameter EMG needs enough points to be well-conditioned.
        if (x.Length < 5)
        {
            return null;
        }

        // Initial guess from data moments and a trapezoid area estimate.
        double total = 0.0, weightedMean = 0.0, max = 0.0;
        int apexIdx = 0;
        for (int i = 0; i < x.Length; i++)
        {
            total += y[i];
            weightedMean += x[i] * y[i];
            if (y[i] > max)
            {
                max = y[i];
                apexIdx = i;
            }
        }

        if (total <= 0.0)
        {
            return null;
        }

        weightedMean /= total;
        double variance = 0.0;
        for (int i = 0; i < x.Length; i++)
        {
            double d = x[i] - weightedMean;
            variance += y[i] * d * d;
        }

        variance /= total;
        double spread = System.Math.Sqrt(System.Math.Max(variance, 1e-6));
        double reference = TrapezoidIntegrator.Trapezoid(x, y);
        double area0 = System.Math.Max(1e-6, reference);
        double[] initial = { area0, x[apexIdx], spread * 0.7, spread * 0.5 };

        double[] fit = LevenbergMarquardt.Fit(x, y, initial, Model, maxIterations: 80);
        double area = fit[0];
        double sigma = System.Math.Abs(fit[2]);
        double tau = System.Math.Abs(fit[3]);

        // Reject non-finite, non-positive, or wildly inflated fits.
        if (!double.IsFinite(area) || area <= 0.0 || sigma <= 0.0 || tau <= 0.0
            || (reference > 0.0 && area > 10.0 * reference))
        {
            return null;
        }

        return new Result(area, fit[1], sigma, tau);
    }

    /// <summary>Area-normalized EMG density scaled by the amplitude parameter (parameters[0]).</summary>
    public static double Model(double[] parameters, double rt)
    {
        double amplitude = parameters[0];
        double mu = parameters[1];
        double sigma = System.Math.Abs(parameters[2]);
        double tau = System.Math.Abs(parameters[3]);
        if (sigma < 1e-9 || tau < 1e-9)
        {
            return 0.0;
        }

        double u = (rt - mu) / sigma;
        double k = sigma / tau;
        double z = (k - u) / System.Math.Sqrt(2.0);
        double raw = z >= 0.0
            ? System.Math.Exp(-0.5 * u * u) * MathFunctions.Erfcx(z)
            : System.Math.Exp(0.5 * k * k - u * k) * MathFunctions.Erfc(z);
        return amplitude * raw / (2.0 * tau);
    }

    /// <summary>The unit-area EMG density (amplitude 1) at <paramref name="rt"/> for a fixed shape.</summary>
    public static double UnitDensity(double mu, double sigma, double tau, double rt) =>
        Model(new[] { 1.0, mu, sigma, tau }, rt);

    /// <summary>
    /// Retention times where the EMG density falls to <paramref name="fraction"/> of its peak height,
    /// on the leading and trailing side of the mode. For a tailed peak the trailing crossing is
    /// farther from the mode than the leading one, which is what makes the integration boundary
    /// asymmetric. Found on a fine analytic grid (the model is noise-free), so the placement is stable
    /// even far down the tail. The grid spans the leading Gaussian shoulder and a trailing extent that
    /// grows with the exponential time constant and how far down the threshold sits.
    /// </summary>
    public static (double Left, double Right) HeightCrossings(double mu, double sigma, double tau, double fraction)
    {
        double lo = mu - 8.0 * sigma;
        double hi = mu + 8.0 * sigma + tau * (System.Math.Max(0.0, -System.Math.Log(fraction)) + 2.0);
        const int steps = 1000;
        double dt = (hi - lo) / steps;

        double peakHeight = 0.0;
        int peakIdx = 0;
        var g = new double[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            g[i] = UnitDensity(mu, sigma, tau, lo + i * dt);
            if (g[i] > peakHeight)
            {
                peakHeight = g[i];
                peakIdx = i;
            }
        }

        double target = fraction * peakHeight;

        double left = lo;
        for (int i = peakIdx; i > 0; i--)
        {
            if (g[i] >= target && g[i - 1] < target)
            {
                double f = (target - g[i - 1]) / (g[i] - g[i - 1]);
                left = lo + (i - 1 + f) * dt;
                break;
            }
        }

        double right = hi;
        for (int i = peakIdx; i < steps; i++)
        {
            if (g[i] >= target && g[i + 1] < target)
            {
                double f = (g[i] - target) / (g[i] - g[i + 1]);
                right = lo + (i + f) * dt;
                break;
            }
        }

        return (left, right);
    }
}
