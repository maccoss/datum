//! A synthesized chromatogram: shared RT grid with ground-truth and noisy traces.

using Datum.Core.Models;

namespace Datum.Core.Simulation;

/// <summary>
/// A high-resolution chromatogram sharing one retention-time grid between the noise-free
/// ground-truth trace (peak + interference + background) and a noisy realization.
/// </summary>
public sealed class Chromatogram
{
    internal Chromatogram(double[] rt, double[] groundTruth, double[] noisy)
    {
        Rt = rt;
        GroundTruth = groundTruth;
        Noisy = noisy;
    }

    /// <summary>Retention-time grid.</summary>
    public double[] Rt { get; }

    /// <summary>Noise-free signal including interference and background.</summary>
    public double[] GroundTruth { get; }

    /// <summary>A noisy realization of <see cref="GroundTruth"/>.</summary>
    public double[] Noisy { get; }

    /// <summary>Linearly interpolate the noisy trace at an arbitrary retention time.</summary>
    public double InterpolateNoisy(double rt) => Interpolate(Rt, Noisy, rt);

    /// <summary>Linearly interpolate the ground-truth trace at an arbitrary retention time.</summary>
    public double InterpolateGroundTruth(double rt) => Interpolate(Rt, GroundTruth, rt);

    /// <summary>
    /// Linear interpolation of <paramref name="y"/> sampled at sorted <paramref name="x"/>.
    /// Values outside the range are clamped to the endpoints (matches numpy.interp).
    /// </summary>
    public static double Interpolate(double[] x, double[] y, double query)
    {
        if (query <= x[0])
        {
            return y[0];
        }

        if (query >= x[^1])
        {
            return y[^1];
        }

        int lo = System.Array.BinarySearch(x, query);
        if (lo >= 0)
        {
            return y[lo];
        }

        int hi = ~lo;
        int prev = hi - 1;
        double t = (query - x[prev]) / (x[hi] - x[prev]);
        return y[prev] + t * (y[hi] - y[prev]);
    }
}

/// <summary>
/// Builds <see cref="Chromatogram"/> instances from a peak, interference, background, and
/// noise. The clean trace is the main peak plus interference peaks; background and noise
/// are layered on per realization.
/// </summary>
public sealed class ChromatogramBuilder
{
    private readonly IPeakModel _mainPeak;
    private readonly IReadOnlyList<IPeakModel> _interference;
    private readonly IBackground _background;
    private readonly NoiseParameters _noise;
    private readonly double[] _rt;
    private readonly double[] _clean;

    /// <summary>
    /// Create a builder over a retention-time grid spanning the peak (plus margin).
    /// </summary>
    /// <param name="mainPeak">The analyte peak (ground-truth area comes from this).</param>
    /// <param name="interference">Co-eluting interference peaks (already placed).</param>
    /// <param name="background">Chemical background model.</param>
    /// <param name="noise">Noise parameters.</param>
    /// <param name="rtStart">Grid start retention time.</param>
    /// <param name="rtEnd">Grid end retention time.</param>
    /// <param name="resolution">Number of grid points across the chromatogram.</param>
    public ChromatogramBuilder(
        IPeakModel mainPeak,
        IReadOnlyList<IPeakModel> interference,
        IBackground background,
        NoiseParameters noise,
        double rtStart,
        double rtEnd,
        int resolution = 1000)
    {
        _mainPeak = mainPeak;
        _interference = interference;
        _background = background;
        _noise = noise;

        _rt = new double[resolution];
        _clean = new double[resolution];
        double dx = (rtEnd - rtStart) / (resolution - 1);
        for (int i = 0; i < resolution; i++)
        {
            double rt = rtStart + i * dx;
            _rt[i] = rt;
            double value = mainPeak.Evaluate(rt);
            foreach (var peak in interference)
            {
                value += peak.Evaluate(rt);
            }

            _clean[i] = value;
        }
    }

    /// <summary>Retention-time grid shared by all realizations.</summary>
    public double[] Rt => _rt;

    /// <summary>The analyte peak being quantified.</summary>
    public IPeakModel MainPeak => _mainPeak;

    /// <summary>The chemical background model.</summary>
    public IBackground Background => _background;

    /// <summary>Clean signal with background but no noise (for display/baseline reference).</summary>
    public double[] CleanWithBackground()
    {
        var trace = new double[_rt.Length];
        for (int i = 0; i < _rt.Length; i++)
        {
            trace[i] = _clean[i] + _background.Evaluate(_rt[i]);
        }

        return trace;
    }

    /// <summary>Build one chromatogram realization (full display grid) using the supplied random source.</summary>
    public Chromatogram Build(RandomSource rng)
    {
        double[] noisy = NoiseModel.Apply(_rt, _clean, _background, _noise, rng);
        return new Chromatogram(_rt, CleanWithBackground(), noisy);
    }

    /// <summary>
    /// Sample a fresh noisy realization directly at arbitrary retention times (no fine-grid
    /// interpolation, so it is independent of the display window). Used by the Monte-Carlo
    /// engine where sample points may extend beyond the display grid.
    /// </summary>
    public double[] SampleNoisy(double[] sampleRt, RandomSource rng)
    {
        var clean = new double[sampleRt.Length];
        for (int i = 0; i < sampleRt.Length; i++)
        {
            double rt = sampleRt[i];
            double value = _mainPeak.Evaluate(rt);
            foreach (var peak in _interference)
            {
                value += peak.Evaluate(rt);
            }

            clean[i] = value;
        }

        return NoiseModel.Apply(sampleRt, clean, _background, _noise, rng);
    }
}
