//! Trapezoidal area integration with optional fractional-trapezoid edge estimation.

namespace Datum.Core.Algorithms.Integrators;

/// <summary>
/// Integrates the area under the detected peak with the trapezoidal rule over the in-peak
/// samples. When <see cref="IntegratorOptions.EdgeEstimation"/> is enabled, interpolated
/// points are inserted at the exact start/end boundaries (the "fractional trapezoid" that
/// fills each edge), which removes the systematic under-integration seen at low sampling.
/// Ports the reference notebook's trapezoid + boundary-interpolation logic.
/// </summary>
public sealed class TrapezoidIntegrator : IIntegrator
{
    /// <inheritdoc/>
    public string Name => "Trapezoid";

    /// <inheritdoc/>
    public bool SupportsEdgeEstimation => true;

    /// <inheritdoc/>
    public double Integrate(double[] rt, double[] intensity, PeakBounds bounds, IntegratorOptions options)
    {
        if (!bounds.Detected)
        {
            return 0.0;
        }

        (double[] xs, double[] ys) = IntegrationGeometry.InPeakPoints(rt, intensity, bounds, options.EdgeEstimation);
        return Trapezoid(xs, ys);
    }

    /// <summary>Trapezoidal integration of y sampled at x.</summary>
    internal static double Trapezoid(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        double area = 0.0;
        for (int i = 1; i < x.Count; i++)
        {
            area += 0.5 * (y[i] + y[i - 1]) * (x[i] - x[i - 1]);
        }

        return area;
    }
}
