//! Savitzky-Golay smoothing (ported from Osprey's smooth_savitzky_golay).

namespace Datum.Core.Math;

/// <summary>Signal smoothing helpers.</summary>
public static class Smoothing
{
    /// <summary>
    /// 5-point quadratic Savitzky-Golay smoothing with coefficients
    /// <c>[-3, 12, 17, 12, -3] / 35</c>. The first and last two points are returned
    /// unsmoothed; negative outputs are clamped to zero. Matches Osprey's implementation.
    /// </summary>
    public static double[] SavitzkyGolay5(double[] values)
    {
        int n = values.Length;
        if (n < 5)
        {
            return (double[])values.Clone();
        }

        var result = new double[n];
        result[0] = values[0];
        result[1] = values[1];
        result[n - 1] = values[n - 1];
        result[n - 2] = values[n - 2];

        for (int i = 2; i < n - 2; i++)
        {
            double v = (-3.0 * values[i - 2] + 12.0 * values[i - 1] + 17.0 * values[i]
                + 12.0 * values[i + 1] - 3.0 * values[i + 2]) / 35.0;
            result[i] = System.Math.Max(0.0, v);
        }

        return result;
    }
}
