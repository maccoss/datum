using Datum.Core.Algorithms;
using Datum.Core.Algorithms.BackgroundSubtractors;
using Datum.Core.Algorithms.Detectors;
using Datum.Core.Algorithms.Integrators;
using Datum.Core.Models;
using Datum.Core.Simulation;
using Xunit;

namespace Datum.Core.Tests;

public class SimulationTests
{
    [Fact]
    public void Noise_is_reproducible_under_a_fixed_seed()
    {
        var rt = new double[] { 0, 1, 2, 3, 4 };
        var clean = new double[] { 0, 50, 100, 50, 0 };
        var noise = new NoiseParameters(GaussianSigma: 5.0, UsePoisson: true);

        double[] a = NoiseModel.Apply(rt, clean, new NoBackground(), noise, new RandomSource(42));
        double[] b = NoiseModel.Apply(rt, clean, new NoBackground(), noise, new RandomSource(42));
        double[] c = NoiseModel.Apply(rt, clean, new NoBackground(), noise, new RandomSource(43));

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Poisson_draws_have_mean_and_variance_near_lambda()
    {
        var rng = new RandomSource(7);
        const double lambda = 16.0;
        const int n = 50000;
        double sum = 0.0;
        double sumSq = 0.0;
        for (int i = 0; i < n; i++)
        {
            long k = rng.NextPoisson(lambda);
            sum += k;
            sumSq += (double)k * k;
        }

        double mean = sum / n;
        double variance = sumSq / n - mean * mean;
        Assert.Equal(lambda, mean, 0); // within 0.5
        Assert.True(System.Math.Abs(variance - lambda) < 1.0);
    }

    [Fact]
    public void Deviation_approaches_zero_as_points_increase_on_a_clean_peak()
    {
        var peak = new GaussianPeak(height: 100.0, center: 8.0, sigma: 0.3);
        var builder = new ChromatogramBuilder(
            peak, System.Array.Empty<IPeakModel>(), new NoBackground(), NoiseParameters.None,
            rtStart: 6.0, rtEnd: 10.0, resolution: 2000);
        var pipeline = new QuantificationPipeline(
            new ThresholdDetector(), new NoBackgroundSubtractor(), new TrapezoidIntegrator(),
            DetectorParams.Default, IntegratorOptions.Default);

        var results = new SimulationEngine().Run(
            builder, pipeline, new SimulationSettings(MinPoints: 2, MaxPoints: 30, Iterations: 20));

        DeviationResult few = results[0];
        DeviationResult many = results[^1];
        Assert.True(System.Math.Abs(many.PercentDeviation) < System.Math.Abs(few.PercentDeviation));
        Assert.True(System.Math.Abs(many.PercentDeviation) < 3.0);
    }

    [Fact]
    public void Precision_improves_with_more_points_under_noise()
    {
        var peak = new GaussianPeak(height: 1000.0, center: 8.0, sigma: 0.3);
        var builder = new ChromatogramBuilder(
            peak, System.Array.Empty<IPeakModel>(), new NoBackground(),
            new NoiseParameters(GaussianSigma: 30.0, UsePoisson: false),
            rtStart: 6.0, rtEnd: 10.0, resolution: 2000);
        var pipeline = new QuantificationPipeline(
            new ThresholdDetector(), new NoBackgroundSubtractor(), new TrapezoidIntegrator(),
            DetectorParams.Default, IntegratorOptions.Default);

        var results = new SimulationEngine().Run(
            builder, pipeline, new SimulationSettings(MinPoints: 3, MaxPoints: 25, Iterations: 80));

        DeviationResult few = results[0];
        DeviationResult many = results[^1];
        Assert.True(many.PercentStd < few.PercentStd, $"std at many ({many.PercentStd}) should beat few ({few.PercentStd})");
    }

    [Fact]
    public void Mexican_hat_kernel_is_zero_mean_and_symmetric()
    {
        double[] kernel = Datum.Core.Algorithms.Cwt.CwtMath.MexicanHatKernel(sigma: 4.0, kernelRadius: 20);
        double mean = 0.0;
        foreach (double v in kernel)
        {
            mean += v;
        }

        mean /= kernel.Length;
        Assert.True(System.Math.Abs(mean) < 1e-9);

        for (int i = 0; i < kernel.Length / 2; i++)
        {
            Assert.Equal(kernel[i], kernel[kernel.Length - 1 - i], 9);
        }
    }
}
