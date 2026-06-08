//! Monte-Carlo engine sweeping points-across-the-peak and reporting area deviation.

namespace Datum.Core.Simulation;

/// <summary>Settings controlling the Monte-Carlo points-across-peak sweep.</summary>
/// <param name="MinPoints">Smallest number of points across the peak to evaluate.</param>
/// <param name="MaxPoints">Largest number of points across the peak to evaluate.</param>
/// <param name="Iterations">Monte-Carlo replicates per point count.</param>
/// <param name="Seed">Base RNG seed for reproducibility.</param>
/// <param name="BoundaryFraction">Apex fraction defining the reference peak width for sampling.</param>
/// <param name="ContextFraction">Baseline sampled each side of the peak, in peak-widths.</param>
public readonly record struct SimulationSettings(
    int MinPoints = 2,
    int MaxPoints = 30,
    int Iterations = 100,
    int Seed = 12345,
    double BoundaryFraction = 0.01,
    double ContextFraction = 0.5)
{
    /// <summary>Default settings (explicit; a record-struct <c>new()</c> would zero-init instead).</summary>
    public static SimulationSettings Default => new(2, 30, 100, 12345, 0.01, 0.5);
}

/// <summary>One point in the deviation-vs-sampling curve.</summary>
/// <param name="PointsAcrossPeak">Number of samples spanning the reference peak.</param>
/// <param name="PercentDeviation">Mean area deviation from truth, in percent.</param>
/// <param name="PercentStd">Standard deviation of area across replicates, in percent of truth.</param>
/// <param name="MeanArea">Mean integrated area across replicates.</param>
/// <param name="TrueArea">The ground-truth area.</param>
public readonly record struct DeviationResult(
    int PointsAcrossPeak,
    double PercentDeviation,
    double PercentStd,
    double MeanArea,
    double TrueArea);

/// <summary>Progress update emitted during a sweep.</summary>
/// <param name="Completed">Point counts completed so far.</param>
/// <param name="Total">Total point counts to evaluate.</param>
public readonly record struct SimulationProgress(int Completed, int Total);

/// <summary>
/// Runs the Monte-Carlo experiment: for each number of points across the peak, repeatedly
/// synthesize a fresh noisy chromatogram with a random sampling offset, quantify it, and
/// summarize how far the recovered area strays from the ground truth (mean deviation and
/// variance). Ports the reference notebook's simulation loop.
/// </summary>
public sealed class SimulationEngine
{
    /// <summary>
    /// Execute the sweep. The builder supplies the peak/background/noise model; the pipeline
    /// supplies the detection/integration algorithms.
    /// </summary>
    /// <param name="builder">Chromatogram source (defines ground truth and noise).</param>
    /// <param name="pipeline">Detection/subtraction/integration algorithms.</param>
    /// <param name="settings">Sweep settings.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IReadOnlyList<DeviationResult> Run(
        ChromatogramBuilder builder,
        QuantificationPipeline pipeline,
        SimulationSettings settings,
        IProgress<SimulationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        double trueArea = builder.MainPeak.TrueArea();
        int total = settings.MaxPoints - settings.MinPoints + 1;
        var results = new List<DeviationResult>(total);

        int completed = 0;
        for (int points = settings.MinPoints; points <= settings.MaxPoints; points++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Each iteration is independently seeded, so they can run in parallel; results are
            // written to distinct indices and reduced deterministically afterward.
            var areas = new double[settings.Iterations];
            int pointsLocal = points;
            Parallel.For(0, settings.Iterations, new ParallelOptions { CancellationToken = cancellationToken }, iter =>
            {
                // Deterministic per-(points, iter) seed keeps runs reproducible.
                var rng = new RandomSource(CombineSeed(settings.Seed, pointsLocal, iter));
                double offset = rng.NextUniform();
                SamplingGrid grid = SamplingGrid.Create(
                    builder.MainPeak, pointsLocal, offset, settings.BoundaryFraction, settings.ContextFraction);
                double[] sampleIntensity = builder.SampleNoisy(grid.Rt, rng);
                areas[iter] = pipeline.Run(grid.Rt, sampleIntensity).Area;
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
    /// Mean and sample standard deviation over the finite areas, ignoring any non-finite values
    /// (e.g. a fit integrator returning NaN when it cannot fit). Returns NaN when none are finite,
    /// so that point count is reported as "NA".
    /// </summary>
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

        // Require most iterations to have produced a finite area; otherwise the method is
        // unreliable at this sampling rate and the point count is reported as "NA".
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

    /// <summary>Combine the base seed with the loop indices into a stable per-iteration seed.</summary>
    private static int CombineSeed(int baseSeed, int points, int iter)
    {
        unchecked
        {
            int hash = baseSeed;
            hash = (hash * 397) ^ points;
            hash = (hash * 397) ^ iter;
            return hash;
        }
    }
}
