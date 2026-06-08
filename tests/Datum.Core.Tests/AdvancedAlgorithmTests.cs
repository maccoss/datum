using Datum.Core.Algorithms;
using Datum.Core.Algorithms.BackgroundSubtractors;
using Datum.Core.Algorithms.Detectors;
using Datum.Core.Algorithms.Integrators;
using Datum.Core.Models;
using Datum.Core.Simulation;
using Datum.Skyline;
using Xunit;

namespace Datum.Core.Tests;

public class AdvancedAlgorithmTests
{
    private static (double[] Rt, double[] Y) SampleClean(IPeakModel peak, int points)
    {
        var builder = new ChromatogramBuilder(
            peak, System.Array.Empty<IPeakModel>(), new NoBackground(), NoiseParameters.None,
            peak.Center - 6 * peak.Sigma, peak.ApexRt + 8 * peak.Sigma, 2000);
        var grid = SamplingGrid.Create(peak, points, 0.5);
        return (grid.Rt, builder.SampleNoisy(grid.Rt, new RandomSource(1)));
    }

    [Fact]
    public void OspreyCwt_detects_apex_near_true_center()
    {
        var peak = new GaussianPeak(1000, 8.0, 0.3);
        var (rt, y) = SampleClean(peak, 20);
        PeakBounds b = new OspreyCwtDetector().Detect(rt, y, DetectorParams.Default);
        Assert.True(b.Detected);
        Assert.True(System.Math.Abs(b.ApexRt - 8.0) < 0.3);
    }

    [Fact]
    public void OspreyCwt_improved_places_fractional_boundary_near_sigma_multiple_and_recovers_area()
    {
        // The improved variant is the integration model: it places the boundaries at apex +/-
        // BoundarySigmaMultiple sigma (default 3.0) as exact fractional retention times (not snapped
        // to a sample), and reports fractional boundaries so an edge-estimating integrator integrates
        // to the true boundary.
        var peak = new GaussianPeak(1000, 8.0, 0.3);

        var (rt, y) = SampleClean(peak, 20);
        var improved = new OspreyCwtDetector(improved: true);
        PeakBounds bounds = improved.Detect(rt, y, DetectorParams.Default);
        Assert.True(bounds.Detected);
        Assert.True(((IPeakDetector)improved).ProducesFractionalBoundaries);

        // Boundary half-width should be near 3 sigma = 0.9 (within a coarse tolerance on noisy data).
        double halfWidth = 0.5 * (bounds.EndRt - bounds.StartRt);
        Assert.True(System.Math.Abs(halfWidth - 3.0 * 0.3) < 0.3, $"half-width {halfWidth} should be near 3 sigma = 0.9");

        // Boundary is fractional: it need not land on a sampled retention time.
        Assert.DoesNotContain(rt, t => t == bounds.StartRt);

        var builder = new ChromatogramBuilder(
            peak, System.Array.Empty<IPeakModel>(), new ConstantBackground(50), NoiseParameters.None, 5, 11, 2000);
        var pipeline = new QuantificationPipeline(
            new OspreyCwtDetector(improved: true), new LinearBaselineSubtractor(), new TrapezoidIntegrator(),
            DetectorParams.Default, new IntegratorOptions(true));
        var results = new SimulationEngine().Run(builder, pipeline, new SimulationSettings(8, 20, 30, 7));

        DeviationResult many = results[^1];
        Assert.True(System.Math.Abs(many.PercentDeviation) < 15.0,
            $"improved background-subtracted deviation {many.PercentDeviation}% should be modest");
    }

    [Fact]
    public void OspreyCwt_improved_boundary_is_symmetric_on_gaussian_but_tailed_on_emg()
    {
        var detector = new OspreyCwtDetector(improved: true);

        (double[] Rt, double[] Y) SampleWide(IPeakModel peak)
        {
            var builder = new ChromatogramBuilder(
                peak, System.Array.Empty<IPeakModel>(), new NoBackground(), NoiseParameters.None,
                peak.Center - 6 * peak.Sigma, peak.ApexRt + 18 * peak.Sigma, 3000);
            var grid = SamplingGrid.Create(peak, 25, 0.5);
            return (grid.Rt, builder.SampleNoisy(grid.Rt, new RandomSource(1)));
        }

        // Symmetric peak: the two sides land at nearly the same distance.
        var gaussian = new GaussianPeak(1000, 12.0, 0.3);
        var (rtG, yG) = SampleWide(gaussian);
        PeakBounds g = detector.Detect(rtG, yG, DetectorParams.Default);
        double leftG = g.ApexRt - g.StartRt;
        double rightG = g.EndRt - g.ApexRt;
        Assert.True(rightG / leftG < 1.4, $"Gaussian boundary asymmetry {rightG / leftG} should be ~1");

        // Tailed peak: the trailing edge reaches substantially farther than the leading edge.
        var emg = new EmgPeak(1000, 12.0, 0.25, 0.5);
        var (rtE, yE) = SampleWide(emg);
        PeakBounds e = detector.Detect(rtE, yE, DetectorParams.Default);
        double leftE = e.ApexRt - e.StartRt;
        double rightE = e.EndRt - e.ApexRt;
        Assert.True(rightE / leftE > 1.5, $"EMG boundary asymmetry {rightE / leftE} should exceed 1.5 (tail extended)");
    }

    [Fact]
    public void OspreyCwt_improved_boundary_is_independent_of_constant_background()
    {
        // The peak width (and hence the k*sigma boundaries) is derived from a baseline-corrected
        // FWHM, so a constant chemical background must not move the boundaries. Before the fix the
        // FWHM was measured from the absolute apex height, so a large background pushed the
        // half-height into the tails and the boundaries far out.
        var profile = new GaussianPeak(1000, 8.0, 0.3);
        double[] rel = { 1.0, 0.8, 0.6, 0.5, 0.45, 0.34 };
        var grid = SamplingGrid.Create(profile, 15, 0.5);

        double[][] Make(double background)
        {
            var frags = new double[rel.Length][];
            for (int f = 0; f < rel.Length; f++)
            {
                frags[f] = new double[grid.Rt.Length];
                for (int i = 0; i < grid.Rt.Length; i++)
                {
                    frags[f][i] = rel[f] * profile.Evaluate(grid.Rt[i]) + background;
                }
            }

            return frags;
        }

        var detector = new OspreyCwtDetector(improved: true);
        PeakBounds low = detector.DetectFromXics(grid.Rt, Make(50), DetectorParams.Default);
        PeakBounds high = detector.DetectFromXics(grid.Rt, Make(500), DetectorParams.Default);

        Assert.Equal(low.StartRt, high.StartRt, 6);
        Assert.Equal(low.EndRt, high.EndRt, 6);
    }

    [Fact]
    public void OspreyCwt_improved_boundary_rejects_single_transition_interference()
    {
        // The improved boundary uses the per-point median across transitions, so interference in
        // one transition must not move the shared integration boundary.
        var profile = new GaussianPeak(1000, 8.0, 0.3);
        double[] rel = { 1.0, 0.67, 0.5, 0.45, 0.42, 0.34 };
        var grid = SamplingGrid.Create(profile, 15, 0.5);
        var interference = new GaussianPeak(350, 9.2, 0.25);

        double[][] Make(bool withInterference)
        {
            var frags = new double[rel.Length][];
            for (int f = 0; f < rel.Length; f++)
            {
                frags[f] = new double[grid.Rt.Length];
                for (int i = 0; i < grid.Rt.Length; i++)
                {
                    frags[f][i] = rel[f] * profile.Evaluate(grid.Rt[i]);
                    if (withInterference && f == rel.Length - 1)
                    {
                        frags[f][i] += interference.Evaluate(grid.Rt[i]);
                    }
                }
            }

            return frags;
        }

        var detector = new OspreyCwtDetector(improved: true);
        PeakBounds clean = detector.DetectFromXics(grid.Rt, Make(false), DetectorParams.Default);
        PeakBounds interfered = detector.DetectFromXics(grid.Rt, Make(true), DetectorParams.Default);

        // The median reference makes the boundary essentially immune to the single-transition
        // interference: it shifts by far less than a sample spacing (~0.0006 RT here), versus the
        // >1 RT drag that a summed reference would suffer. (Contrast the faithful sum-based variant.)
        Assert.True(System.Math.Abs(clean.StartRt - interfered.StartRt) < 0.02,
            $"start shifted by {System.Math.Abs(clean.StartRt - interfered.StartRt)} RT");
        Assert.True(System.Math.Abs(clean.EndRt - interfered.EndRt) < 0.02,
            $"end shifted by {System.Math.Abs(clean.EndRt - interfered.EndRt)} RT");
        Assert.True(interfered.EndRt < 9.0, "boundary must not extend into the single-transition interference at 9.2");
    }

    [Fact]
    public void OspreyCwt_consensus_rejects_single_transition_interference()
    {
        // Three clean fragment XICs sharing one apex, plus one with a spurious early spike.
        var peak = new GaussianPeak(1000, 8.0, 0.3);
        var grid = SamplingGrid.Create(peak, 25, 0.5);
        var rt = grid.Rt;
        int n = rt.Length;

        double[] Frag(double scale)
        {
            var f = new double[n];
            for (int i = 0; i < n; i++)
            {
                f[i] = scale * peak.Evaluate(rt[i]);
            }

            return f;
        }

        double[] f1 = Frag(1.0), f2 = Frag(0.8), f3 = Frag(0.6);
        double[] f4 = Frag(0.5);
        f4[2] += 5000; // single-transition interference spike near the start

        PeakBounds b = new OspreyCwtDetector().DetectFromXics(rt, new[] { f1, f2, f3, f4 }, DetectorParams.Default);
        Assert.True(b.Detected);
        // The median consensus should ignore the lone spike and land on the real apex.
        Assert.True(System.Math.Abs(b.ApexRt - 8.0) < 0.4);
    }

    [Fact]
    public void GaussianFit_recovers_area_even_with_truncated_boundaries()
    {
        var peak = new GaussianPeak(1000, 8.0, 0.3);
        var (rt, y) = SampleClean(peak, 8);
        var detector = new ThresholdDetector();

        // Use FWHM boundaries (rel height 0.5) to deliberately truncate the tails.
        var tight = new DetectorParams(0.5, 0.0, 0.5);
        PeakBounds b = detector.Detect(rt, y, tight);

        double fitArea = new GaussianFitIntegrator().Integrate(rt, y, b, IntegratorOptions.Default);
        double trapArea = new TrapezoidIntegrator().Integrate(rt, y, b, IntegratorOptions.Default);

        // The analytic fit extrapolates the truncated tails; trapezoid cannot.
        Assert.True(System.Math.Abs(fitArea - peak.TrueArea()) / peak.TrueArea() < 0.05);
        Assert.True(System.Math.Abs(fitArea - peak.TrueArea()) < System.Math.Abs(trapArea - peak.TrueArea()));
    }

    [Fact]
    public void EmgFit_recovers_area_on_a_tailed_peak()
    {
        var peak = new EmgPeak(1000, 8.0, 0.25, 0.35);
        var (rt, y) = SampleClean(peak, 25);
        PeakBounds b = new FindPeaksDetector().Detect(rt, y, DetectorParams.Default);
        Assert.True(b.Detected);

        double area = new EmgFitIntegrator().Integrate(rt, y, b, IntegratorOptions.Default);
        Assert.True(System.Math.Abs(area - peak.TrueArea()) / peak.TrueArea() < 0.08,
            $"EMG fit area {area} vs true {peak.TrueArea()}");
    }

    [Fact]
    public void Skyline_exact_integrator_removes_constant_background_exactly()
    {
        // Skyline's background (min-clip at the boundary level) removes a constant offset exactly,
        // and its 2nd-derivative detection ignores a constant offset, so the recovered area is
        // identical with and without a flat background.
        var peak = new GaussianPeak(1000, 8.0, 0.3);
        var grid = SamplingGrid.Create(peak, 25, 0.5);

        var noBg = new ChromatogramBuilder(peak, System.Array.Empty<IPeakModel>(), new NoBackground(),
            NoiseParameters.None, 6, 11, 2000);
        var withBg = new ChromatogramBuilder(peak, System.Array.Empty<IPeakModel>(), new ConstantBackground(100),
            NoiseParameters.None, 6, 11, 2000);

        double[] y0 = noBg.SampleNoisy(grid.Rt, new RandomSource(1));
        double[] y1 = withBg.SampleNoisy(grid.Rt, new RandomSource(1));

        var detector = new SkylineExactDetector();
        var integrator = new SkylineExactIntegrator();
        double area0 = integrator.Integrate(grid.Rt, y0, detector.Detect(grid.Rt, y0, DetectorParams.Default), IntegratorOptions.Default);
        double area1 = integrator.Integrate(grid.Rt, y1, detector.Detect(grid.Rt, y1, DetectorParams.Default), IntegratorOptions.Default);

        Assert.True(area0 > 0);
        Assert.Equal(area0, area1, 3); // constant background removed exactly
    }

    [Fact]
    public void Skyline_exact_detector_finds_peak()
    {
        var peak = new GaussianPeak(1000, 8.0, 0.3);
        var (rt, y) = SampleClean(peak, 20);
        PeakBounds b = new SkylineExactDetector().Detect(rt, y, DetectorParams.Default);
        Assert.True(b.Detected);
        Assert.True(System.Math.Abs(b.ApexRt - 8.0) < 0.3);
    }
}
