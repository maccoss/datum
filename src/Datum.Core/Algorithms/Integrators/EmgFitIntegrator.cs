//! EMG-fit integrator: Levenberg-Marquardt fit of an exponentially-modified Gaussian.

namespace Datum.Core.Algorithms.Integrators;

/// <summary>
/// Fits an exponentially-modified Gaussian (EMG) to the in-peak samples by Levenberg-Marquardt
/// and reports the analytic area of the fitted model. The EMG captures chromatographic tailing,
/// so it recovers area more faithfully than a symmetric Gaussian fit on skewed peaks.
/// </summary>
/// <remarks>
/// The model is an <em>area-normalized</em> EMG density scaled by an amplitude A:
/// <c>y(t) = A * g(t; mu, sigma, tau)</c> where g integrates to 1, so the fitted <c>A</c> is itself
/// the peak area (see <see cref="EmgFit"/>). Reports NA when the fit is underdetermined or unusable.
/// Each transition is fitted independently, so interference in a transition biases its own area; the
/// <see cref="ConsensusEmgIntegrator"/> shares one shape across transitions to resist that.
/// </remarks>
public sealed class EmgFitIntegrator : IIntegrator
{
    /// <inheritdoc/>
    public string Name => "EMG fit";

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
            if (i >= 0)
            {
                xs.Add(rt[i]);
                ys.Add(intensity[i]);
            }
        }

        EmgFit.Result? fit = EmgFit.Fit(xs.ToArray(), ys.ToArray());
        return fit?.Area ?? double.NaN;
    }
}
