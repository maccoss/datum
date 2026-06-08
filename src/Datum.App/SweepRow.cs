//! Pre-formatted row for the sweep results table (renders non-finite values as "NA").

using Datum.Core.Simulation;

namespace Datum.App;

/// <summary>
/// A formatted row of the Monte-Carlo sweep results table. Non-finite values (e.g. a fit
/// integrator that could not fit at low sampling, reported as NaN) are shown as "NA" rather than
/// a meaningless number.
/// </summary>
public sealed record SweepRow(string Points, string MeanArea, string Deviation, string Sd)
{
    /// <summary>Format a <see cref="DeviationResult"/> for display.</summary>
    public static SweepRow From(DeviationResult r) => new(
        r.PointsAcrossPeak.ToString(),
        double.IsFinite(r.MeanArea) ? r.MeanArea.ToString("N0") : "NA",
        double.IsFinite(r.PercentDeviation) ? r.PercentDeviation.ToString("+0.0;-0.0;0.0") : "NA",
        double.IsFinite(r.PercentStd) ? r.PercentStd.ToString("0.0") : "NA");
}
