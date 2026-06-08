//! Ground-truth peak shape abstraction.

namespace Datum.Core.Models;

/// <summary>
/// A noise-free, analytic chromatographic peak shape evaluated over retention time.
/// Implementations are the "ground truth" that integration algorithms are scored against.
/// </summary>
public interface IPeakModel
{
    /// <summary>Nominal center of the peak in retention-time units (the Gaussian mean).</summary>
    double Center { get; }

    /// <summary>Width parameter (Gaussian standard deviation) in retention-time units.</summary>
    double Sigma { get; }

    /// <summary>Retention time of the intensity maximum (may differ from <see cref="Center"/> when skewed/tailed).</summary>
    double ApexRt { get; }

    /// <summary>Evaluate the noise-free intensity at a given retention time.</summary>
    /// <param name="rt">Retention time.</param>
    /// <returns>Intensity (non-negative).</returns>
    double Evaluate(double rt);

    /// <summary>
    /// The true integrated area of the noise-free peak, used as the quantification ground truth.
    /// </summary>
    double TrueArea();
}
