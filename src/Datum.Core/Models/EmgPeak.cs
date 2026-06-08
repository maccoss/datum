//! Exponentially-modified Gaussian (EMG) peak shape for chromatographic tailing.

using Datum.Core.Math;

namespace Datum.Core.Models;

/// <summary>
/// An exponentially-modified Gaussian peak (Gaussian convolved with a one-sided
/// exponential), the canonical model for chromatographic tailing. Parameterized by the
/// Gaussian center <c>mu</c>, Gaussian width <c>sigma</c>, and the exponential time
/// constant <c>tau</c> (larger tau = stronger right tail). The shape is rescaled so the
/// apex intensity equals <c>height</c>.
/// </summary>
/// <remarks>
/// Evaluated with a numerically stable factorization. With <c>u = (rt - mu) / sigma</c>,
/// <c>k = sigma / tau</c>, and <c>z = (k - u) / sqrt(2)</c>:
/// for <c>z &gt;= 0</c> the density is <c>exp(-u^2/2) * erfcx(z)</c> (avoids exp(+large)),
/// for <c>z &lt; 0</c> it is <c>exp(k^2/2 - u*k) * erfc(z)</c>. See Kalambet et al. 2011.
/// </remarks>
public sealed class EmgPeak : PeakModelBase
{
    private readonly double _peakValue;

    /// <summary>Initialize an EMG peak.</summary>
    /// <param name="height">Peak amplitude at the apex.</param>
    /// <param name="center">Gaussian center <c>mu</c> (the apex sits slightly to the right).</param>
    /// <param name="sigma">Gaussian standard deviation.</param>
    /// <param name="tau">Exponential decay time constant; must be positive.</param>
    public EmgPeak(double height, double center, double sigma, double tau)
    {
        if (sigma <= 0.0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(sigma), "sigma must be positive.");
        }

        if (tau <= 0.0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(tau), "tau must be positive.");
        }

        Height = height;
        Center = center;
        Sigma = sigma;
        Tau = tau;

        _peakValue = FindMaxRaw();
    }

    /// <summary>Peak amplitude at the apex.</summary>
    public double Height { get; }

    /// <inheritdoc/>
    public override double Center { get; }

    /// <inheritdoc/>
    public override double Sigma { get; }

    /// <summary>Exponential decay time constant controlling the right tail.</summary>
    public double Tau { get; }

    // Tail extends to the right; include several tau in the integration window.
    /// <inheritdoc/>
    protected override double IntegrationStart => Center - 5.0 * Sigma;

    /// <inheritdoc/>
    protected override double IntegrationEnd => Center + 5.0 * Sigma + 10.0 * Tau;

    /// <inheritdoc/>
    public override double Evaluate(double rt) => Height * RawDensity(rt) / _peakValue;

    private double RawDensity(double rt)
    {
        double u = (rt - Center) / Sigma;
        double k = Sigma / Tau;
        double z = (k - u) / System.Math.Sqrt(2.0);
        return z >= 0.0
            ? System.Math.Exp(-0.5 * u * u) * MathFunctions.Erfcx(z)
            : System.Math.Exp(0.5 * k * k - u * k) * MathFunctions.Erfc(z);
    }

    private double FindMaxRaw()
    {
        double a = IntegrationStart;
        double b = IntegrationEnd;
        double dx = (b - a) / (IntegrationPoints - 1);
        double max = 0.0;
        for (int i = 0; i < IntegrationPoints; i++)
        {
            double val = RawDensity(a + i * dx);
            if (val > max)
            {
                max = val;
            }
        }

        return max;
    }
}
