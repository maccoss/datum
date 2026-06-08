//! Monte-Carlo sweep for multi-transition (consensus) quantification.

using Datum.Core.Algorithms;
using Datum.Core.Algorithms.Detectors;

namespace Datum.Core.Simulation;

/// <summary>
/// Runs the points-across-peak sweep for the multi-transition case. A single shared boundary is
/// determined for all transitions (see <see cref="DetectSharedBounds"/>): Osprey uses its median
/// CWT consensus across transitions, while every other detector uses one boundary from the summed
/// transitions and applies it to all (Skyline's "Integrate all" behavior). Each transition is then
/// integrated over that one boundary and the summed area is compared to the ground-truth total.
/// </summary>
public sealed class MultiTransitionEngine
{
    /// <summary>
    /// Determine the single boundary shared by all transitions. Osprey computes a median-CWT
    /// consensus across the transitions; every other detector is run on the summed transition
    /// signal and the resulting boundary is applied to all of them (Skyline "Integrate all").
    /// </summary>
    public static PeakBounds DetectSharedBounds(
        IPeakDetector detector, double[] rt, double[][] fragments, DetectorParams detectorParams)
    {
        if (detector is OspreyCwtDetector osprey)
        {
            return osprey.DetectFromXics(rt, fragments, detectorParams);
        }

        var summed = new double[rt.Length];
        foreach (double[] fragment in fragments)
        {
            for (int i = 0; i < rt.Length; i++)
            {
                summed[i] += fragment[i];
            }
        }

        return detector.Detect(rt, summed, detectorParams);
    }

    /// <summary>Execute the multi-transition sweep.</summary>
    /// <param name="builder">Multi-transition chromatogram source.</param>
    /// <param name="detector">Detector used to determine the shared boundary for all transitions.</param>
    /// <param name="integrator">Per-fragment area integrator.</param>
    /// <param name="backgroundSubtractor">Per-fragment background subtractor.</param>
    /// <param name="detectorParams">Detector parameters.</param>
    /// <param name="integratorOptions">Integrator options.</param>
    /// <param name="settings">Sweep settings.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IReadOnlyList<DeviationResult> Run(
        MultiTransitionBuilder builder,
        IPeakDetector detector,
        IIntegrator integrator,
        IBackgroundSubtractor backgroundSubtractor,
        DetectorParams detectorParams,
        IntegratorOptions integratorOptions,
        SimulationSettings settings,
        IProgress<SimulationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        double trueArea = builder.TrueAreaTotal();
        int total = settings.MaxPoints - settings.MinPoints + 1;
        var results = new List<DeviationResult>(total);
        int completed = 0;

        for (int points = settings.MinPoints; points <= settings.MaxPoints; points++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var areas = new double[settings.Iterations];
            int pointsLocal = points;
            Parallel.For(0, settings.Iterations, new ParallelOptions { CancellationToken = cancellationToken }, iter =>
            {
                var rng = new RandomSource(unchecked((settings.Seed * 397) ^ (pointsLocal * 131) ^ iter));
                double offset = rng.NextUniform();
                SamplingGrid grid = SamplingGrid.Create(
                    builder.Profile, pointsLocal, offset, settings.BoundaryFraction, settings.ContextFraction);
                double[][] fragments = builder.SampleFragments(grid.Rt, rng);
                PeakBounds bounds = DetectSharedBounds(detector, grid.Rt, fragments, detectorParams);

                areas[iter] = IntegrateFragments(
                    integrator, backgroundSubtractor, grid.Rt, fragments, bounds, integratorOptions);
            });

            (double mean, double std) = MeanAndStd(areas);
            results.Add(new DeviationResult(
                points,
                (mean - trueArea) / trueArea * 100.0,
                std / trueArea * 100.0,
                mean,
                trueArea));

            completed++;
            progress?.Report(new SimulationProgress(completed, total));
        }

        return results;
    }

    /// <summary>
    /// Background-subtract every transition (per transition, so a transition-specific background is
    /// handled on its own trace), then sum the integrated areas. A consensus integrator
    /// (<see cref="IMultiTransitionIntegrator"/>) quantifies all transitions jointly; otherwise each
    /// transition is integrated independently.
    /// </summary>
    public static double IntegrateFragments(
        IIntegrator integrator,
        IBackgroundSubtractor backgroundSubtractor,
        double[] rt,
        double[][] fragments,
        PeakBounds bounds,
        IntegratorOptions integratorOptions)
    {
        var subtracted = new double[fragments.Length][];
        for (int f = 0; f < fragments.Length; f++)
        {
            subtracted[f] = backgroundSubtractor.Subtract(rt, fragments[f], bounds);
        }

        if (integrator is IMultiTransitionIntegrator consensus)
        {
            double[] perTransition = consensus.IntegrateAll(rt, subtracted, bounds, integratorOptions);
            double sum = 0.0;
            foreach (double a in perTransition)
            {
                sum += a; // a NaN transition propagates to NaN, matching the per-transition path
            }

            return sum;
        }

        double total = 0.0;
        foreach (double[] trace in subtracted)
        {
            total += integrator.Integrate(rt, trace, bounds, integratorOptions);
        }

        return total;
    }

    private static (double Mean, double Std) MeanAndStd(double[] values)
    {
        double sum = 0.0;
        int n = 0;
        foreach (double v in values)
        {
            if (double.IsFinite(v))
            {
                sum += v;
                n++;
            }
        }

        if (n == 0 || n < 0.8 * values.Length)
        {
            return (double.NaN, double.NaN);
        }

        double mean = sum / n;
        if (n < 2)
        {
            return (mean, 0.0);
        }

        double sq = 0.0;
        foreach (double v in values)
        {
            if (double.IsFinite(v))
            {
                double d = v - mean;
                sq += d * d;
            }
        }

        return (mean, System.Math.Sqrt(sq / (n - 1)));
    }
}
