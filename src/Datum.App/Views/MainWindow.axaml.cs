//! Main window: binds the view model's preview/simulation events to the ScottPlot panels.

using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Datum.App.Plotting;
using Datum.App.ViewModels;
using Datum.Core.Simulation;

namespace Datum.App.Views;

/// <summary>
/// The application window. Plotting in ScottPlot is imperative, so the window subscribes to
/// the view model's <see cref="MainWindowViewModel.PreviewUpdated"/> and
/// <see cref="MainWindowViewModel.SimulationCompleted"/> events and redraws the panels.
/// </summary>
public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Move the window onto the primary monitor (it otherwise opens wherever the window manager
    /// decides, which on a multi-monitor setup is often a secondary screen). Set the environment
    /// variable <c>DATUM_SCREEN=&lt;index&gt;</c> to force a specific screen when auto-detection
    /// picks the wrong one; <c>DATUM_SCREEN_DEBUG=1</c> logs the detected layout to stderr.
    /// </summary>
    /// <remarks>
    /// Some compositors honor a client move only at map time, others only a moment afterward, and
    /// some (notably WSLg) ignore client moves entirely. We therefore set the position on open and
    /// again on a short delay; if it still lands on the wrong monitor the compositor is overriding
    /// us (see README "Window placement").
    /// </remarks>
    protected override void OnOpened(System.EventArgs e)
    {
        base.OnOpened(e);
        PlaceOnPreferredScreen();

        var timer = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(400) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            PlaceOnPreferredScreen();
        };
        timer.Start();
    }

    private void PlaceOnPreferredScreen()
    {
        try
        {
            IReadOnlyList<Screen>? all = Screens?.All;
            if (all is null || all.Count == 0)
            {
                return;
            }

            string? envIndex = System.Environment.GetEnvironmentVariable("DATUM_SCREEN");
            bool debug = envIndex is not null
                || System.Environment.GetEnvironmentVariable("DATUM_SCREEN_DEBUG") == "1";

            if (debug)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    Screen s = all[i];
                    System.Console.Error.WriteLine(
                        $"[datum] screen[{i}] primary={s.IsPrimary} bounds={s.Bounds} workingArea={s.WorkingArea} scaling={s.Scaling}");
                }
            }

            Screen? target = null;
            if (envIndex is not null && int.TryParse(envIndex, out int idx) && idx >= 0 && idx < all.Count)
            {
                target = all[idx];
            }

            // Prefer the OS-reported primary; but some compositors (notably WSLg) report no
            // primary at all, so fall back to the monitor whose top-left is the virtual-desktop
            // origin (0,0) — which is the Windows primary — and finally to the screen nearest it.
            target ??= Screens?.Primary
                ?? all.FirstOrDefault(s => s.IsPrimary)
                ?? all.FirstOrDefault(s => s.Bounds.X == 0 && s.Bounds.Y == 0)
                ?? all.OrderBy(s => System.Math.Abs(s.Bounds.X) + System.Math.Abs(s.Bounds.Y)).First();

            PixelRect area = target.WorkingArea;
            double scale = target.Scaling > 0 ? target.Scaling : 1.0;
            int w = (int)((Width > 0 ? Width : 1320) * scale);
            int h = (int)((Height > 0 ? Height : 860) * scale);
            var position = new PixelPoint(
                area.X + System.Math.Max(0, (area.Width - w) / 2),
                area.Y + System.Math.Max(0, (area.Height - h) / 2));
            Position = position;
            if (debug)
            {
                System.Console.Error.WriteLine(
                    $"[datum] placing window at {position} on screen primary={target.IsPrimary} (workingArea={area}, scaling={scale})");
            }
        }
        catch (System.Exception ex)
        {
            System.Console.Error.WriteLine($"[datum] window placement failed: {ex.Message}");
        }
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PreviewUpdated -= OnPreviewUpdated;
            _viewModel.SimulationCompleted -= OnSimulationCompleted;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PreviewUpdated += OnPreviewUpdated;
            _viewModel.SimulationCompleted += OnSimulationCompleted;
            _viewModel.Initialize();

            // Verification hook: when DATUM_AUTORUN=1, trigger the real Run command on startup so
            // the deviation plot can be observed without click automation. DATUM_MULTI=1 also
            // fetches Koina fragments and exercises the multi-transition consensus path.
            if (System.Environment.GetEnvironmentVariable("DATUM_AUTORUN") == "1")
            {
                _ = RunVerificationAsync();
            }
        }
    }

    private async System.Threading.Tasks.Task RunVerificationAsync()
    {
        if (_viewModel is null)
        {
            return;
        }

        if (System.Environment.GetEnvironmentVariable("DATUM_MULTI") == "1")
        {
            await _viewModel.FetchKoinaCommand.ExecuteAsync(null);
            _viewModel.UseMultiTransition = true;
        }

        if (_viewModel.RunSimulationCommand.CanExecute(null))
        {
            await _viewModel.RunSimulationCommand.ExecuteAsync(null);
        }
    }

    private void OnPreviewUpdated(PreviewResult preview)
    {
        PlotRenderer.DrawGroundTruth(GroundTruthPlot.Plot, preview);
        PlotRenderer.DrawNoisy(NoisyPlot.Plot, preview);
        PlotRenderer.DrawSampled(SampledPlot.Plot, preview);

        GroundTruthPlot.Refresh();
        NoisyPlot.Refresh();
        SampledPlot.Refresh();
    }

    private void OnSimulationCompleted(IReadOnlyList<DeviationResult> results)
    {
        PlotRenderer.DrawDeviation(DeviationPlot.Plot, results);
        DeviationPlot.Refresh();
        CaptureIfRequested();
    }

    /// <summary>
    /// Verification hook: when DATUM_SHOT names a path, render the window to a PNG (independent
    /// of the display server) shortly after the plots settle, then close.
    /// </summary>
    private void CaptureIfRequested()
    {
        string? path = System.Environment.GetEnvironmentVariable("DATUM_SHOT");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var timer = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(800) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                int w = (int)System.Math.Ceiling(Bounds.Width <= 0 ? Width : Bounds.Width);
                int h = (int)System.Math.Ceiling(Bounds.Height <= 0 ? Height : Bounds.Height);
                using var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
                rtb.Render(this);
                rtb.Save(path);
            }
            catch (System.Exception ex)
            {
                System.Console.Error.WriteLine($"capture failed: {ex.Message}");
            }

            Close();
        };
        timer.Start();
    }
}
