//! Riemann-sum area integration (sum of intensities times the sample spacing).

namespace Datum.Core.Algorithms.Integrators;

/// <summary>
/// Integrates the area as the sum of in-peak intensities multiplied by the sample spacing
/// (a left/midpoint Riemann sum). This is the naive "sum the points" approach; it
/// over-counts relative to the trapezoid rule and is included for comparison.
/// </summary>
public sealed class RiemannSumIntegrator : IIntegrator
{
    /// <inheritdoc/>
    public string Name => "Riemann sum";

    /// <inheritdoc/>
    public double Integrate(double[] rt, double[] intensity, PeakBounds bounds, IntegratorOptions options)
    {
        if (!bounds.Detected)
        {
            return 0.0;
        }

        double area = 0.0;
        int count = 0;
        for (int i = bounds.StartIndex; i <= bounds.EndIndex && i < rt.Length; i++)
        {
            if (i < 0)
            {
                continue;
            }

            count++;
        }

        if (count == 0)
        {
            return 0.0;
        }

        // Bin width from the reference span divided by the number of in-peak samples.
        double width = (bounds.EndRt - bounds.StartRt) / count;
        for (int i = bounds.StartIndex; i <= bounds.EndIndex && i < rt.Length; i++)
        {
            if (i < 0)
            {
                continue;
            }

            area += intensity[i] * width;
        }

        return area;
    }
}
