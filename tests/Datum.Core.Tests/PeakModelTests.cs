using Datum.Core.Models;
using Xunit;

namespace Datum.Core.Tests;

public class PeakModelTests
{
    [Fact]
    public void GaussianPeak_true_area_matches_analytic_formula()
    {
        var peak = new GaussianPeak(height: 100.0, center: 8.0, sigma: 0.3);
        double analytic = 100.0 * 0.3 * System.Math.Sqrt(2.0 * System.Math.PI);
        Assert.Equal(analytic, peak.TrueArea(), 3);
    }

    [Fact]
    public void GaussianPeak_apex_is_at_center_with_height()
    {
        var peak = new GaussianPeak(height: 50.0, center: 5.0, sigma: 0.2);
        Assert.Equal(5.0, peak.ApexRt, 6);
        Assert.Equal(50.0, peak.Evaluate(5.0), 6);
    }

    [Fact]
    public void SkewNormalPeak_center_of_mass_equals_requested_center()
    {
        var peak = new SkewNormalPeak(height: 100.0, center: 10.0, sigma: 0.4, skew: 4.0);

        // Numerically compute the intensity-weighted mean over a wide window.
        const int n = 20000;
        double a = 6.0;
        double b = 14.0;
        double dx = (b - a) / (n - 1);
        double num = 0.0;
        double den = 0.0;
        for (int i = 0; i < n; i++)
        {
            double rt = a + i * dx;
            double y = peak.Evaluate(rt);
            num += rt * y;
            den += y;
        }

        Assert.Equal(10.0, num / den, 2);
    }

    [Fact]
    public void EmgPeak_is_right_tailed_and_finite_everywhere()
    {
        var peak = new EmgPeak(height: 100.0, center: 8.0, sigma: 0.2, tau: 0.4);

        // Apex sits to the right of the Gaussian center for a tailing peak.
        Assert.True(peak.ApexRt > 8.0);

        // The right tail decays slower than the left rise (asymmetry check).
        double leftFar = peak.Evaluate(peak.ApexRt - 1.0);
        double rightFar = peak.Evaluate(peak.ApexRt + 1.0);
        Assert.True(rightFar > leftFar);

        Assert.True(double.IsFinite(peak.TrueArea()) && peak.TrueArea() > 0.0);
    }
}
