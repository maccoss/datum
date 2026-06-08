//! Skew-normal peak shape (asymmetric tailing/fronting via a skewness parameter).

using Datum.Core.Math;

namespace Datum.Core.Models;

/// <summary>
/// A skew-normal peak. Ports the reference notebook's <c>skewed_peak</c>: the location
/// parameter is shifted so the distribution mean equals the requested <see cref="Center"/>,
/// and the PDF is rescaled so the apex intensity equals <c>height</c>.
/// </summary>
/// <remarks>
/// Positive <c>skew</c> produces a right-tailed peak (chromatographic tailing); negative
/// <c>skew</c> produces fronting. The underlying density is
/// <c>(2/sigma) * phi(z) * Phi(skew * z)</c> with <c>z = (rt - loc) / sigma</c>.
/// </remarks>
public sealed class SkewNormalPeak : PeakModelBase
{
    private readonly double _loc;
    private readonly double _peakPdf;

    /// <summary>Initialize a skew-normal peak.</summary>
    /// <param name="height">Peak amplitude at the apex.</param>
    /// <param name="center">Desired center of mass (distribution mean) in retention-time units.</param>
    /// <param name="sigma">Scale parameter (standard deviation of the underlying Gaussian).</param>
    /// <param name="skew">Skewness; positive = right tail, negative = left tail, zero = Gaussian.</param>
    public SkewNormalPeak(double height, double center, double sigma, double skew)
    {
        if (sigma <= 0.0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(sigma), "sigma must be positive.");
        }

        Height = height;
        Center = center;
        Sigma = sigma;
        Skew = skew;

        // Shift loc so the distribution mean lands on `center` (notebook cell 3).
        double delta = skew / System.Math.Sqrt(1.0 + skew * skew);
        double meanOffset = delta * sigma * System.Math.Sqrt(2.0 / System.Math.PI);
        _loc = center - meanOffset;

        _peakPdf = FindMaxPdf();
    }

    /// <summary>Peak amplitude at the apex.</summary>
    public double Height { get; }

    /// <inheritdoc/>
    public override double Center { get; }

    /// <inheritdoc/>
    public override double Sigma { get; }

    /// <summary>Skewness parameter; positive values produce a right tail.</summary>
    public double Skew { get; }

    // Skew compresses the spread, so a window relative to loc generously covers both tails.
    /// <inheritdoc/>
    protected override double IntegrationStart => _loc - 8.0 * Sigma;

    /// <inheritdoc/>
    protected override double IntegrationEnd => _loc + 8.0 * Sigma;

    /// <inheritdoc/>
    public override double Evaluate(double rt) => Height * RawPdf(rt) / _peakPdf;

    private double RawPdf(double rt)
    {
        double z = (rt - _loc) / Sigma;
        return (2.0 / Sigma) * MathFunctions.NormalPdf(z) * MathFunctions.NormalCdf(Skew * z);
    }

    private double FindMaxPdf()
    {
        double a = _loc - 8.0 * Sigma;
        double b = _loc + 8.0 * Sigma;
        double dx = (b - a) / (IntegrationPoints - 1);
        double max = 0.0;
        for (int i = 0; i < IntegrationPoints; i++)
        {
            double val = RawPdf(a + i * dx);
            if (val > max)
            {
                max = val;
            }
        }

        return max;
    }
}
