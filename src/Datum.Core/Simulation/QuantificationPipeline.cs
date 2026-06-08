//! Sample -> detect -> subtract -> integrate pipeline for a single realization.

using Datum.Core.Algorithms;

namespace Datum.Core.Simulation;

/// <summary>The per-realization outcome of the quantification pipeline.</summary>
/// <param name="Area">Integrated area for this realization.</param>
/// <param name="SampleRt">Retention times of the sampled points.</param>
/// <param name="SampleIntensity">Sampled (noisy) intensities, before background subtraction.</param>
/// <param name="SubtractedIntensity">Intensities after background subtraction.</param>
/// <param name="Bounds">Detected peak boundaries.</param>
public sealed record PipelineResult(
    double Area,
    double[] SampleRt,
    double[] SampleIntensity,
    double[] SubtractedIntensity,
    PeakBounds Bounds);

/// <summary>
/// Configurable choice of algorithms and parameters for one quantification run. Bundles a
/// detector, integrator, and background subtractor together with their options.
/// </summary>
public sealed class QuantificationPipeline
{
    /// <summary>Create a pipeline from explicit algorithm choices.</summary>
    public QuantificationPipeline(
        IPeakDetector detector,
        IBackgroundSubtractor backgroundSubtractor,
        IIntegrator integrator,
        DetectorParams detectorParams,
        IntegratorOptions integratorOptions)
    {
        Detector = detector;
        BackgroundSubtractor = backgroundSubtractor;
        Integrator = integrator;
        DetectorParams = detectorParams;
        IntegratorOptions = integratorOptions;
    }

    /// <summary>The peak detector.</summary>
    public IPeakDetector Detector { get; }

    /// <summary>The background subtractor applied before integration.</summary>
    public IBackgroundSubtractor BackgroundSubtractor { get; }

    /// <summary>The area integrator.</summary>
    public IIntegrator Integrator { get; }

    /// <summary>Detector parameters.</summary>
    public DetectorParams DetectorParams { get; }

    /// <summary>Integrator options.</summary>
    public IntegratorOptions IntegratorOptions { get; }

    /// <summary>
    /// Detect the peak on the sampled trace, subtract background, and integrate. Returns the
    /// area along with the sampled trace for display.
    /// </summary>
    /// <param name="sampleRt">Sample retention times (increasing).</param>
    /// <param name="sampleIntensity">Sampled (noisy) intensities at those retention times.</param>
    public PipelineResult Run(double[] sampleRt, double[] sampleIntensity)
    {
        PeakBounds bounds = Detector.Detect(sampleRt, sampleIntensity, DetectorParams);
        double[] subtracted = BackgroundSubtractor.Subtract(sampleRt, sampleIntensity, bounds);
        double area = Integrator.Integrate(sampleRt, subtracted, bounds, IntegratorOptions);

        return new PipelineResult(area, sampleRt, sampleIntensity, subtracted, bounds);
    }
}
