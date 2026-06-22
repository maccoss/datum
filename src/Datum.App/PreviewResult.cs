//! Snapshot of the current model used to render the live preview plots.

using Datum.Core.Algorithms;

namespace Datum.App;

/// <summary>
/// The data needed to render the three live-preview plots (ground truth, noisy, sampled)
/// for the current parameter state. Built by the view model and consumed by the plotters.
/// </summary>
public sealed record PreviewResult
{
    /// <summary>High-resolution display retention-time grid.</summary>
    public required double[] Rt { get; init; }

    /// <summary>Noise-free analyte peak only (no interference or background).</summary>
    public required double[] GroundTruthMain { get; init; }

    /// <summary>Noise-free total signal: analyte + interference + background.</summary>
    public required double[] GroundTruthTotal { get; init; }

    /// <summary>Noisy realization of the total signal.</summary>
    public required double[] Noisy { get; init; }

    /// <summary>The chemical background baseline over the display grid.</summary>
    public required double[] BackgroundCurve { get; init; }

    /// <summary>Retention times of the sampled points (flanking points included).</summary>
    public required double[] SampleRt { get; init; }

    /// <summary>Sampled (noisy) intensities at <see cref="SampleRt"/>.</summary>
    public required double[] SampleIntensity { get; init; }

    /// <summary>Sampled intensities after background subtraction.</summary>
    public required double[] SubtractedIntensity { get; init; }

    /// <summary>Detected peak boundaries on the sampled trace.</summary>
    public required PeakBounds Bounds { get; init; }

    /// <summary>Reference peak start used to define points-across-peak.</summary>
    public required double ReferenceStart { get; init; }

    /// <summary>Reference peak end used to define points-across-peak.</summary>
    public required double ReferenceEnd { get; init; }

    /// <summary>Ground-truth integrated area.</summary>
    public required double TrueArea { get; init; }

    /// <summary>Area recovered by the selected algorithms on this realization.</summary>
    public required double SampledArea { get; init; }

    /// <summary>Whether a baseline should be drawn (i.e. background subtraction is active).</summary>
    public required bool SubtractsBackground { get; init; }

    /// <summary>Whether edge estimation is on (the integrated area reaches the exact boundaries).</summary>
    public required bool EdgeEstimation { get; init; }

    /// <summary>The nominal points-across-the-peak setting used for sampling.</summary>
    public required int PointsAcrossPeak { get; init; }

    // ---- Sampled-plot display options ----

    /// <summary>Sampled plot: draw the faint full-resolution noisy trace overlay (the "transitions
    /// with noise" curve). Turning it off makes the sampled points and their connecting lines stand out.</summary>
    public bool ShowNoisyTrace { get; init; } = true;

    /// <summary>Sampled plot: draw the sample-point markers. Turning it off leaves only the solid
    /// lines drawn between the sampled points (the piecewise-linear trace the trapezoid integrates).</summary>
    public bool ShowSamplePoints { get; init; } = true;

    // ---- Multi-transition overlays (null in single-trace mode) ----

    /// <summary>Per-transition noise-free traces over <see cref="Rt"/> (multi-transition mode).</summary>
    public double[][]? FragmentGroundTruth { get; init; }

    /// <summary>Per-transition noisy traces over <see cref="Rt"/> (multi-transition mode).</summary>
    public double[][]? FragmentNoisy { get; init; }

    /// <summary>Per-transition sampled intensities at <see cref="SampleRt"/> (multi-transition mode).</summary>
    public double[][]? FragmentSamples { get; init; }

    /// <summary>Per-transition labels (e.g. "y7+1") for the legend (multi-transition mode).</summary>
    public string[]? FragmentLabels { get; init; }

    /// <summary>True when per-transition overlays are present.</summary>
    public bool IsMultiTransition => FragmentNoisy is { Length: > 0 };
}
