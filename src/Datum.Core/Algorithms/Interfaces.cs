//! The extensible algorithm contracts: detection, integration, background subtraction.

namespace Datum.Core.Algorithms;

/// <summary>Tunable parameters shared by peak detectors.</summary>
/// <param name="HeightFraction">Minimum apex height as a fraction of the max sampled intensity.</param>
/// <param name="Prominence">Minimum absolute prominence for a candidate peak.</param>
/// <param name="BoundaryRelHeight">
/// Relative height (0..1) at which integration boundaries are placed: 0 = apex, 1 = baseline.
/// 0.99 ~ very wide capture (notebook default); 0.5 ~ full width at half maximum.
/// </param>
/// <param name="BoundarySigmaMultiple">
/// For the improved Osprey integration model only: integration boundaries are placed at the apex
/// plus/minus this many estimated peak standard deviations (as fractional retention times). ~3.0 is
/// the sweet spot (leaves little for a linear baseline to over-subtract while losing a negligible
/// Gaussian tail); ~2.5 keeps the boundaries tighter. Ignored by all other detectors.
/// </param>
public readonly record struct DetectorParams(
    double HeightFraction = 0.5,
    double Prominence = 0.0,
    double BoundaryRelHeight = 0.99,
    double BoundarySigmaMultiple = 3.0)
{
    /// <summary>Sensible defaults matching the reference notebook.</summary>
    /// <remarks>Constructed explicitly: a record-struct <c>new()</c> would zero-init, not apply parameter defaults.</remarks>
    public static DetectorParams Default => new(HeightFraction: 0.5, Prominence: 0.0, BoundaryRelHeight: 0.99, BoundarySigmaMultiple: 3.0);
}

/// <summary>Options controlling area integration.</summary>
/// <param name="EdgeEstimation">
/// When true, add fractionally-interpolated points at the exact integration boundaries
/// (the "fractional trapezoid" edge estimate that removes low-sampling bias).
/// </param>
public readonly record struct IntegratorOptions(bool EdgeEstimation = false)
{
    /// <summary>Default options (no edge estimation).</summary>
    public static IntegratorOptions Default => new(EdgeEstimation: false);
}

/// <summary>Detects a peak and its integration boundaries on a sampled chromatogram.</summary>
public interface IPeakDetector
{
    /// <summary>Display name shown in the UI and used as a registry key.</summary>
    string Name { get; }

    /// <summary>
    /// Whether this detector reports sub-sample (interpolated) boundary retention times that fall
    /// between sampled points. Edge estimation only has an effect for such detectors: when the
    /// boundary is snapped to a sampled point (as Osprey and Skyline do) there is no gap for the
    /// fractional-trapezoid edge to fill, so the UI greys edge estimation. Defaults to false.
    /// </summary>
    bool ProducesFractionalBoundaries => false;

    /// <summary>Detect the dominant peak over the sampled trace.</summary>
    /// <param name="rt">Sample retention times (increasing).</param>
    /// <param name="intensity">Sample intensities.</param>
    /// <param name="p">Detector parameters.</param>
    PeakBounds Detect(double[] rt, double[] intensity, DetectorParams p);
}

/// <summary>Integrates the area under a detected peak.</summary>
public interface IIntegrator
{
    /// <summary>Display name shown in the UI and used as a registry key.</summary>
    string Name { get; }

    /// <summary>
    /// Whether this integrator honors <see cref="IntegratorOptions.EdgeEstimation"/> (the
    /// fractional-trapezoid edge fill). Only the trapezoid rule uses it; fit-based and sum-based
    /// integrators ignore it, so the UI greys the edge-estimation control for them. Defaults to false.
    /// </summary>
    bool SupportsEdgeEstimation => false;

    /// <summary>Integrate the area between the detected boundaries.</summary>
    /// <param name="rt">Sample retention times (increasing).</param>
    /// <param name="intensity">Sample intensities (already background-subtracted if applicable).</param>
    /// <param name="bounds">Detected peak boundaries.</param>
    /// <param name="options">Integration options.</param>
    double Integrate(double[] rt, double[] intensity, PeakBounds bounds, IntegratorOptions options);
}

/// <summary>
/// An integrator that quantifies all transitions jointly rather than one trace at a time. The
/// multi-transition engine calls <see cref="IntegrateAll"/> when an integrator implements this, so a
/// consensus model (e.g. a single EMG shape shared across transitions) can use information from the
/// clean transitions to recover the area of an interfered one. Integrators that do not implement this
/// are applied per transition independently.
/// </summary>
public interface IMultiTransitionIntegrator
{
    /// <summary>
    /// Integrate every transition jointly and return the per-transition areas (same order and length
    /// as <paramref name="subtractedFragments"/>). Fragments are already background-subtracted. A
    /// transition whose area cannot be determined is returned as NaN.
    /// </summary>
    /// <param name="rt">Sample retention times (increasing).</param>
    /// <param name="subtractedFragments">Background-subtracted intensities, one array per transition.</param>
    /// <param name="bounds">Shared detected peak boundaries.</param>
    /// <param name="options">Integration options.</param>
    double[] IntegrateAll(double[] rt, double[][] subtractedFragments, PeakBounds bounds, IntegratorOptions options);
}

/// <summary>Removes chemical background beneath a detected peak prior to integration.</summary>
public interface IBackgroundSubtractor
{
    /// <summary>Display name shown in the UI and used as a registry key.</summary>
    string Name { get; }

    /// <summary>
    /// Return a background-subtracted copy of <paramref name="intensity"/> for the sampled trace.
    /// </summary>
    /// <param name="rt">Sample retention times (increasing).</param>
    /// <param name="intensity">Sample intensities.</param>
    /// <param name="bounds">Detected peak boundaries.</param>
    double[] Subtract(double[] rt, double[] intensity, PeakBounds bounds);
}
