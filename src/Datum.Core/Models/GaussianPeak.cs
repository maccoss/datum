//! Symmetric Gaussian peak shape.

namespace Datum.Core.Models;

/// <summary>
/// A symmetric Gaussian peak: <c>height * exp(-0.5 * ((rt - center) / sigma)^2)</c>.
/// Mirrors the <c>gaussian_peak</c> function from the reference notebook.
/// </summary>
public sealed class GaussianPeak : PeakModelBase
{
    /// <summary>Initialize a Gaussian peak.</summary>
    /// <param name="height">Peak amplitude at the apex.</param>
    /// <param name="center">Retention time of the apex (the mean).</param>
    /// <param name="sigma">Standard deviation (width) in retention-time units.</param>
    public GaussianPeak(double height, double center, double sigma)
    {
        if (sigma <= 0.0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(sigma), "sigma must be positive.");
        }

        Height = height;
        Center = center;
        Sigma = sigma;
    }

    /// <summary>Peak amplitude at the apex.</summary>
    public double Height { get; }

    /// <inheritdoc/>
    public override double Center { get; }

    /// <inheritdoc/>
    public override double Sigma { get; }

    /// <inheritdoc/>
    public override double ApexRt => Center;

    /// <inheritdoc/>
    public override double Evaluate(double rt)
    {
        double z = (rt - Center) / Sigma;
        return Height * System.Math.Exp(-0.5 * z * z);
    }

    /// <summary>Analytic Gaussian area, <c>height * sigma * sqrt(2*pi)</c>.</summary>
    public override double TrueArea() => Height * Sigma * System.Math.Sqrt(2.0 * System.Math.PI);
}
