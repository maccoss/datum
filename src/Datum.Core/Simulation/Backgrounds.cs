//! Chemical background (baseline) models added beneath the peak.

namespace Datum.Core.Simulation;

/// <summary>A baseline intensity added beneath the signal as a function of retention time.</summary>
public interface IBackground
{
    /// <summary>Baseline intensity at the given retention time.</summary>
    double Evaluate(double rt);
}

/// <summary>No background (flat zero baseline).</summary>
public sealed class NoBackground : IBackground
{
    /// <inheritdoc/>
    public double Evaluate(double rt) => 0.0;
}

/// <summary>Constant (flat) chemical background, as in the reference notebook.</summary>
public sealed class ConstantBackground(double level) : IBackground
{
    /// <summary>The constant baseline level.</summary>
    public double Level { get; } = level;

    /// <inheritdoc/>
    public double Evaluate(double rt) => Level;
}

/// <summary>
/// Linear (sloping) baseline: <c>intercept + slope * (rt - referenceRt)</c>.
/// <paramref name="referenceRt"/> anchors the intercept (typically the peak center).
/// </summary>
public sealed class LinearBackground(double intercept, double slope, double referenceRt) : IBackground
{
    /// <summary>Baseline value at the reference retention time.</summary>
    public double Intercept { get; } = intercept;

    /// <summary>Baseline slope per retention-time unit.</summary>
    public double Slope { get; } = slope;

    /// <summary>Retention time at which the baseline equals <see cref="Intercept"/>.</summary>
    public double ReferenceRt { get; } = referenceRt;

    /// <inheritdoc/>
    public double Evaluate(double rt) => Intercept + Slope * (rt - ReferenceRt);
}

/// <summary>
/// Curved (quadratic) baseline: <c>intercept + slope*d + curvature*d^2</c> with
/// <c>d = rt - referenceRt</c>. Models a rolling chemical background.
/// </summary>
public sealed class CurvedBackground(double intercept, double slope, double curvature, double referenceRt) : IBackground
{
    /// <summary>Baseline value at the reference retention time.</summary>
    public double Intercept { get; } = intercept;

    /// <summary>Linear baseline coefficient.</summary>
    public double Slope { get; } = slope;

    /// <summary>Quadratic baseline coefficient.</summary>
    public double Curvature { get; } = curvature;

    /// <summary>Retention time at which the baseline equals <see cref="Intercept"/>.</summary>
    public double ReferenceRt { get; } = referenceRt;

    /// <inheritdoc/>
    public double Evaluate(double rt)
    {
        double d = rt - ReferenceRt;
        return Intercept + Slope * d + Curvature * d * d;
    }
}
