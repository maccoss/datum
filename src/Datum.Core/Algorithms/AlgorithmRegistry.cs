//! Central registry of available detection / integration / subtraction algorithms.

using Datum.Core.Algorithms.BackgroundSubtractors;
using Datum.Core.Algorithms.Detectors;
using Datum.Core.Algorithms.Integrators;

namespace Datum.Core.Algorithms;

/// <summary>
/// A registry of available algorithm implementations, keyed by display name. The UI binds
/// to <see cref="Detectors"/>, <see cref="Integrators"/>, and <see cref="BackgroundSubtractors"/>
/// so new implementations appear automatically once registered here.
/// </summary>
public sealed class AlgorithmRegistry
{
    private readonly Dictionary<string, IPeakDetector> _detectors;
    private readonly Dictionary<string, IIntegrator> _integrators;
    private readonly Dictionary<string, IBackgroundSubtractor> _subtractors;

    /// <summary>Create a registry preloaded with the built-in algorithms.</summary>
    public AlgorithmRegistry()
    {
        _detectors = new Dictionary<string, IPeakDetector>();
        _integrators = new Dictionary<string, IIntegrator>();
        _subtractors = new Dictionary<string, IBackgroundSubtractor>();

        // Note: the byte-identical Skyline detector/integrator live in Datum.Skyline (which
        // references this project) and are registered by the application at startup.
        Register(new ThresholdDetector());
        Register(new FindPeaksDetector());
        Register(new OspreyCwtDetector());
        Register(new OspreyCwtDetector(improved: true));

        Register(new TrapezoidIntegrator());
        Register(new RiemannSumIntegrator());
        Register(new GaussianFitIntegrator());
        Register(new EmgFitIntegrator());
        Register(new ConsensusEmgIntegrator());

        Register(new NoBackgroundSubtractor());
        Register(new ConstantBackgroundSubtractor());
        Register(new LinearBaselineSubtractor());
    }

    /// <summary>Registered peak detectors, in registration order.</summary>
    public IReadOnlyList<IPeakDetector> Detectors => _detectors.Values.ToList();

    /// <summary>Registered integrators, in registration order.</summary>
    public IReadOnlyList<IIntegrator> Integrators => _integrators.Values.ToList();

    /// <summary>Registered background subtractors, in registration order.</summary>
    public IReadOnlyList<IBackgroundSubtractor> BackgroundSubtractors => _subtractors.Values.ToList();

    /// <summary>Register (or replace) a peak detector.</summary>
    public void Register(IPeakDetector detector) => _detectors[detector.Name] = detector;

    /// <summary>Register (or replace) an integrator.</summary>
    public void Register(IIntegrator integrator) => _integrators[integrator.Name] = integrator;

    /// <summary>Register (or replace) a background subtractor.</summary>
    public void Register(IBackgroundSubtractor subtractor) => _subtractors[subtractor.Name] = subtractor;

    /// <summary>Look up a detector by name.</summary>
    public IPeakDetector GetDetector(string name) => _detectors[name];

    /// <summary>Look up an integrator by name.</summary>
    public IIntegrator GetIntegrator(string name) => _integrators[name];

    /// <summary>Look up a background subtractor by name.</summary>
    public IBackgroundSubtractor GetBackgroundSubtractor(string name) => _subtractors[name];
}
