using Datum.Core.Algorithms;
using Datum.Core.Algorithms.BackgroundSubtractors;
using Datum.Core.Algorithms.Detectors;
using Datum.Core.Algorithms.Integrators;
using Datum.Core.Models;
using Datum.Core.Simulation;
using Xunit;

namespace Datum.Core.Tests;

public class AlgorithmTests
{
    private static ChromatogramBuilder NoiselessBuilder(IPeakModel peak, IBackground? background = null)
    {
        return new ChromatogramBuilder(
            peak,
            System.Array.Empty<IPeakModel>(),
            background ?? new NoBackground(),
            NoiseParameters.None,
            rtStart: peak.Center - 5.0 * peak.Sigma,
            rtEnd: peak.Center + 5.0 * peak.Sigma,
            resolution: 2000);
    }

    private static double[] Sample(ChromatogramBuilder builder, SamplingGrid grid) =>
        builder.SampleNoisy(grid.Rt, new RandomSource(1));

    [Fact]
    public void Trapezoid_on_densely_sampled_clean_peak_recovers_true_area()
    {
        var peak = new GaussianPeak(height: 100.0, center: 8.0, sigma: 0.3);
        var builder = NoiselessBuilder(peak);
        SamplingGrid grid = SamplingGrid.Create(peak, pointsAcrossPeak: 40, offset: 0.5);

        var pipeline = new QuantificationPipeline(
            new ThresholdDetector(), new NoBackgroundSubtractor(), new TrapezoidIntegrator(),
            DetectorParams.Default, IntegratorOptions.Default);
        double area = pipeline.Run(grid.Rt, Sample(builder, grid)).Area;

        Assert.True(System.Math.Abs(area - peak.TrueArea()) / peak.TrueArea() < 0.02);
    }

    [Fact]
    public void Edge_estimation_reduces_bias_at_low_sampling()
    {
        var peak = new GaussianPeak(height: 100.0, center: 8.0, sigma: 0.3);
        var builder = NoiselessBuilder(peak);
        SamplingGrid grid = SamplingGrid.Create(peak, pointsAcrossPeak: 4, offset: 0.5);
        double[] sample = Sample(builder, grid);

        var detector = new ThresholdDetector();
        var subtractor = new NoBackgroundSubtractor();
        var integrator = new TrapezoidIntegrator();

        double naive = new QuantificationPipeline(detector, subtractor, integrator,
            DetectorParams.Default, new IntegratorOptions(EdgeEstimation: false)).Run(grid.Rt, sample).Area;
        double withEdges = new QuantificationPipeline(detector, subtractor, integrator,
            DetectorParams.Default, new IntegratorOptions(EdgeEstimation: true)).Run(grid.Rt, sample).Area;

        double trueArea = peak.TrueArea();
        double naiveBias = System.Math.Abs(naive - trueArea);
        double edgeBias = System.Math.Abs(withEdges - trueArea);
        Assert.True(edgeBias < naiveBias, $"edge bias {edgeBias} should beat naive bias {naiveBias}");
    }

    [Fact]
    public void LinearBaseline_subtraction_removes_constant_background()
    {
        var peak = new GaussianPeak(height: 100.0, center: 8.0, sigma: 0.3);
        var withBg = NoiselessBuilder(peak, new ConstantBackground(20.0));
        SamplingGrid grid = SamplingGrid.Create(peak, pointsAcrossPeak: 30, offset: 0.5);

        var pipeline = new QuantificationPipeline(
            new ThresholdDetector(), new LinearBaselineSubtractor(), new TrapezoidIntegrator(),
            DetectorParams.Default, IntegratorOptions.Default);
        double area = pipeline.Run(grid.Rt, Sample(withBg, grid)).Area;

        // After removing the flat baseline, the recovered area should be close to the peak area.
        Assert.True(System.Math.Abs(area - peak.TrueArea()) / peak.TrueArea() < 0.05,
            $"recovered {area} vs true {peak.TrueArea()}");
    }

    [Fact]
    public void FindPeaks_detects_apex_near_true_center()
    {
        var peak = new GaussianPeak(height: 100.0, center: 8.0, sigma: 0.3);
        var builder = NoiselessBuilder(peak);
        SamplingGrid grid = SamplingGrid.Create(peak, pointsAcrossPeak: 20, offset: 0.5);

        PeakBounds bounds = new FindPeaksDetector().Detect(grid.Rt, Sample(builder, grid), DetectorParams.Default);
        Assert.True(bounds.Detected);
        Assert.True(System.Math.Abs(bounds.ApexRt - 8.0) < 0.3);
    }
}
