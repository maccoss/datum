using Datum.Core.Algorithms;
using Datum.Core.Algorithms.BackgroundSubtractors;
using Datum.Core.Algorithms.Detectors;
using Datum.Core.Algorithms.Integrators;
using Datum.Core.Models;
using Datum.Core.Simulation;
using Datum.Skyline;
using Xunit;

namespace Datum.Core.Tests;

public class CoverageTests
{
    private static (double[] Rt, double[] Y) SampleClean(IPeakModel peak, int points, double boundaryRel = 0.99)
    {
        var builder = new ChromatogramBuilder(
            peak, System.Array.Empty<IPeakModel>(), new NoBackground(), NoiseParameters.None,
            peak.Center - 6 * peak.Sigma, peak.ApexRt + 8 * peak.Sigma, 2000);
        var grid = SamplingGrid.Create(peak, points, 0.5, 1.0 - boundaryRel);
        return (grid.Rt, builder.SampleNoisy(grid.Rt, new RandomSource(1)));
    }

    [Fact]
    public void Sweep_is_deterministic_across_runs_despite_parallelism()
    {
        var peak = new GaussianPeak(1000, 8.0, 0.3);
        var builder = new ChromatogramBuilder(
            peak, System.Array.Empty<IPeakModel>(), new NoBackground(),
            new NoiseParameters(30, true), 6, 10, 2000);
        var pipeline = new QuantificationPipeline(
            new ThresholdDetector(), new NoBackgroundSubtractor(), new TrapezoidIntegrator(),
            DetectorParams.Default, IntegratorOptions.Default);
        var settings = new SimulationSettings(2, 20, 60, 99);

        var a = new SimulationEngine().Run(builder, pipeline, settings);
        var b = new SimulationEngine().Run(builder, pipeline, settings);

        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].MeanArea, b[i].MeanArea, 12);
            Assert.Equal(a[i].PercentStd, b[i].PercentStd, 12);
        }
    }

    [Fact]
    public void SamplingGrid_puts_requested_count_within_the_reference_width()
    {
        var peak = new GaussianPeak(1000, 8.0, 0.3);
        (double start, double end) = SamplingGrid.ReferenceBounds(peak, 0.01);
        var grid = SamplingGrid.Create(peak, 4, 0.5, 0.01);

        int inside = 0;
        foreach (double t in grid.Rt)
        {
            if (t >= start && t <= end)
            {
                inside++;
            }
        }

        Assert.Equal(4, inside);
        Assert.Equal((end - start) / 4.0, grid.Interval, 6);
    }

    [Fact]
    public void Detector_and_integrator_capability_flags_are_correct()
    {
        Assert.True(((IPeakDetector)new FindPeaksDetector()).ProducesFractionalBoundaries);
        Assert.True(((IPeakDetector)new ThresholdDetector()).ProducesFractionalBoundaries);
        Assert.False(((IPeakDetector)new OspreyCwtDetector()).ProducesFractionalBoundaries);
        Assert.True(((IPeakDetector)new OspreyCwtDetector(improved: true)).ProducesFractionalBoundaries);
        Assert.False(((IPeakDetector)new SkylineExactDetector()).ProducesFractionalBoundaries);

        Assert.True(((IIntegrator)new TrapezoidIntegrator()).SupportsEdgeEstimation);
        Assert.False(((IIntegrator)new RiemannSumIntegrator()).SupportsEdgeEstimation);
        Assert.False(((IIntegrator)new GaussianFitIntegrator()).SupportsEdgeEstimation);
        Assert.False(((IIntegrator)new SkylineExactIntegrator()).SupportsEdgeEstimation);
    }

    [Fact]
    public void Edge_estimation_changes_area_only_for_fractional_boundary_detectors()
    {
        var peak = new GaussianPeak(1000, 8.0, 0.3);
        var (rt, y) = SampleClean(peak, 8, boundaryRel: 0.5); // tighter bounds so edges matter
        var trap = new TrapezoidIntegrator();

        // FindPeaks reports fractional boundaries → edge estimation changes the area.
        PeakBounds fp = new FindPeaksDetector().Detect(rt, y, new DetectorParams(0.5, 0.0, 0.5));
        double fpNoEdge = trap.Integrate(rt, y, fp, new IntegratorOptions(false));
        double fpEdge = trap.Integrate(rt, y, fp, new IntegratorOptions(true));
        Assert.True(System.Math.Abs(fpEdge - fpNoEdge) > 1e-6);

        // Osprey snaps boundaries to samples → edge estimation is a no-op.
        PeakBounds op = new OspreyCwtDetector().Detect(rt, y, DetectorParams.Default);
        double opNoEdge = trap.Integrate(rt, y, op, new IntegratorOptions(false));
        double opEdge = trap.Integrate(rt, y, op, new IntegratorOptions(true));
        Assert.Equal(opNoEdge, opEdge, 9);
    }

    [Fact]
    public void GaussianFit_returns_nan_when_too_few_points()
    {
        var peak = new GaussianPeak(1000, 8.0, 0.3);
        var (rt, y) = SampleClean(peak, 2);
        PeakBounds b = new ThresholdDetector().Detect(rt, y, DetectorParams.Default);
        double area = new GaussianFitIntegrator().Integrate(rt, y, b, IntegratorOptions.Default);
        Assert.True(double.IsNaN(area));
    }

    [Fact]
    public void Sweep_reports_na_for_fit_methods_at_low_sampling_but_values_when_sampled_enough()
    {
        var peak = new GaussianPeak(1000, 8.0, 0.3);
        var builder = new ChromatogramBuilder(
            peak, System.Array.Empty<IPeakModel>(), new NoBackground(),
            new NoiseParameters(15, false), 5, 11, 2000);
        var pipeline = new QuantificationPipeline(
            new FindPeaksDetector(), new NoBackgroundSubtractor(), new GaussianFitIntegrator(),
            DetectorParams.Default, IntegratorOptions.Default);

        var results = new SimulationEngine().Run(builder, pipeline, new SimulationSettings(2, 12, 40, 7));

        Assert.True(double.IsNaN(results[0].PercentDeviation)); // 2 points -> NA
        Assert.True(double.IsFinite(results[^1].PercentDeviation)); // 12 points -> a value
    }

    [Fact]
    public void SkylineExact_area_matches_hand_computed_trapezoid_minus_clipped_background()
    {
        // rt spacing 1; intensities 0,100,200,100,0. backgroundLevel = min(ends) = 0, so the
        // background term is 0 and area = (sum - first/2 - last/2) * interval = 400 * 1.
        var rt = new double[] { 0, 1, 2, 3, 4 };
        var clean = new double[] { 0, 100, 200, 100, 0 };
        var withBg = new double[] { 50, 150, 250, 150, 50 };
        var bounds = new PeakBounds(true, 0, 4, 2, 0, 4, 2);
        var integrator = new SkylineExactIntegrator();

        Assert.Equal(400.0, integrator.Integrate(rt, clean, bounds, IntegratorOptions.Default), 3);
        // A flat background is removed exactly, so the area is unchanged.
        Assert.Equal(400.0, integrator.Integrate(rt, withBg, bounds, IntegratorOptions.Default), 3);
    }

    [Fact]
    public void Constant_background_subtractor_removes_the_lower_boundary_level()
    {
        var rt = new double[] { 0, 1, 2, 3, 4 };
        var intensity = new double[] { 30, 130, 230, 130, 30 };
        var bounds = new PeakBounds(true, 0, 4, 2, 0, 4, 2);
        double[] result = new ConstantBackgroundSubtractor().Subtract(rt, intensity, bounds);
        Assert.Equal(new double[] { 0, 100, 200, 100, 0 }, result);
    }
}
