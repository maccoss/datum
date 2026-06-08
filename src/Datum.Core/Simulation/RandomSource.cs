//! Seeded random source with Gaussian and Poisson draws for reproducible noise.

namespace Datum.Core.Simulation;

/// <summary>
/// A seedable pseudo-random source providing the draws needed for noise modeling:
/// uniform, Gaussian (Box-Muller), and Poisson (Knuth for small lambda, normal
/// approximation for large lambda). Seeding makes Monte-Carlo runs reproducible.
/// </summary>
public sealed class RandomSource
{
    private readonly System.Random _rng;
    private double? _spareGaussian;

    /// <summary>Create a source with an explicit seed for reproducibility.</summary>
    public RandomSource(int seed) => _rng = new System.Random(seed);

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextUniform() => _rng.NextDouble();

    /// <summary>Standard normal draw via the polar Box-Muller transform.</summary>
    public double NextGaussian()
    {
        if (_spareGaussian is { } spare)
        {
            _spareGaussian = null;
            return spare;
        }

        double u1;
        double u2;
        double s;
        do
        {
            u1 = 2.0 * _rng.NextDouble() - 1.0;
            u2 = 2.0 * _rng.NextDouble() - 1.0;
            s = u1 * u1 + u2 * u2;
        }
        while (s >= 1.0 || s == 0.0);

        double factor = System.Math.Sqrt(-2.0 * System.Math.Log(s) / s);
        _spareGaussian = u2 * factor;
        return u1 * factor;
    }

    /// <summary>Gaussian draw with given mean and standard deviation.</summary>
    public double NextGaussian(double mean, double stdDev) => mean + stdDev * NextGaussian();

    /// <summary>
    /// Poisson draw with rate <paramref name="lambda"/>. Uses Knuth's multiplicative
    /// algorithm for small lambda and a clamped normal approximation for large lambda
    /// (where Knuth would loop excessively). Models ion-counting (shot) noise.
    /// </summary>
    public long NextPoisson(double lambda)
    {
        if (lambda <= 0.0)
        {
            return 0L;
        }

        if (lambda < 30.0)
        {
            double l = System.Math.Exp(-lambda);
            long k = 0;
            double p = 1.0;
            do
            {
                k++;
                p *= _rng.NextDouble();
            }
            while (p > l);

            return k - 1;
        }

        // Normal approximation: Poisson(lambda) ~ N(lambda, lambda) for large lambda.
        double approx = NextGaussian(lambda, System.Math.Sqrt(lambda));
        return (long)System.Math.Max(0.0, System.Math.Round(approx));
    }
}
