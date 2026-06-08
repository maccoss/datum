using Datum.Core.Algorithms;
using Datum.Core.Algorithms.BackgroundSubtractors;
using Datum.Core.Algorithms.Detectors;
using Datum.Core.Algorithms.Integrators;
using Datum.Core.Models;
using Datum.Core.Simulation;
using Datum.Koina;
using Xunit;

namespace Datum.Core.Tests;

public class MultiTransitionTests
{
    [Fact]
    public void MultiTransition_true_area_sums_fragment_contributions()
    {
        var profile = new GaussianPeak(1000, 8.0, 0.3);
        double[] rel = { 1.0, 0.5, 0.25 };
        var builder = new MultiTransitionBuilder(profile, rel, new NoBackground(), NoiseParameters.None);
        Assert.Equal((1.0 + 0.5 + 0.25) * profile.TrueArea(), builder.TrueAreaTotal(), 6);
    }

    [Fact]
    public void MultiTransition_sweep_converges_to_truth_on_clean_fragments()
    {
        var profile = new GaussianPeak(1000, 8.0, 0.3);
        double[] rel = { 1.0, 0.7, 0.5, 0.3 };
        var builder = new MultiTransitionBuilder(profile, rel, new NoBackground(), NoiseParameters.None);

        var results = new MultiTransitionEngine().Run(
            builder, new OspreyCwtDetector(), new TrapezoidIntegrator(), new NoBackgroundSubtractor(),
            DetectorParams.Default, new IntegratorOptions(EdgeEstimation: true),
            new SimulationSettings(MinPoints: 4, MaxPoints: 25, Iterations: 15));

        DeviationResult many = results[^1];
        Assert.True(System.Math.Abs(many.PercentDeviation) < 6.0,
            $"deviation at many points was {many.PercentDeviation}%");
    }

    [Fact]
    public void MultiTransition_builder_applies_per_transition_interference_and_background()
    {
        var profile = new GaussianPeak(1000, 8.0, 0.3);
        double[] rel = { 1.0, 1.0, 1.0 };
        var interference = new GaussianPeak(500, 9.2, 0.2);
        var perInterference = new IReadOnlyList<IPeakModel>[]
        {
            new[] { (IPeakModel)interference }, // transition 0 interfered
            System.Array.Empty<IPeakModel>(),
            System.Array.Empty<IPeakModel>(),
        };
        var perBackground = new IBackground[]
        {
            new ConstantBackground(10),
            new ConstantBackground(100),
            new ConstantBackground(500),
        };

        var builder = new MultiTransitionBuilder(
            profile, rel, new NoBackground(), NoiseParameters.None, perInterference, perBackground);

        var rt = new double[] { 7.0, 8.0, 9.2, 11.0 };
        double[][] frags = builder.SampleFragments(rt, new RandomSource(1));

        // Off-peak (rt 11) reflects each transition's own background.
        Assert.Equal(10.0, frags[0][3], 3);
        Assert.Equal(100.0, frags[1][3], 3);
        Assert.Equal(500.0, frags[2][3], 3);

        // At the interference center (rt 9.2) only transition 0 carries the extra peak.
        double cleanAt92 = rel[1] * profile.Evaluate(9.2) + 100.0;
        Assert.Equal(cleanAt92, frags[1][2], 3);
        Assert.True(frags[0][2] - (rel[0] * profile.Evaluate(9.2) + 10.0) > 400.0, "transition 0 should carry the interference");

        // Interference and background are contamination, not analyte: the true area excludes them.
        Assert.Equal((1.0 + 1.0 + 1.0) * profile.TrueArea(), builder.TrueAreaTotal(), 6);
    }

    [Fact]
    public void ConsensusEmg_recovers_area_under_interference_in_a_minority_of_transitions()
    {
        // Six transitions share one EMG elution profile; two of them carry a co-eluting interference
        // peak. The median-shape consensus integrator should recover the total area far better than
        // independent per-transition EMG fits, which absorb the interference into the affected areas.
        var profile = new EmgPeak(1000, 12.0, 0.25, 0.35);
        double[] rel = { 1.0, 0.8, 0.6, 0.5, 0.45, 0.34 };
        double trueTotal = 0.0;
        foreach (double r in rel)
        {
            trueTotal += r * profile.TrueArea();
        }

        int n = 120;
        var rt = new double[n];
        for (int i = 0; i < n; i++)
        {
            rt[i] = 9.0 + 7.0 * i / (n - 1);
        }

        var interference = new GaussianPeak(600, 12.5, 0.18);
        var frags = new double[rel.Length][];
        for (int f = 0; f < rel.Length; f++)
        {
            frags[f] = new double[n];
            for (int i = 0; i < n; i++)
            {
                frags[f][i] = rel[f] * profile.Evaluate(rt[i]);
                if (f < 2)
                {
                    frags[f][i] += interference.Evaluate(rt[i]);
                }
            }
        }

        var bounds = new PeakBounds(true, 0, n - 1, n / 2, rt[0], rt[^1], 12.0);

        double perTransition = 0.0;
        var emg = new EmgFitIntegrator();
        foreach (double[] trace in frags)
        {
            perTransition += emg.Integrate(rt, trace, bounds, IntegratorOptions.Default);
        }

        double[] areas = new ConsensusEmgIntegrator().IntegrateAll(rt, frags, bounds, IntegratorOptions.Default);
        double consensus = 0.0;
        foreach (double a in areas)
        {
            consensus += a;
        }

        double consensusDev = System.Math.Abs(consensus - trueTotal) / trueTotal;
        double perTransitionDev = System.Math.Abs(perTransition - trueTotal) / trueTotal;

        Assert.True(consensusDev < 0.05, $"consensus deviation {consensusDev:P1} should be small under 2/6 interference");
        Assert.True(consensusDev < perTransitionDev / 3.0,
            $"consensus ({consensusDev:P1}) should be much better than per-transition EMG ({perTransitionDev:P1})");
    }

    [Fact]
    public void Improved_boundary_captures_tail_so_consensus_recovers_tailed_peak_area()
    {
        // The tail-aware (asymmetric) boundary reaches down the EMG tail, so the consensus EMG
        // recovers the area of a strongly tailed peak across sampling. A symmetric boundary would
        // chop the tail and under-report by ~10%.
        var profile = new EmgPeak(1000, 12.0, 0.25, 0.4);
        double[] rel = { 1.0, 0.7, 0.5, 0.4 };
        var builder = new MultiTransitionBuilder(profile, rel, new ConstantBackground(50), new NoiseParameters(15, false));

        var results = new MultiTransitionEngine().Run(
            builder, new OspreyCwtDetector(improved: true), new ConsensusEmgIntegrator(),
            new LinearBaselineSubtractor(), DetectorParams.Default, new IntegratorOptions(EdgeEstimation: true),
            new SimulationSettings(MinPoints: 8, MaxPoints: 20, Iterations: 20));

        DeviationResult many = results[^1];
        Assert.True(System.Math.Abs(many.PercentDeviation) < 7.0,
            $"tailed-peak consensus deviation {many.PercentDeviation}% should be small (tail captured)");
    }

    [Fact]
    public void ConsensusEmg_matches_per_transition_emg_when_all_clean()
    {
        // With no interference the shared-shape consensus and independent fits agree.
        var profile = new EmgPeak(1000, 12.0, 0.25, 0.3);
        double[] rel = { 1.0, 0.7, 0.5 };
        double trueTotal = 0.0;
        foreach (double r in rel)
        {
            trueTotal += r * profile.TrueArea();
        }

        int n = 100;
        var rt = new double[n];
        for (int i = 0; i < n; i++)
        {
            rt[i] = 9.0 + 7.0 * i / (n - 1);
        }

        var frags = new double[rel.Length][];
        for (int f = 0; f < rel.Length; f++)
        {
            frags[f] = new double[n];
            for (int i = 0; i < n; i++)
            {
                frags[f][i] = rel[f] * profile.Evaluate(rt[i]);
            }
        }

        var bounds = new PeakBounds(true, 0, n - 1, n / 2, rt[0], rt[^1], 12.0);
        double[] areas = new ConsensusEmgIntegrator().IntegrateAll(rt, frags, bounds, IntegratorOptions.Default);
        double consensus = 0.0;
        foreach (double a in areas)
        {
            consensus += a;
        }

        Assert.True(System.Math.Abs(consensus - trueTotal) / trueTotal < 0.02, $"clean consensus {consensus} vs {trueTotal}");
    }

    [Fact]
    public void Koina_top_fragments_filters_impossible_ions_and_normalizes()
    {
        // -1 marks impossible ions (Prosit convention); they must be dropped.
        string[] ann = { "y1+1", "b2+1", "y3+1", "y4+1" };
        double[] mz = { 175.1, 200.2, 400.3, 500.4 };
        double[] intensities = { -1.0, 0.25, 1.0, 0.5 };

        var top = KoinaClient.ToTopFragments(ann, mz, intensities, topN: 2);

        Assert.Equal(2, top.Count);
        Assert.Equal("y3+1", top[0].Annotation); // highest intensity first
        Assert.Equal(1.0, top[0].RelativeIntensity, 6); // normalized to base peak
        Assert.Equal(0.5, top[1].RelativeIntensity, 6);
        Assert.DoesNotContain(top, f => f.Annotation == "y1+1"); // -1 ion excluded
    }
}
