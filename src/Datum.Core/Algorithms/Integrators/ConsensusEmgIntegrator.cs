//! Consensus EMG integrator: one shared EMG shape across transitions, with a robust per-transition
//! amplitude so interference in a minority of transitions does not corrupt their recovered areas.

namespace Datum.Core.Algorithms.Integrators;

/// <summary>
/// Quantifies multiple transitions with a single shared exponentially-modified Gaussian shape. The
/// shape (mu, sigma, tau) is fitted once to the per-point MEDIAN across transitions, which suppresses
/// interference present in only a minority of transitions (the same consensus principle the Osprey
/// detector uses for the apex). Each transition's area is then the robust amplitude of that fixed
/// unit-area shape against the transition's own points: the median of <c>y_i / shape_i</c> over the
/// points where the shape is significant. Because the shape is taken from the clean consensus and the
/// amplitude is a median, an interference bump on one or two transitions is rejected and the area
/// under it is recovered from the shared shape.
/// </summary>
/// <remarks>
/// In single-transition use it falls back to an independent EMG fit (identical to
/// <see cref="EmgFitIntegrator"/>), so it is a valid standalone integrator too.
/// </remarks>
public sealed class ConsensusEmgIntegrator : IIntegrator, IMultiTransitionIntegrator
{
    /// <summary>Points are kept for the amplitude estimate only where the unit shape is at least this
    /// fraction of its peak, so near-baseline tail points (tiny denominators) do not add noise.</summary>
    private const double ShapeFloorFraction = 0.05;

    /// <inheritdoc/>
    public string Name => "Consensus EMG (median shape)";

    /// <inheritdoc/>
    public double Integrate(double[] rt, double[] intensity, PeakBounds bounds, IntegratorOptions options)
    {
        if (!bounds.Detected)
        {
            return 0.0;
        }

        (double[] x, double[] y) = InPeak(rt, intensity, bounds);
        return EmgFit.Fit(x, y)?.Area ?? double.NaN;
    }

    /// <inheritdoc/>
    public double[] IntegrateAll(double[] rt, double[][] subtractedFragments, PeakBounds bounds, IntegratorOptions options)
    {
        int fragmentCount = subtractedFragments.Length;
        var areas = new double[fragmentCount];
        if (!bounds.Detected || fragmentCount == 0)
        {
            System.Array.Fill(areas, double.NaN);
            return areas;
        }

        // In-peak sample indices (shared across transitions).
        var indices = new List<int>();
        for (int i = bounds.StartIndex; i <= bounds.EndIndex && i < rt.Length; i++)
        {
            if (i >= 0)
            {
                indices.Add(i);
            }
        }

        if (indices.Count < 5)
        {
            System.Array.Fill(areas, double.NaN);
            return areas;
        }

        // Fit one shape to the per-point median across transitions.
        var x = new double[indices.Count];
        var medianTrace = new double[indices.Count];
        var column = new double[fragmentCount];
        for (int j = 0; j < indices.Count; j++)
        {
            int i = indices[j];
            x[j] = rt[i];
            for (int f = 0; f < fragmentCount; f++)
            {
                column[f] = subtractedFragments[f][i];
            }

            medianTrace[j] = Median(column);
        }

        EmgFit.Result? shape = EmgFit.Fit(x, medianTrace);
        if (shape is not { } s)
        {
            System.Array.Fill(areas, double.NaN);
            return areas;
        }

        // Unit-area shape at each in-peak point; keep the points where it is significant.
        var g = new double[indices.Count];
        double gMax = 0.0;
        for (int j = 0; j < indices.Count; j++)
        {
            g[j] = EmgFit.UnitDensity(s.Mu, s.Sigma, s.Tau, x[j]);
            if (g[j] > gMax)
            {
                gMax = g[j];
            }
        }

        if (gMax <= 0.0)
        {
            System.Array.Fill(areas, double.NaN);
            return areas;
        }

        double floor = ShapeFloorFraction * gMax;

        // Per-transition area = robust (median) amplitude of the shared unit-area shape.
        var ratios = new List<double>(indices.Count);
        for (int f = 0; f < fragmentCount; f++)
        {
            ratios.Clear();
            for (int j = 0; j < indices.Count; j++)
            {
                if (g[j] >= floor)
                {
                    ratios.Add(subtractedFragments[f][indices[j]] / g[j]);
                }
            }

            double amplitude = ratios.Count > 0 ? Median(ratios) : double.NaN;
            areas[f] = double.IsFinite(amplitude) && amplitude > 0.0 ? amplitude : double.NaN;
        }

        return areas;
    }

    private static (double[] X, double[] Y) InPeak(double[] rt, double[] intensity, PeakBounds bounds)
    {
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

        return (xs.ToArray(), ys.ToArray());
    }

    /// <summary>Median of a list (does not mutate the input order beyond a local copy).</summary>
    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        var copy = new double[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            copy[i] = values[i];
        }

        System.Array.Sort(copy);
        int mid = copy.Length / 2;
        return copy.Length % 2 == 1 ? copy[mid] : 0.5 * (copy[mid - 1] + copy[mid]);
    }
}
