//! Gaussian-fit integrator using Guo's weighted parabolic (log-domain) fit.

namespace Datum.Core.Algorithms.Integrators;

/// <summary>
/// Fits a Gaussian to the in-peak samples and reports its analytic area
/// (<c>amplitude * sigma * sqrt(2*pi)</c>). The fit uses Guo's weighted parabola in the log
/// domain: fitting <c>ln y = a + b·x + c·x^2</c> with weights <c>y^2</c> recovers the
/// Gaussian parameters in closed form, which is robust and needs no iteration. Because it
/// extrapolates the analytic tails, it is less sensitive to truncated boundaries than the
/// trapezoid rule. Falls back to a trapezoid sum when a Gaussian cannot be fit.
/// </summary>
public sealed class GaussianFitIntegrator : IIntegrator
{
    /// <inheritdoc/>
    public string Name => "Gaussian fit";

    /// <inheritdoc/>
    public double Integrate(double[] rt, double[] intensity, PeakBounds bounds, IntegratorOptions options)
    {
        if (!bounds.Detected)
        {
            return 0.0;
        }

        var xs = new List<double>();
        var ys = new List<double>();
        for (int i = bounds.StartIndex; i <= bounds.EndIndex && i < rt.Length; i++)
        {
            if (i >= 0 && intensity[i] > 0.0)
            {
                xs.Add(rt[i]);
                ys.Add(intensity[i]);
            }
        }

        // A 3-parameter Gaussian needs enough well-conditioned points; with too few the fit is
        // unstable and can yield a near-zero curvature and an absurd area. Report NaN ("NA") in
        // those cases rather than a meaningless number.
        if (xs.Count < 4 || !TryFit(xs, ys, out double amplitude, out double sigma))
        {
            return double.NaN;
        }

        double area = amplitude * sigma * System.Math.Sqrt(2.0 * System.Math.PI);
        double reference = TrapezoidIntegrator.Trapezoid(xs, ys);

        // Reject non-finite or wildly inflated fits (a sane Gaussian fit is at most modestly
        // larger than the trapezoid of the sampled points).
        if (!double.IsFinite(area) || area <= 0.0 || (reference > 0.0 && area > 10.0 * reference))
        {
            return double.NaN;
        }

        return area;
    }

    /// <summary>Guo's weighted (w = y^2) parabolic fit of ln(y) vs x; returns Gaussian amplitude and sigma.</summary>
    private static bool TryFit(IReadOnlyList<double> x, IReadOnlyList<double> y, out double amplitude, out double sigma)
    {
        amplitude = 0.0;
        sigma = 0.0;

        // Weighted normal equations for [a, b, c] against basis [1, x, x^2], weight w = y^2.
        double s0 = 0, s1 = 0, s2 = 0, s3 = 0, s4 = 0;
        double t0 = 0, t1 = 0, t2 = 0;
        for (int i = 0; i < x.Count; i++)
        {
            double xi = x[i];
            double w = y[i] * y[i];
            double lny = System.Math.Log(y[i]);
            double x2 = xi * xi;
            s0 += w;
            s1 += w * xi;
            s2 += w * x2;
            s3 += w * x2 * xi;
            s4 += w * x2 * x2;
            t0 += w * lny;
            t1 += w * xi * lny;
            t2 += w * x2 * lny;
        }

        double[,] m =
        {
            { s0, s1, s2 },
            { s1, s2, s3 },
            { s2, s3, s4 },
        };
        double[] rhs = { t0, t1, t2 };

        if (!Solve3(m, rhs, out double a, out double b, out double c) || c >= 0.0)
        {
            return false;
        }

        sigma = System.Math.Sqrt(-1.0 / (2.0 * c));
        double mu = -b / (2.0 * c);
        amplitude = System.Math.Exp(a - c * mu * mu);
        return double.IsFinite(amplitude) && double.IsFinite(sigma) && sigma > 0.0;
    }

    private static bool Solve3(double[,] m, double[] r, out double x0, out double x1, out double x2)
    {
        x0 = x1 = x2 = 0.0;
        double det =
            m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
            - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
            + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);

        if (System.Math.Abs(det) < 1e-18)
        {
            return false;
        }

        double inv = 1.0 / det;
        x0 = inv * (r[0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
            - m[0, 1] * (r[1] * m[2, 2] - m[1, 2] * r[2])
            + m[0, 2] * (r[1] * m[2, 1] - m[1, 1] * r[2]));
        x1 = inv * (m[0, 0] * (r[1] * m[2, 2] - m[1, 2] * r[2])
            - r[0] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
            + m[0, 2] * (m[1, 0] * r[2] - r[1] * m[2, 0]));
        x2 = inv * (m[0, 0] * (m[1, 1] * r[2] - r[1] * m[2, 1])
            - m[0, 1] * (m[1, 0] * r[2] - r[1] * m[2, 0])
            + r[0] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]));
        return true;
    }
}
