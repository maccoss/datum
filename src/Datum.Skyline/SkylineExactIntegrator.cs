//! Byte-identical Skyline peak-area integrator (wraps vendored pwiz PeakFinder + ChromPeak math).

#nullable enable

using Datum.Core.Algorithms;
using pwiz.Common.PeakFinding;

namespace Datum.Skyline;

/// <summary>
/// Area integrator that reproduces Skyline's exact peak-area calculation. The chromatogram is
/// handed to Skyline's vendored peak finder as <see langword="float"/> data and re-integrated at
/// the supplied boundary indices via <c>IPeakFinder.GetPeak</c> (Skyline's
/// <c>PeakIntegrator.IntegrateFoundPeak</c> path), giving the background-subtracted index-space
/// area exactly as <c>FoundPeak</c> computes it (trapezoid <c>sum - yStart/2 - yEnd/2</c> minus
/// the min-clipped background). That area is then scaled by the per-sample time interval, exactly
/// as Skyline's <c>ChromPeak</c> constructor does.
/// </summary>
/// <remarks>
/// One deliberate difference from Skyline's reported number: Skyline's <c>time_normalized</c>
/// path multiplies by <c>interval * 60</c> to report area in intensity·seconds (interval in
/// minutes). We multiply by the interval only, so the result is in intensity·(retention-time
/// unit) and is directly comparable to datum's ground-truth area, which is computed in the same
/// units. To recover Skyline's exact reported value, supply retention time in minutes and
/// multiply this area by 60.
/// </remarks>
public sealed class SkylineExactIntegrator : IIntegrator
{
    /// <inheritdoc/>
    public string Name => "Skyline area (exact)";

    /// <inheritdoc/>
    public double Integrate(double[] rt, double[] intensity, PeakBounds bounds, IntegratorOptions options)
    {
        if (!bounds.Detected)
        {
            return 0.0;
        }

        int start = bounds.StartIndex;
        int end = bounds.EndIndex;
        if (end <= start || start < 0 || end >= rt.Length)
        {
            return 0.0;
        }

        using IPeakFinder finder = PeakFinders.NewDefaultPeakFinder();
        finder.SetChromatogram(ToFloat(rt), ToFloat(intensity));
        using IFoundPeak peak = finder.GetPeak(start, end);

        // ChromPeak time normalization: interval = (endTime - startTime) / (endIndex - startIndex).
        double interval = (rt[end] - rt[start]) / (end - start);
        return peak.Area * interval;
    }

    /// <summary>Convert a double array to the float array Skyline's chromatograms use.</summary>
    internal static float[] ToFloat(double[] values)
    {
        var result = new float[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            result[i] = (float)values[i];
        }

        return result;
    }
}
