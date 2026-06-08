//! Renders the four ScottPlot panels from preview and simulation data.

using System.Collections.Generic;
using Datum.Core.Algorithms;
using Datum.Core.Algorithms.Integrators;
using Datum.Core.Simulation;
using ScottPlot;

namespace Datum.App.Plotting;

/// <summary>
/// Draws the application's plots onto ScottPlot <see cref="Plot"/> instances: the ground-truth
/// peak, the noisy trace, the sampled peak with its integration region, and the deviation-vs-
/// points-across-peak curve with the too-few/borderline/enough regions from the reference figures.
/// </summary>
public static class PlotRenderer
{
    private static readonly Color GroundTruthColor = Color.FromHex("#1f4e79");
    private static readonly Color NoisyColor = Color.FromHex("#5b9bd5");
    private static readonly Color SampleColor = Color.FromHex("#c00000");
    private static readonly Color AreaColor = Color.FromHex("#c00000").WithAlpha(0.18);
    private static readonly Color BackgroundColor = Color.FromHex("#808080");

    // Distinct colors for individual transitions in multi-transition mode.
    private static readonly Color[] TransitionPalette =
    {
        Color.FromHex("#1f77b4"), Color.FromHex("#ff7f0e"), Color.FromHex("#2ca02c"),
        Color.FromHex("#d62728"), Color.FromHex("#9467bd"), Color.FromHex("#8c564b"),
        Color.FromHex("#e377c2"), Color.FromHex("#17becf"),
    };

    private static Color TransitionColor(int index) => TransitionPalette[index % TransitionPalette.Length];

    private static string TransitionLabel(PreviewResult p, int index) =>
        p.FragmentLabels is { } labels && index < labels.Length ? labels[index] : $"transition {index + 1}";

    /// <summary>Plot 1: the noise-free analyte peak with its true-area shading.</summary>
    public static void DrawGroundTruth(Plot plot, PreviewResult p)
    {
        ResetPlot(plot);

        if (p.IsMultiTransition && p.FragmentGroundTruth is { } frags)
        {
            for (int f = 0; f < frags.Length; f++)
            {
                var fragLine = plot.Add.Scatter(p.Rt, frags[f]);
                fragLine.LineWidth = 1.5f;
                fragLine.MarkerSize = 0;
                fragLine.Color = TransitionColor(f);
                fragLine.LegendText = TransitionLabel(p, f);
            }

            plot.Title($"Ground truth · {frags.Length} transitions · total area {p.TrueArea:N0}");
            plot.XLabel("Retention time");
            plot.YLabel("Intensity");
            plot.ShowLegend(Alignment.UpperRight);
            plot.Axes.AutoScale();
            return;
        }

        FillUnder(plot, p.Rt, p.GroundTruthMain, GroundTruthColor.WithAlpha(0.15));

        var line = plot.Add.Scatter(p.Rt, p.GroundTruthMain);
        line.LineWidth = 2;
        line.MarkerSize = 0;
        line.Color = GroundTruthColor;

        plot.Title($"Ground truth peak  ·  true area {p.TrueArea:N0}");
        plot.XLabel("Retention time");
        plot.YLabel("Intensity");
        plot.Axes.AutoScale();
    }

    /// <summary>Plot 2: the noisy realization plus the ground-truth and background overlays.</summary>
    public static void DrawNoisy(Plot plot, PreviewResult p)
    {
        ResetPlot(plot);

        if (p.IsMultiTransition && p.FragmentNoisy is { } frags)
        {
            for (int f = 0; f < frags.Length; f++)
            {
                var trace = plot.Add.Scatter(p.Rt, frags[f]);
                trace.LineWidth = 1.2f;
                trace.MarkerSize = 0;
                trace.Color = TransitionColor(f);
                trace.LegendText = TransitionLabel(p, f);
            }

            if (HasBackground(p.BackgroundCurve))
            {
                var bgLine = plot.Add.Scatter(p.Rt, p.BackgroundCurve);
                bgLine.LineWidth = 1;
                bgLine.MarkerSize = 0;
                bgLine.Color = BackgroundColor;
                bgLine.LinePattern = LinePattern.Dashed;
                bgLine.LegendText = "Background";
            }

            plot.Title($"Transitions with noise ({frags.Length})");
            plot.XLabel("Retention time");
            plot.YLabel("Intensity");
            plot.ShowLegend(Alignment.UpperRight);
            plot.Axes.AutoScale();
            return;
        }

        var noisy = plot.Add.Scatter(p.Rt, p.Noisy);
        noisy.LineWidth = 1.5f;
        noisy.MarkerSize = 0;
        noisy.Color = NoisyColor;
        noisy.LegendText = "Noisy signal";

        var truth = plot.Add.Scatter(p.Rt, p.GroundTruthTotal);
        truth.LineWidth = 2;
        truth.MarkerSize = 0;
        truth.Color = GroundTruthColor;
        truth.LegendText = "Ground truth";

        if (HasBackground(p.BackgroundCurve))
        {
            var bg = plot.Add.Scatter(p.Rt, p.BackgroundCurve);
            bg.LineWidth = 1;
            bg.MarkerSize = 0;
            bg.Color = BackgroundColor;
            bg.LinePattern = LinePattern.Dashed;
            bg.LegendText = "Background";
        }

        plot.Title("Peak with noise");
        plot.XLabel("Retention time");
        plot.YLabel("Intensity");
        plot.ShowLegend(Alignment.UpperRight);
        plot.Axes.AutoScale();
    }

    /// <summary>Plot 3: the sampled peak, detected boundaries, and the integrated area.</summary>
    public static void DrawSampled(Plot plot, PreviewResult p)
    {
        ResetPlot(plot);

        if (p.IsMultiTransition && p.FragmentNoisy is { } noisyFrags && p.FragmentSamples is { } sampleFrags)
        {
            for (int f = 0; f < noisyFrags.Length; f++)
            {
                Color color = TransitionColor(f);

                var trace = plot.Add.Scatter(p.Rt, noisyFrags[f]);
                trace.LineWidth = 1;
                trace.MarkerSize = 0;
                trace.Color = color.WithAlpha(0.45);

                // All sampled points for this transition: open circles outside the boundaries,
                // larger filled circles for the integrated (in-peak) points.
                var inRt = new List<double>();
                var inY = new List<double>();
                var outRt = new List<double>();
                var outY = new List<double>();
                for (int i = 0; i < p.SampleRt.Length; i++)
                {
                    bool inPeak = p.Bounds.Detected && i >= p.Bounds.StartIndex && i <= p.Bounds.EndIndex;
                    (inPeak ? inRt : outRt).Add(p.SampleRt[i]);
                    (inPeak ? inY : outY).Add(sampleFrags[f][i]);
                }

                if (outRt.Count > 0)
                {
                    var outPts = plot.Add.Scatter(outRt.ToArray(), outY.ToArray());
                    outPts.LineWidth = 0;
                    outPts.MarkerSize = 5;
                    outPts.MarkerShape = MarkerShape.OpenCircle;
                    outPts.Color = color.WithAlpha(0.5);
                }

                if (inRt.Count > 0)
                {
                    var pts = plot.Add.Scatter(inRt.ToArray(), inY.ToArray());
                    pts.LineWidth = 0;
                    pts.MarkerSize = 9;
                    pts.MarkerShape = MarkerShape.FilledCircle;
                    pts.Color = color;
                    pts.LegendText = TransitionLabel(p, f);
                }
            }

            if (p.Bounds.Detected)
            {
                AddBoundary(plot, p.Bounds.StartRt);
                AddBoundary(plot, p.Bounds.EndRt);
            }

            plot.Title(SampledTitle("Sampled transitions · consensus", p));
            plot.XLabel("Retention time");
            plot.YLabel("Intensity");
            plot.ShowLegend(Alignment.UpperRight);
            plot.Axes.AutoScale();
            return;
        }

        var underlying = plot.Add.Scatter(p.Rt, p.Noisy);
        underlying.LineWidth = 1;
        underlying.MarkerSize = 0;
        underlying.Color = NoisyColor.WithAlpha(0.5);
        underlying.LegendText = "Noisy signal";

        // All sampled points (including those outside the detected peak) as open circles.
        var all = plot.Add.Scatter(p.SampleRt, p.SampleIntensity);
        all.LineWidth = 0;
        all.MarkerSize = 7;
        all.MarkerShape = MarkerShape.OpenCircle;
        all.Color = Colors.Black;
        all.LegendText = "All samples";

        if (p.Bounds.Detected)
        {
            // Integration region (reaches the exact boundaries when edge estimation is on).
            (double[] ix, double[] iy) = IntegrationGeometry.InPeakPoints(
                p.SampleRt, p.SampleIntensity, p.Bounds, p.EdgeEstimation);

            if (ix.Length >= 2)
            {
                double[] baseline = Baseline(ix, iy, p.SubtractsBackground);
                FillBetween(plot, ix, iy, baseline, AreaColor);

                if (p.SubtractsBackground)
                {
                    var baseLine = plot.Add.Scatter(new[] { ix[0], ix[^1] }, new[] { baseline[0], baseline[^1] });
                    baseLine.LineWidth = 1.5f;
                    baseLine.MarkerSize = 0;
                    baseLine.Color = Color.FromHex("#e08a00");
                    baseLine.LegendText = "Baseline";
                }
            }

            // The actual in-peak samples.
            var (inRt, inY) = InPeakSamples(p);
            if (inRt.Count > 0)
            {
                var inPeak = plot.Add.Scatter(inRt.ToArray(), inY.ToArray());
                inPeak.LineWidth = 0;
                inPeak.MarkerSize = 12;
                inPeak.MarkerShape = MarkerShape.FilledCircle;
                inPeak.Color = SampleColor;
                inPeak.LegendText = "Integrated points";
            }

            // Imputed fractional-trapezoid edge points at the exact boundaries.
            if (p.EdgeEstimation)
            {
                double startEdge = Chromatogram.Interpolate(p.SampleRt, p.SampleIntensity, p.Bounds.StartRt);
                double endEdge = Chromatogram.Interpolate(p.SampleRt, p.SampleIntensity, p.Bounds.EndRt);
                var edges = plot.Add.Scatter(
                    new[] { p.Bounds.StartRt, p.Bounds.EndRt }, new[] { startEdge, endEdge });
                edges.LineWidth = 0;
                edges.MarkerSize = 9;
                edges.MarkerShape = MarkerShape.OpenDiamond;
                edges.Color = SampleColor;
                edges.LegendText = "Edge estimate";
            }

            AddBoundary(plot, p.Bounds.StartRt);
            AddBoundary(plot, p.Bounds.EndRt);
        }

        plot.Title(SampledTitle("Sampled peak", p));
        plot.XLabel("Retention time");
        plot.YLabel("Intensity");
        plot.ShowLegend(Alignment.UpperRight);
        plot.Axes.AutoScale();
    }

    /// <summary>Plot 4: deviation in area (%) vs points across the peak, with regions and error bars.</summary>
    public static void DrawDeviation(Plot plot, IReadOnlyList<DeviationResult> results)
    {
        ResetPlot(plot);
        if (results.Count == 0)
        {
            return;
        }

        double maxX = results[^1].PointsAcrossPeak + 1;

        AddRegion(plot, 0, 4.5, Color.FromHex("#e8a0a0"), "too few");
        AddRegion(plot, 4.5, 7.5, Color.FromHex("#f0d890"), "borderline");
        AddRegion(plot, 7.5, maxX, Color.FromHex("#a8d8a8"), "enough");

        var zero = plot.Add.HorizontalLine(0);
        zero.Color = Colors.Gray;
        zero.LinePattern = LinePattern.Dashed;
        zero.LineWidth = 1;

        // Skip non-finite points (e.g. a fit integrator that returned NaN/"NA" at low sampling).
        var xList = new List<double>();
        var yList = new List<double>();
        foreach (DeviationResult r in results)
        {
            if (!double.IsFinite(r.PercentDeviation))
            {
                continue;
            }

            xList.Add(r.PointsAcrossPeak);
            yList.Add(r.PercentDeviation);
            AddErrorBar(plot, r.PointsAcrossPeak, r.PercentDeviation,
                double.IsFinite(r.PercentStd) ? r.PercentStd : 0.0);
        }

        var line = plot.Add.Scatter(xList.ToArray(), yList.ToArray());
        line.LineWidth = 2;
        line.MarkerSize = 6;
        line.MarkerShape = MarkerShape.FilledCircle;
        line.Color = GroundTruthColor;

        var tenLine = plot.Add.VerticalLine(10);
        tenLine.Color = Color.FromHex("#2e7d32");
        tenLine.LinePattern = LinePattern.Dotted;
        tenLine.LineWidth = 1.5f;

        plot.Title("Sampling rate vs accuracy");
        plot.XLabel("Points across the peak");
        plot.YLabel("Deviation in peak area (%)");
        plot.ShowLegend(Edge.Right);
        plot.Axes.AutoScale();
    }

    /// <summary>
    /// Clear plottables and also remove any legend panels. ScottPlot's <c>ShowLegend(Edge.Right)</c>
    /// adds a new legend panel each call, and <c>Plot.Clear()</c> does not remove panels, so without
    /// this they accumulate (a fresh duplicate legend on every redraw).
    /// </summary>
    private static void ResetPlot(Plot plot)
    {
        plot.Clear();
        foreach (var panel in plot.Axes.GetPanels())
        {
            if (panel is ScottPlot.Panels.LegendPanel)
            {
                plot.Axes.Remove(panel);
            }
        }
    }

    /// <summary>Title fragment for a sampled-peak plot, showing "NA" when the area could not be computed.</summary>
    private static string SampledTitle(string prefix, PreviewResult p) =>
        double.IsFinite(p.SampledArea)
            ? $"{prefix}  ·  area {p.SampledArea:N0}  ({Deviation(p):+0.0;-0.0;0.0}%)"
            : $"{prefix}  ·  area NA";

    private static double Deviation(PreviewResult p) =>
        p.TrueArea > 0 ? (p.SampledArea - p.TrueArea) / p.TrueArea * 100.0 : 0.0;

    private static (List<double> Rt, List<double> Y) InPeakSamples(PreviewResult p)
    {
        var rt = new List<double>();
        var y = new List<double>();
        for (int i = p.Bounds.StartIndex; i <= p.Bounds.EndIndex && i < p.SampleRt.Length; i++)
        {
            if (i < 0)
            {
                continue;
            }

            rt.Add(p.SampleRt[i]);
            y.Add(p.SampleIntensity[i]);
        }

        return (rt, y);
    }

    private static void AddBoundary(Plot plot, double x)
    {
        var v = plot.Add.VerticalLine(x);
        v.Color = Color.FromHex("#2e7d32");
        v.LinePattern = LinePattern.Dashed;
        v.LineWidth = 1;
    }

    private static void AddRegion(Plot plot, double x1, double x2, Color color, string label)
    {
        var span = plot.Add.HorizontalSpan(x1, x2);
        span.FillColor = color.WithAlpha(0.25);
        span.LineWidth = 0;
        span.LegendText = label;
    }

    private static void AddErrorBar(Plot plot, double x, double y, double err)
    {
        if (err <= 0)
        {
            return;
        }

        var stem = plot.Add.Line(x, y - err, x, y + err);
        stem.Color = Colors.Black.WithAlpha(0.6);
        stem.LineWidth = 1;

        double cap = 0.2;
        var top = plot.Add.Line(x - cap, y + err, x + cap, y + err);
        top.Color = Colors.Black.WithAlpha(0.6);
        top.LineWidth = 1;
        var bottom = plot.Add.Line(x - cap, y - err, x + cap, y - err);
        bottom.Color = Colors.Black.WithAlpha(0.6);
        bottom.LineWidth = 1;
    }

    /// <summary>Baseline under the integration region: linear between endpoints, or zero.</summary>
    private static double[] Baseline(double[] x, double[] y, bool subtractsBackground)
    {
        var baseline = new double[x.Length];
        if (!subtractsBackground || x.Length < 2)
        {
            return baseline; // zeros
        }

        double x0 = x[0];
        double span = x[^1] - x[0];
        for (int i = 0; i < x.Length; i++)
        {
            double t = span > 1e-12 ? (x[i] - x0) / span : 0.0;
            baseline[i] = y[0] + (y[^1] - y[0]) * t;
        }

        return baseline;
    }

    /// <summary>Fill the band between an upper curve and a lower baseline.</summary>
    private static void FillBetween(Plot plot, double[] xs, double[] top, double[] bottom, Color color)
    {
        var coords = new List<Coordinates>(xs.Length * 2);
        for (int i = 0; i < xs.Length; i++)
        {
            coords.Add(new Coordinates(xs[i], top[i]));
        }

        for (int i = xs.Length - 1; i >= 0; i--)
        {
            coords.Add(new Coordinates(xs[i], bottom[i]));
        }

        var poly = plot.Add.Polygon(coords.ToArray());
        poly.FillColor = color;
        poly.LineWidth = 0;
    }

    private static void FillUnder(Plot plot, double[] xs, double[] ys, Color color)
    {
        if (xs.Length < 2)
        {
            return;
        }

        var coords = new List<Coordinates>(xs.Length + 2) { new(xs[0], 0) };
        for (int i = 0; i < xs.Length; i++)
        {
            coords.Add(new Coordinates(xs[i], ys[i]));
        }

        coords.Add(new Coordinates(xs[^1], 0));

        var poly = plot.Add.Polygon(coords.ToArray());
        poly.FillColor = color;
        poly.LineWidth = 0;
    }

    private static bool HasBackground(double[] background)
    {
        foreach (double v in background)
        {
            if (v != 0.0)
            {
                return true;
            }
        }

        return false;
    }
}
