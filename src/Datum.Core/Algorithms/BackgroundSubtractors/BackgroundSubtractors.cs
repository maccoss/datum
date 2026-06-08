//! Background subtraction strategies applied before integration.

namespace Datum.Core.Algorithms.BackgroundSubtractors;

/// <summary>No background subtraction; returns the trace unchanged.</summary>
public sealed class NoBackgroundSubtractor : IBackgroundSubtractor
{
    /// <inheritdoc/>
    public string Name => "None";

    /// <inheritdoc/>
    public double[] Subtract(double[] rt, double[] intensity, PeakBounds bounds) => intensity;
}

/// <summary>
/// Subtracts a constant baseline equal to the lower of the two boundary intensities. Uses only the
/// peak's own boundary points (not the flanking samples), so it is unaffected by interference
/// outside the peak.
/// </summary>
public sealed class ConstantBackgroundSubtractor : IBackgroundSubtractor
{
    /// <inheritdoc/>
    public string Name => "Constant";

    /// <inheritdoc/>
    public double[] Subtract(double[] rt, double[] intensity, PeakBounds bounds)
    {
        if (!bounds.Detected)
        {
            return intensity;
        }

        double startValue = Simulation.Chromatogram.Interpolate(rt, intensity, bounds.StartRt);
        double endValue = Simulation.Chromatogram.Interpolate(rt, intensity, bounds.EndRt);
        double baseline = System.Math.Min(startValue, endValue);

        var result = new double[intensity.Length];
        for (int i = 0; i < intensity.Length; i++)
        {
            result[i] = System.Math.Max(0.0, intensity[i] - baseline);
        }

        return result;
    }
}

/// <summary>
/// Subtracts a straight baseline drawn between the intensities at the two detected boundaries
/// (the reference notebook's, and Skyline's, linear background). It uses only the peak's own
/// boundary points, so interference in the flanks does not affect it; it does assume the
/// boundaries sit near the true baseline (wide detectors), and will over-subtract if the
/// boundaries are placed well above baseline.
/// </summary>
public sealed class LinearBaselineSubtractor : IBackgroundSubtractor
{
    /// <inheritdoc/>
    public string Name => "Linear baseline";

    /// <inheritdoc/>
    public double[] Subtract(double[] rt, double[] intensity, PeakBounds bounds)
    {
        if (!bounds.Detected)
        {
            return intensity;
        }

        double startValue = Simulation.Chromatogram.Interpolate(rt, intensity, bounds.StartRt);
        double endValue = Simulation.Chromatogram.Interpolate(rt, intensity, bounds.EndRt);
        double span = bounds.EndRt - bounds.StartRt;

        var result = new double[intensity.Length];
        for (int i = 0; i < intensity.Length; i++)
        {
            double baseline = System.Math.Abs(span) < 1e-12
                ? startValue
                : startValue + (endValue - startValue) * (rt[i] - bounds.StartRt) / span;
            result[i] = System.Math.Max(0.0, intensity[i] - baseline);
        }

        return result;
    }
}
