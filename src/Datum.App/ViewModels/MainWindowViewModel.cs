//! Main view model: parameter state, live preview, and the Monte-Carlo sweep command.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Datum.Core.Algorithms;
using Datum.Core.Algorithms.Detectors;
using Datum.Core.Models;
using Datum.Core.Simulation;
using Datum.Koina;
using Datum.Skyline;

namespace Datum.App.ViewModels;

/// <summary>
/// Drives the whole UI: holds every peak/noise/interference/sampling/method parameter as an
/// observable property, rebuilds the live preview (debounced) whenever a parameter changes,
/// and runs the Monte-Carlo points-across-peak sweep on a background thread.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private const string ScipyDetectorName = "Peak finder (scipy)";
    private const string OspreyDetectorName = "Osprey CWT";
    private const string OspreyImprovedDetectorName = "Osprey CWT (improved)";
    private const string SkylineBoundariesOnly = "Skyline (boundaries only)";
    private const string SkylineExactArea = "Skyline (exact area)";
    private const string SkylineIntegratorName = "Skyline area (exact)";
    private const string NoSubtractorName = "None";

    private readonly AlgorithmRegistry _registry = new();
    private readonly KoinaClient _koina = new();
    private readonly DispatcherTimer _previewTimer;
    private CancellationTokenSource? _cts;
    private bool _suppressPreview;

    /// <summary>Top-N predicted fragments (from Koina or the offline fallback).</summary>
    public ObservableCollection<FragmentPrediction> Fragments { get; } = new();

    /// <summary>Per-point-count results of the most recent Monte-Carlo sweep (for the results table).</summary>
    public ObservableCollection<SweepRow> SweepResults { get; } = new();

    /// <summary>Raised when the live-preview data should be redrawn.</summary>
    public event Action<PreviewResult>? PreviewUpdated;

    /// <summary>Raised when a Monte-Carlo sweep finishes, with its deviation curve.</summary>
    public event Action<IReadOnlyList<DeviationResult>>? SimulationCompleted;

    /// <summary>Initialize collections, defaults, and the debounce timer.</summary>
    public MainWindowViewModel()
    {
        PeakTypes = new ObservableCollection<string> { "Gaussian", "Skew-normal", "EMG" };
        BackgroundTypes = new ObservableCollection<string> { "None", "Constant", "Linear", "Curved" };
        DetectorNames = new ObservableCollection<string>();
        IntegratorNames = new ObservableCollection<string>();
        SubtractorNames = new ObservableCollection<string>();

        // Byte-identical Skyline algorithms live in Datum.Skyline; register them here.
        _registry.Register(new SkylineExactDetector());
        _registry.Register(new SkylineExactIntegrator());

        foreach (var d in _registry.Detectors)
        {
            DetectorNames.Add(d.Name);
            if (d.Name == SkylineBoundariesOnly)
            {
                // Second Skyline mode: same boundaries, but Skyline's exact area calculation.
                DetectorNames.Add(SkylineExactArea);
            }
        }

        foreach (var i in _registry.Integrators)
        {
            IntegratorNames.Add(i.Name);
        }

        foreach (var s in _registry.BackgroundSubtractors)
        {
            SubtractorNames.Add(s.Name);
        }

        _selectedPeakType = "Gaussian";
        _selectedBackgroundType = "Constant";
        _selectedDetector = "Peak finder (scipy)";
        _selectedIntegrator = "Trapezoid";
        _selectedSubtractor = "Linear baseline";

        // Preload the built-in fragments so multi-transition works offline at startup.
        foreach (FragmentPrediction fragment in DefaultFragments)
        {
            Fragments.Add(fragment);
        }

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _previewTimer.Tick += (_, _) =>
        {
            _previewTimer.Stop();
            RefreshPreview();
        };

        PropertyChanged += (_, e) =>
        {
            if (_suppressPreview || e.PropertyName is null)
            {
                return;
            }

            if (PreviewIrrelevant.Contains(e.PropertyName))
            {
                return;
            }

            SchedulePreview();
        };
    }

    private static readonly HashSet<string> PreviewIrrelevant = new()
    {
        nameof(IsRunning), nameof(Progress), nameof(ProgressText),
        nameof(TrueAreaText), nameof(SampledAreaText), nameof(DeviationText), nameof(PointsText), nameof(StatusText),
        nameof(KoinaStatus), nameof(SweepSummary), nameof(MultiTransitionBoundaryInfo),
        nameof(ShowSkew), nameof(ShowTau),
    };

    // ---- Peak shape ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSkew))]
    [NotifyPropertyChangedFor(nameof(ShowTau))]
    private string _selectedPeakType;

    [ObservableProperty] private double _height = 1000;
    [ObservableProperty] private double _center = 8.0;
    [ObservableProperty] private double _sigma = 0.3;
    [ObservableProperty] private double _skew = 3.0;
    [ObservableProperty] private double _tau = 0.3;

    // ---- Background ----
    [ObservableProperty] private string _selectedBackgroundType;
    [ObservableProperty] private double _backgroundLevel = 50;
    [ObservableProperty] private double _backgroundSlope = 0;
    [ObservableProperty] private double _backgroundCurvature = 0;

    // ---- Noise ----
    [ObservableProperty] private double _gaussianNoise = 20;
    [ObservableProperty] private bool _usePoisson = true;
    [ObservableProperty] private int _seed = 12345;

    // ---- Interference ----
    [ObservableProperty] private bool _interferenceEnabled;
    [ObservableProperty] private double _interferenceRelAmplitude = 0.3;
    [ObservableProperty] private double _interferenceOffsetSigma = -2.5;
    [ObservableProperty] private double _interferenceSigmaRatio = 1.0;
    [ObservableProperty] private double _interferenceSkew = 0;

    // Multi-transition only: how many transitions carry the interference, and whether each
    // transition gets its own background level (spread across 0.5x..1.5x of the set level).
    [ObservableProperty] private int _interferedTransitionCount = 1;
    [ObservableProperty] private bool _varyBackgroundPerTransition;

    // ---- Multi-transition / Koina ----
    [ObservableProperty] private string _peptide = "ELVISLIVESR";
    [ObservableProperty] private int _precursorCharge = 2;
    [ObservableProperty] private double _collisionEnergy = 25;
    [ObservableProperty] private int _topN = 6;
    [ObservableProperty] private bool _useMultiTransition = true;
    [ObservableProperty] private string _koinaStatus = "Built-in fragments for ELVISLIVESR (2+); Fetch to refresh from Koina.";

    // Built-in top-6 fragments for ELVISLIVESR (2+, NCE 25) from Prosit_2020_intensity_HCD, so
    // multi-transition works out of the box without a network call. Fetch overwrites these.
    private static readonly FragmentPrediction[] DefaultFragments =
    {
        new("y7+1", 803.4622, 1.000000),
        new("y8+1", 916.5462, 0.666087),
        new("b2+1", 243.1339, 0.499574),
        new("y5+1", 603.3461, 0.448614),
        new("b3+1", 342.2024, 0.422871),
        new("y4+1", 490.2620, 0.335546),
    };

    // ---- Sampling (live preview) ----
    [ObservableProperty] private int _pointsAcrossPeak = 10;
    [ObservableProperty] private double _sampleOffset = 0.5;

    // ---- Sampled-plot display toggles (above the bottom-left plot) ----
    [ObservableProperty] private bool _showNoisyTrace = true;
    [ObservableProperty] private bool _showSamplePoints = true;

    // ---- Methods ----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScipyDetector))]
    [NotifyPropertyChangedFor(nameof(DetectorUsesBoundaryRelHeight))]
    [NotifyPropertyChangedFor(nameof(IsExactSkyline))]
    [NotifyPropertyChangedFor(nameof(AreaOptionsEnabled))]
    [NotifyPropertyChangedFor(nameof(EdgeEstimationApplies))]
    [NotifyPropertyChangedFor(nameof(MultiTransitionBoundaryInfo))]
    [NotifyPropertyChangedFor(nameof(IsOspreyImprovedDetector))]
    private string _selectedDetector;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EdgeEstimationApplies))]
    private string _selectedIntegrator;
    [ObservableProperty] private string _selectedSubtractor;
    [ObservableProperty] private bool _edgeEstimation = true;
    [ObservableProperty] private double _boundaryRelHeight = 0.99;
    [ObservableProperty] private double _boundarySigmaMultiple = 3.0;
    [ObservableProperty] private double _heightFraction = 0.5;
    [ObservableProperty] private double _prominence = 0;

    // ---- Simulation ----
    [ObservableProperty] private int _minPoints = 2;
    [ObservableProperty] private int _maxPoints = 30;
    [ObservableProperty] private int _iterations = 100;
    [ObservableProperty] private int _simSeed = 12345;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressText = string.Empty;

    // ---- Outputs ----
    [ObservableProperty] private string _trueAreaText = string.Empty;
    [ObservableProperty] private string _sampledAreaText = string.Empty;
    [ObservableProperty] private string _deviationText = string.Empty;
    [ObservableProperty] private string _pointsText = string.Empty;
    [ObservableProperty] private string _statusText = "Ready.";
    [ObservableProperty] private string _sweepSummary = "Run a simulation to populate the table.";

    /// <summary>Whether the skew parameter applies (skew-normal peak only).</summary>
    public bool ShowSkew => SelectedPeakType == "Skew-normal";

    /// <summary>Whether the tau parameter applies (EMG peak only).</summary>
    public bool ShowTau => SelectedPeakType == "EMG";

    /// <summary>Available peak shapes.</summary>
    public ObservableCollection<string> PeakTypes { get; }

    /// <summary>Available background models.</summary>
    public ObservableCollection<string> BackgroundTypes { get; }

    /// <summary>Available peak detectors.</summary>
    public ObservableCollection<string> DetectorNames { get; }

    /// <summary>Available integrators.</summary>
    public ObservableCollection<string> IntegratorNames { get; }

    /// <summary>Available background subtractors.</summary>
    public ObservableCollection<string> SubtractorNames { get; }

    /// <summary>True when the scipy peak finder is selected (enables height fraction / prominence).</summary>
    public bool IsScipyDetector => SelectedDetector == ScipyDetectorName;

    /// <summary>True for detectors that use the boundary relative height (all except the Osprey CWT variants).</summary>
    public bool DetectorUsesBoundaryRelHeight => !IsOspreyDetector;

    /// <summary>True for any Osprey CWT variant (faithful index-based or the improved integration model).</summary>
    private bool IsOspreyDetector => SelectedDetector.StartsWith(OspreyDetectorName, System.StringComparison.Ordinal);

    /// <summary>True for the improved Osprey integration model (enables the boundary sigma-multiple control).</summary>
    public bool IsOspreyImprovedDetector => SelectedDetector == OspreyImprovedDetectorName;

    /// <summary>True in the "Skyline (exact area)" mode, where Skyline's own area calculation is used.</summary>
    public bool IsExactSkyline => SelectedDetector == SkylineExactArea;

    /// <summary>
    /// Whether the area-calculation controls (integrator, edge estimation, background subtraction)
    /// are available. They are greyed in the exact-Skyline mode, which fixes the area method.
    /// </summary>
    public bool AreaOptionsEnabled => !IsExactSkyline;

    /// <summary>
    /// Whether the edge-estimation control applies: only when the area options are available and
    /// the selected integrator actually honors edge estimation (the trapezoid rule). Greyed for
    /// fit-based and sum-based integrators, which ignore it.
    /// </summary>
    public bool EdgeEstimationApplies =>
        AreaOptionsEnabled
        && _registry.GetIntegrator(SelectedIntegrator).SupportsEdgeEstimation
        && _registry.GetDetector(SelectedDetector).ProducesFractionalBoundaries;

    /// <summary>
    /// Describes how the shared peak boundary is determined for the selected detector when
    /// quantifying multiple transitions (see <c>MultiTransitionEngine.DetectSharedBounds</c>).
    /// </summary>
    public string MultiTransitionBoundaryInfo
    {
        get
        {
            if (IsOspreyDetector)
            {
                return "Boundary: Osprey median-CWT consensus across all transitions.";
            }

            if (SelectedDetector is SkylineBoundariesOnly or SkylineExactArea)
            {
                return "Boundary: Skyline \"Integrate all\" — one boundary from the summed transitions, applied to all.";
            }

            return $"Boundary: {SelectedDetector} run on the summed transitions, applied to all.";
        }
    }

    /// <summary>Render the very first preview (call after the view is ready).</summary>
    public void Initialize() => RefreshPreview();

    private void SchedulePreview()
    {
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    // ---- Model construction ----

    private IPeakModel BuildPeak() => SelectedPeakType switch
    {
        "Skew-normal" => new SkewNormalPeak(Height, Center, Sigma, Skew),
        "EMG" => new EmgPeak(Height, Center, Sigma, System.Math.Max(1e-3, Tau)),
        _ => new GaussianPeak(Height, Center, Sigma),
    };

    private IBackground BuildBackground() => SelectedBackgroundType switch
    {
        "Constant" => new ConstantBackground(BackgroundLevel),
        "Linear" => new LinearBackground(BackgroundLevel, BackgroundSlope, Center),
        "Curved" => new CurvedBackground(BackgroundLevel, BackgroundSlope, BackgroundCurvature, Center),
        _ => new NoBackground(),
    };

    private IReadOnlyList<IPeakModel> BuildInterference(IPeakModel main)
    {
        if (!InterferenceEnabled)
        {
            return Array.Empty<IPeakModel>();
        }

        var spec = new InterferenceSpec(InterferenceRelAmplitude, InterferenceOffsetSigma, InterferenceSigmaRatio, InterferenceSkew);
        return new[] { spec.ToPeak(main) };
    }

    /// <summary>
    /// Per-transition interference for the multi-transition path: the configured interference peak is
    /// placed on the first <see cref="InterferedTransitionCount"/> transitions, the rest are clean.
    /// Returns null (no per-transition interference) when interference is disabled or the count is 0.
    /// </summary>
    private IReadOnlyList<IReadOnlyList<IPeakModel>>? BuildPerTransitionInterference(IPeakModel main, int transitionCount)
    {
        if (!InterferenceEnabled || InterferedTransitionCount <= 0)
        {
            return null;
        }

        IReadOnlyList<IPeakModel> interfered = BuildInterference(main);
        IReadOnlyList<IPeakModel> clean = Array.Empty<IPeakModel>();
        int k = System.Math.Min(InterferedTransitionCount, transitionCount);
        var list = new IReadOnlyList<IPeakModel>[transitionCount];
        for (int f = 0; f < transitionCount; f++)
        {
            list[f] = f < k ? interfered : clean;
        }

        return list;
    }

    /// <summary>
    /// Per-transition background for the multi-transition path: each transition's background is the
    /// configured background scaled by a factor spread evenly across 0.5x..1.5x, so the transitions
    /// carry genuinely different background levels. Returns null when the option is off.
    /// </summary>
    private IReadOnlyList<IBackground>? BuildPerTransitionBackground(int transitionCount)
    {
        if (!VaryBackgroundPerTransition)
        {
            return null;
        }

        var list = new IBackground[transitionCount];
        for (int f = 0; f < transitionCount; f++)
        {
            double factor = transitionCount > 1 ? 0.5 + 1.0 * f / (transitionCount - 1) : 1.0;
            list[f] = ScaledBackground(factor);
        }

        return list;
    }

    /// <summary>The configured background with its level (and slope/curvature) scaled by a factor.</summary>
    private IBackground ScaledBackground(double factor) => SelectedBackgroundType switch
    {
        "Constant" => new ConstantBackground(BackgroundLevel * factor),
        "Linear" => new LinearBackground(BackgroundLevel * factor, BackgroundSlope * factor, Center),
        "Curved" => new CurvedBackground(BackgroundLevel * factor, BackgroundSlope * factor, BackgroundCurvature * factor, Center),
        _ => new NoBackground(),
    };

    private NoiseParameters BuildNoise() => new(System.Math.Max(0, GaussianNoise), UsePoisson);

    private DetectorParams BuildDetectorParams() =>
        new(HeightFraction, System.Math.Max(0, Prominence), System.Math.Clamp(BoundaryRelHeight, 0.0, 0.999),
            System.Math.Clamp(BoundarySigmaMultiple, 1.0, 6.0));

    // Both Skyline modes detect with the Skyline detector; exact-area mode additionally fixes the
    // area calculation to Skyline's, so the integrator/edge/background controls do not apply.
    private IPeakDetector EffectiveDetector() => IsExactSkyline
        ? _registry.GetDetector(SkylineBoundariesOnly)
        : _registry.GetDetector(SelectedDetector);

    private IIntegrator EffectiveIntegrator() => IsExactSkyline
        ? _registry.GetIntegrator(SkylineIntegratorName)
        : _registry.GetIntegrator(SelectedIntegrator);

    private IBackgroundSubtractor EffectiveSubtractor() => IsExactSkyline
        ? _registry.GetBackgroundSubtractor(NoSubtractorName) // Skyline integrator does its own linear background.
        : _registry.GetBackgroundSubtractor(SelectedSubtractor);

    private IntegratorOptions EffectiveIntegratorOptions() =>
        new(EdgeEstimation: EdgeEstimationApplies && EdgeEstimation);

    private QuantificationPipeline BuildPipeline() => new(
        EffectiveDetector(),
        EffectiveSubtractor(),
        EffectiveIntegrator(),
        BuildDetectorParams(),
        EffectiveIntegratorOptions());

    // Sampling reference width is fixed at the 1%-of-apex peak extent (notebook convention),
    // independent of the detector's boundary relative height (which only affects detection).
    private static double BoundaryFraction() => 0.01;

    private ChromatogramBuilder BuildBuilder(IPeakModel peak, double lo, double hi) => new(
        peak, BuildInterference(peak), BuildBackground(), BuildNoise(), lo, hi, resolution: 1200);

    // ---- Live preview ----

    private void RefreshPreview()
    {
        try
        {
            IPeakModel peak = BuildPeak();
            int points = System.Math.Max(1, PointsAcrossPeak);
            SamplingGrid grid = SamplingGrid.Create(peak, points, SampleOffset, BoundaryFraction());

            double peakLo = peak.Center - 6.0 * peak.Sigma;
            double peakHi = peak.ApexRt + 6.0 * peak.Sigma + 6.0 * (SelectedPeakType == "EMG" ? Tau : 0.0);
            double lo = System.Math.Min(grid.Rt[0], peakLo);
            double hi = System.Math.Max(grid.Rt[^1], peakHi);
            double margin = 0.03 * (hi - lo);
            lo -= margin;
            hi += margin;

            ChromatogramBuilder builder = BuildBuilder(peak, lo, hi);
            Chromatogram chrom = builder.Build(new RandomSource(Seed));

            var groundTruthMain = new double[chrom.Rt.Length];
            var backgroundCurve = new double[chrom.Rt.Length];
            IBackground bg = BuildBackground();
            for (int i = 0; i < chrom.Rt.Length; i++)
            {
                groundTruthMain[i] = peak.Evaluate(chrom.Rt[i]);
                backgroundCurve[i] = bg.Evaluate(chrom.Rt[i]);
            }

            var sampleIntensity = new double[grid.Rt.Length];
            for (int i = 0; i < grid.Rt.Length; i++)
            {
                sampleIntensity[i] = Chromatogram.Interpolate(chrom.Rt, chrom.Noisy, grid.Rt[i]);
            }

            PreviewResult preview = UseMultiTransition && Fragments.Count > 0
                ? BuildMultiTransitionPreview(peak, bg, chrom, grid, backgroundCurve)
                : BuildSingleTracePreview(peak, chrom, grid, groundTruthMain, backgroundCurve, sampleIntensity);

            double trueArea = preview.TrueArea;
            bool areaOk = double.IsFinite(preview.SampledArea);
            double deviation = areaOk && trueArea > 0 ? (preview.SampledArea - trueArea) / trueArea * 100.0 : 0.0;
            int integrated = 0;
            if (preview.Bounds.Detected)
            {
                int s = System.Math.Max(0, preview.Bounds.StartIndex);
                int e = System.Math.Min(preview.SampleRt.Length - 1, preview.Bounds.EndIndex);
                integrated = e >= s ? e - s + 1 : 0;
            }

            _suppressPreview = true;
            TrueAreaText = $"True area: {trueArea:N1}";
            SampledAreaText = areaOk ? $"Sampled area: {preview.SampledArea:N1}" : "Sampled area: NA";
            DeviationText = areaOk ? $"Deviation: {deviation:+0.0;-0.0;0.0}%" : "Deviation: NA (fit unavailable)";
            PointsText = $"Points: {points} across peak, {integrated} integrated";
            _suppressPreview = false;

            PreviewUpdated?.Invoke(preview);
        }
        catch (Exception ex)
        {
            _suppressPreview = true;
            StatusText = $"Preview error: {ex.Message}";
            _suppressPreview = false;
        }
    }

    private PreviewResult BuildSingleTracePreview(
        IPeakModel peak, Chromatogram chrom, SamplingGrid grid,
        double[] groundTruthMain, double[] backgroundCurve, double[] sampleIntensity)
    {
        PipelineResult result = BuildPipeline().Run(grid.Rt, sampleIntensity);
        return new PreviewResult
        {
            Rt = chrom.Rt,
            GroundTruthMain = groundTruthMain,
            GroundTruthTotal = chrom.GroundTruth,
            Noisy = chrom.Noisy,
            BackgroundCurve = backgroundCurve,
            SampleRt = grid.Rt,
            SampleIntensity = sampleIntensity,
            SubtractedIntensity = result.SubtractedIntensity,
            Bounds = result.Bounds,
            ReferenceStart = grid.ReferenceStart,
            ReferenceEnd = grid.ReferenceEnd,
            TrueArea = peak.TrueArea(),
            SampledArea = result.Area,
            SubtractsBackground = SelectedSubtractor != "None",
            EdgeEstimation = EdgeEstimation,
            PointsAcrossPeak = System.Math.Max(1, PointsAcrossPeak),
            ShowNoisyTrace = ShowNoisyTrace,
            ShowSamplePoints = ShowSamplePoints,
        };
    }

    private PreviewResult BuildMultiTransitionPreview(
        IPeakModel peak, IBackground bg, Chromatogram chrom, SamplingGrid grid, double[] backgroundCurve)
    {
        double[] rel = Fragments.Select(f => f.RelativeIntensity).ToArray();
        string[] labels = Fragments.Select(f => f.Annotation).ToArray();
        var builder = new MultiTransitionBuilder(
            peak, rel, bg, BuildNoise(),
            BuildPerTransitionInterference(peak, rel.Length),
            BuildPerTransitionBackground(rel.Length));

        // Per-transition noise-free and noisy display traces.
        int nDisp = chrom.Rt.Length;
        var fragGt = new double[rel.Length][];
        for (int f = 0; f < rel.Length; f++)
        {
            fragGt[f] = new double[nDisp];
            for (int i = 0; i < nDisp; i++)
            {
                fragGt[f][i] = rel[f] * peak.Evaluate(chrom.Rt[i]);
            }
        }

        double[][] fragNoisy = builder.SampleFragments(chrom.Rt, new RandomSource(Seed));

        // Sampled points lie on the displayed noisy traces (interpolate, like the single-trace preview).
        var fragSamples = new double[rel.Length][];
        for (int f = 0; f < rel.Length; f++)
        {
            fragSamples[f] = new double[grid.Rt.Length];
            for (int j = 0; j < grid.Rt.Length; j++)
            {
                fragSamples[f][j] = Chromatogram.Interpolate(chrom.Rt, fragNoisy[f], grid.Rt[j]);
            }
        }

        PeakBounds bounds = MultiTransitionEngine.DetectSharedBounds(
            EffectiveDetector(), grid.Rt, fragSamples, BuildDetectorParams());
        IIntegrator integrator = EffectiveIntegrator();
        IBackgroundSubtractor subtractor = EffectiveSubtractor();
        var options = EffectiveIntegratorOptions();

        double sampledArea = MultiTransitionEngine.IntegrateFragments(
            integrator, subtractor, grid.Rt, fragSamples, bounds, options);

        // Summed traces for fields shared with the single-trace plots / area readout.
        double[] sumGt = SumTraces(fragGt, nDisp);
        double[] sumNoisy = SumTraces(fragNoisy, nDisp);
        double[] sumGtTotal = new double[nDisp];
        for (int i = 0; i < nDisp; i++)
        {
            sumGtTotal[i] = sumGt[i] + backgroundCurve[i];
        }

        double[] sumSample = SumTraces(fragSamples, grid.Rt.Length);

        return new PreviewResult
        {
            Rt = chrom.Rt,
            GroundTruthMain = sumGt,
            GroundTruthTotal = sumGtTotal,
            Noisy = sumNoisy,
            BackgroundCurve = backgroundCurve,
            SampleRt = grid.Rt,
            SampleIntensity = sumSample,
            SubtractedIntensity = sumSample,
            Bounds = bounds,
            ReferenceStart = grid.ReferenceStart,
            ReferenceEnd = grid.ReferenceEnd,
            TrueArea = builder.TrueAreaTotal(),
            SampledArea = sampledArea,
            SubtractsBackground = SelectedSubtractor != "None",
            EdgeEstimation = EdgeEstimation,
            PointsAcrossPeak = System.Math.Max(1, PointsAcrossPeak),
            ShowNoisyTrace = ShowNoisyTrace,
            ShowSamplePoints = ShowSamplePoints,
            FragmentGroundTruth = fragGt,
            FragmentNoisy = fragNoisy,
            FragmentSamples = fragSamples,
            FragmentLabels = labels,
        };
    }

    private static double[] SumTraces(double[][] traces, int length)
    {
        var sum = new double[length];
        foreach (double[] trace in traces)
        {
            for (int i = 0; i < length; i++)
            {
                sum[i] += trace[i];
            }
        }

        return sum;
    }

    // ---- Monte-Carlo sweep ----

    /// <summary>Run the points-across-peak sweep on a background thread.</summary>
    [RelayCommand]
    private async Task RunSimulationAsync()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        Progress = 0;
        StatusText = "Running simulation...";
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        IPeakModel peak = BuildPeak();
        double lo = peak.Center - 8.0 * peak.Sigma;
        double hi = peak.ApexRt + 10.0 * peak.Sigma + 8.0 * (SelectedPeakType == "EMG" ? Tau : 0.0);
        ChromatogramBuilder builder = BuildBuilder(peak, lo, hi);
        QuantificationPipeline pipeline = BuildPipeline();
        var settings = new SimulationSettings(
            System.Math.Max(2, MinPoints), System.Math.Max(MinPoints + 1, MaxPoints),
            System.Math.Max(2, Iterations), SimSeed, BoundaryFraction());
        bool multi = UseMultiTransition && Fragments.Count > 0;

        var progress = new Progress<SimulationProgress>(p =>
        {
            Progress = p.Total > 0 ? (double)p.Completed / p.Total : 0;
            ProgressText = $"{p.Completed}/{p.Total} point counts";
        });

        try
        {
            IReadOnlyList<DeviationResult> results = multi
                ? await Task.Run(() => RunMultiTransition(peak, settings, progress, token), token)
                : await Task.Run(() => new SimulationEngine().Run(builder, pipeline, settings, progress, token), token);

            SweepResults.Clear();
            foreach (DeviationResult r in results)
            {
                SweepResults.Add(SweepRow.From(r));
            }

            SweepSummary = results.Count > 0
                ? $"True area {results[0].TrueArea:N1}  ·  {results.Count} point counts × {settings.Iterations} iterations"
                : "No results.";
            SimulationCompleted?.Invoke(results);
            string boundary = IsExactSkyline || SelectedDetector.StartsWith("Skyline", StringComparison.Ordinal)
                ? "Skyline integrate-all"
                : IsOspreyDetector ? "Osprey consensus" : $"{SelectedDetector} on summed";
            string mode = multi ? $"multi-transition ({Fragments.Count} fragments, {boundary})" : "single trace";
            StatusText = $"Done [{mode}]: {results.Count} point counts, {settings.Iterations} iterations each.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Simulation cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Simulation error: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            Progress = 0;
            ProgressText = string.Empty;
        }
    }

    private IReadOnlyList<DeviationResult> RunMultiTransition(
        IPeakModel peak, SimulationSettings settings, IProgress<SimulationProgress> progress, CancellationToken token)
    {
        double[] relative = Fragments.Select(f => f.RelativeIntensity).ToArray();
        var builder = new MultiTransitionBuilder(
            peak, relative, BuildBackground(), BuildNoise(),
            BuildPerTransitionInterference(peak, relative.Length),
            BuildPerTransitionBackground(relative.Length));
        return new MultiTransitionEngine().Run(
            builder, EffectiveDetector(), EffectiveIntegrator(), EffectiveSubtractor(), BuildDetectorParams(),
            EffectiveIntegratorOptions(), settings, progress, token);
    }

    /// <summary>Fetch top-N fragment intensities from Koina (with an offline fallback).</summary>
    [RelayCommand]
    private async Task FetchKoinaAsync()
    {
        KoinaStatus = "Fetching from Koina...";
        try
        {
            IReadOnlyList<FragmentPrediction> predictions = await _koina.PredictAsync(
                Peptide, PrecursorCharge, CollisionEnergy, System.Math.Max(1, TopN));
            Fragments.Clear();
            foreach (FragmentPrediction p in predictions)
            {
                Fragments.Add(p);
            }

            KoinaStatus = Fragments.Count > 0
                ? $"Loaded {Fragments.Count} fragments for {Peptide} ({PrecursorCharge}+)."
                : "Koina returned no usable fragments.";
            SchedulePreview();
        }
        catch (Exception ex)
        {
            string reason = ex.Message.Split('\n')[0];
            if (Fragments.Count > 0)
            {
                // Keep the fragments already loaded (e.g. the built-in set) rather than discarding them.
                KoinaStatus = $"Warning: Koina unreachable ({reason}). Keeping the current {Fragments.Count} fragments.";
            }
            else
            {
                // No fragments yet: fall back to a synthetic ladder so multi-transition still works.
                double[] fallback = { 1.0, 0.72, 0.55, 0.38, 0.22, 0.12, 0.08 };
                int count = System.Math.Min(System.Math.Max(1, TopN), fallback.Length);
                for (int i = 0; i < count; i++)
                {
                    Fragments.Add(new FragmentPrediction($"y{count - i}+1", 0.0, fallback[i]));
                }

                KoinaStatus = $"Warning: Koina unreachable ({reason}); using synthetic fragments.";
            }

            StatusText = KoinaStatus;
            SchedulePreview();
        }
    }

    /// <summary>Cancel an in-progress sweep.</summary>
    [RelayCommand]
    private void CancelSimulation() => _cts?.Cancel();
}
