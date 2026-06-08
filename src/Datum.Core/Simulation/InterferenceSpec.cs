//! Co-eluting interference specified relative to the main peak.

using Datum.Core.Models;

namespace Datum.Core.Simulation;

/// <summary>
/// A co-eluting interference peak specified relative to the main peak, so it tracks the
/// main peak as parameters change. Converts to a concrete <see cref="IPeakModel"/> for synthesis.
/// </summary>
/// <param name="RelativeAmplitude">Apex height as a fraction of the main peak height (e.g. 0.3).</param>
/// <param name="OffsetSigma">Center offset from the main peak, in units of the main peak sigma.</param>
/// <param name="SigmaRatio">Interference sigma as a multiple of the main peak sigma.</param>
/// <param name="Skew">Skewness of the interference peak (0 = Gaussian).</param>
public readonly record struct InterferenceSpec(
    double RelativeAmplitude,
    double OffsetSigma,
    double SigmaRatio,
    double Skew)
{
    /// <summary>Build a concrete peak model placed relative to <paramref name="main"/>.</summary>
    public IPeakModel ToPeak(IPeakModel main)
    {
        double height = RelativeAmplitude * main.Evaluate(main.ApexRt);
        double center = main.Center + OffsetSigma * main.Sigma;
        double sigma = SigmaRatio * main.Sigma;
        return System.Math.Abs(Skew) < 1e-9
            ? new GaussianPeak(height, center, sigma)
            : new SkewNormalPeak(height, center, sigma, Skew);
    }
}
