//! Shared geometry of the integrated region, used by integrators and by the UI fill.

namespace Datum.Core.Algorithms.Integrators;

/// <summary>
/// Builds the (x, y) points that define the integrated region between the detected boundaries.
/// Integrators and the UI both use this so the shaded area always matches the computed area.
/// With edge estimation enabled, the region is extended to the exact start/end boundaries with
/// linearly-interpolated (fractional-trapezoid) edge points, even when no sample falls between
/// the outermost in-peak sample and the boundary.
/// </summary>
public static class IntegrationGeometry
{
    /// <summary>
    /// Return the integration points across the peak. Includes the in-peak samples and, when
    /// <paramref name="edgeEstimation"/> is true, imputed points at the exact boundaries.
    /// </summary>
    public static (double[] X, double[] Y) InPeakPoints(
        double[] rt, double[] intensity, PeakBounds bounds, bool edgeEstimation)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        if (!bounds.Detected)
        {
            return (xs.ToArray(), ys.ToArray());
        }

        for (int i = bounds.StartIndex; i <= bounds.EndIndex && i < rt.Length; i++)
        {
            if (i < 0)
            {
                continue;
            }

            xs.Add(rt[i]);
            ys.Add(intensity[i]);
        }

        if (edgeEstimation)
        {
            double startValue = Simulation.Chromatogram.Interpolate(rt, intensity, bounds.StartRt);
            double endValue = Simulation.Chromatogram.Interpolate(rt, intensity, bounds.EndRt);

            if (xs.Count == 0)
            {
                // No in-peak samples: the region is the single boundary-to-boundary trapezoid.
                xs.Add(bounds.StartRt);
                ys.Add(startValue);
                xs.Add(bounds.EndRt);
                ys.Add(endValue);
            }
            else
            {
                if (bounds.StartRt < xs[0])
                {
                    xs.Insert(0, bounds.StartRt);
                    ys.Insert(0, startValue);
                }

                if (bounds.EndRt > xs[^1])
                {
                    xs.Add(bounds.EndRt);
                    ys.Add(endValue);
                }
            }
        }

        return (xs.ToArray(), ys.ToArray());
    }
}
