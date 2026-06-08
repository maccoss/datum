//! Multi-transition chromatogram synthesis: several fragment XICs sharing one elution profile.

using Datum.Core.Models;

namespace Datum.Core.Simulation;

/// <summary>
/// Synthesizes a set of fragment-ion XICs that share a single elution profile but differ in
/// abundance (e.g. the top-N transitions for a peptide, with relative intensities from Koina).
/// Each fragment is the profile scaled by its relative intensity, with independent noise and a
/// shared chemical background. This feeds the Osprey consensus (median CWT) detection path,
/// which is where multiple transitions help reject single-transition interference.
/// </summary>
public sealed class MultiTransitionBuilder
{
    private readonly IPeakModel _profile;
    private readonly double[] _relativeIntensities;
    private readonly IBackground _background;
    private readonly NoiseParameters _noise;
    private readonly IReadOnlyList<IReadOnlyList<IPeakModel>>? _perTransitionInterference;
    private readonly IReadOnlyList<IBackground>? _perTransitionBackground;

    /// <summary>Create a multi-transition builder.</summary>
    /// <param name="profile">Shared elution profile (defines RT, width, shape, and per-fragment scaling).</param>
    /// <param name="relativeIntensities">Per-fragment relative abundances (e.g. normalized to the base peak).</param>
    /// <param name="background">Chemical background added to each fragment (the shared default).</param>
    /// <param name="noise">Noise parameters applied independently per fragment.</param>
    /// <param name="perTransitionInterference">
    /// Optional co-eluting interference peaks per transition (outer index = transition); a null or
    /// empty inner list means that transition is clean. Interference is added to the signal before
    /// noise and is deliberately excluded from <see cref="TrueAreaTotal"/> (it is contamination, not
    /// analyte). Used to study how the consensus integrator recovers area under interference.
    /// </param>
    /// <param name="perTransitionBackground">
    /// Optional per-transition chemical background (index = transition); when null, every transition
    /// uses <paramref name="background"/>. Lets each transition carry its own background level/slope.
    /// </param>
    public MultiTransitionBuilder(
        IPeakModel profile,
        double[] relativeIntensities,
        IBackground background,
        NoiseParameters noise,
        IReadOnlyList<IReadOnlyList<IPeakModel>>? perTransitionInterference = null,
        IReadOnlyList<IBackground>? perTransitionBackground = null)
    {
        if (relativeIntensities.Length == 0)
        {
            throw new System.ArgumentException("At least one transition is required.", nameof(relativeIntensities));
        }

        if (perTransitionInterference is not null && perTransitionInterference.Count != relativeIntensities.Length)
        {
            throw new System.ArgumentException(
                "Per-transition interference must have one entry per transition.", nameof(perTransitionInterference));
        }

        if (perTransitionBackground is not null && perTransitionBackground.Count != relativeIntensities.Length)
        {
            throw new System.ArgumentException(
                "Per-transition background must have one entry per transition.", nameof(perTransitionBackground));
        }

        _profile = profile;
        _relativeIntensities = relativeIntensities;
        _background = background;
        _noise = noise;
        _perTransitionInterference = perTransitionInterference;
        _perTransitionBackground = perTransitionBackground;
    }

    /// <summary>The shared elution profile.</summary>
    public IPeakModel Profile => _profile;

    /// <summary>Number of fragment transitions.</summary>
    public int FragmentCount => _relativeIntensities.Length;

    /// <summary>
    /// Ground-truth total area summed over all fragments. Each fragment's area is its relative
    /// intensity times the profile area, so the total is <c>sum(relativeIntensities) * profile.TrueArea()</c>.
    /// </summary>
    public double TrueAreaTotal()
    {
        double sum = 0.0;
        foreach (double r in _relativeIntensities)
        {
            sum += r;
        }

        return sum * _profile.TrueArea();
    }

    /// <summary>
    /// Sample a fresh noisy realization of every fragment XIC at the given retention times.
    /// Returns one intensity array per fragment.
    /// </summary>
    public double[][] SampleFragments(double[] sampleRt, RandomSource rng)
    {
        var fragments = new double[_relativeIntensities.Length][];
        for (int f = 0; f < _relativeIntensities.Length; f++)
        {
            IReadOnlyList<IPeakModel>? interference = _perTransitionInterference?[f];
            var clean = new double[sampleRt.Length];
            for (int i = 0; i < sampleRt.Length; i++)
            {
                clean[i] = _relativeIntensities[f] * _profile.Evaluate(sampleRt[i]);
                if (interference is not null)
                {
                    foreach (IPeakModel peak in interference)
                    {
                        clean[i] += peak.Evaluate(sampleRt[i]);
                    }
                }
            }

            IBackground background = _perTransitionBackground?[f] ?? _background;
            fragments[f] = NoiseModel.Apply(sampleRt, clean, background, _noise, rng);
        }

        return fragments;
    }
}
