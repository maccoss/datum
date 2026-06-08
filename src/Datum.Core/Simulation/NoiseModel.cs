//! Additive Gaussian (signal-independent) and Poisson (signal-dependent) noise.

namespace Datum.Core.Simulation;

/// <summary>
/// Noise configuration. <see cref="GaussianSigma"/> is the standard deviation of additive,
/// signal-independent (white) noise. <see cref="UsePoisson"/> enables signal-dependent
/// shot noise by resampling each point from a Poisson distribution.
/// </summary>
/// <param name="GaussianSigma">Standard deviation of additive Gaussian noise (0 disables it).</param>
/// <param name="UsePoisson">Whether to apply Poisson (shot) resampling.</param>
public readonly record struct NoiseParameters(double GaussianSigma, bool UsePoisson)
{
    /// <summary>A configuration with no added noise.</summary>
    public static NoiseParameters None => new(0.0, false);
}

/// <summary>
/// Applies background and noise to a clean signal, in the same order as the reference
/// notebook's <c>add_noise</c>: add background, add Gaussian noise, Poisson-resample,
/// then clamp to non-negative.
/// </summary>
public static class NoiseModel
{
    /// <summary>
    /// Produce a noisy copy of <paramref name="clean"/> at the given retention times.
    /// </summary>
    /// <param name="rt">Retention-time grid.</param>
    /// <param name="clean">Clean signal (peak + interference), without background.</param>
    /// <param name="background">Chemical background to add beneath the signal.</param>
    /// <param name="noise">Noise parameters.</param>
    /// <param name="rng">Seeded random source.</param>
    /// <returns>A new array of noisy intensities.</returns>
    public static double[] Apply(
        double[] rt,
        double[] clean,
        IBackground background,
        NoiseParameters noise,
        RandomSource rng)
    {
        var noisy = new double[clean.Length];
        for (int i = 0; i < clean.Length; i++)
        {
            double value = clean[i] + background.Evaluate(rt[i]);

            if (noise.GaussianSigma > 0.0)
            {
                value += rng.NextGaussian(0.0, noise.GaussianSigma);
            }

            if (noise.UsePoisson && value > 0.0)
            {
                // Poisson resampling replaces the value (it does not add to it).
                value = rng.NextPoisson(value);
            }

            noisy[i] = System.Math.Max(0.0, value);
        }

        return noisy;
    }
}
