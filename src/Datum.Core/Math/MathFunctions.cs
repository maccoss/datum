//! Numerical special functions used by the peak models and fitting integrators.

namespace Datum.Core.Math;

/// <summary>
/// Special functions (error function family and the standard normal distribution)
/// implemented with stable, well-conditioned approximations.
/// </summary>
/// <remarks>
/// The error-function family uses the Numerical Recipes <c>erfcc</c> rational
/// approximation, which has fractional error below 1.2e-7 across the whole real line.
/// Crucially it gives <em>relative</em> (not absolute) accuracy in the tails, so the
/// scaled complementary error function <see cref="Erfcx"/> stays accurate for large
/// arguments. This matters for the exponentially-modified Gaussian, where naive
/// <c>exp(x^2) * erfc(x)</c> would be a 0 * infinity catastrophe.
/// </remarks>
public static class MathFunctions
{
    /// <summary>Complementary error function, erfc(x) = 1 - erf(x).</summary>
    public static double Erfc(double x)
    {
        double z = System.Math.Abs(x);
        double t = 1.0 / (1.0 + 0.5 * z);
        double ans = t * System.Math.Exp(-z * z - 1.26551223 + Poly(t));
        return x >= 0.0 ? ans : 2.0 - ans;
    }

    /// <summary>Error function, erf(x).</summary>
    public static double Erf(double x) => 1.0 - Erfc(x);

    /// <summary>
    /// Scaled complementary error function, erfcx(x) = exp(x^2) * erfc(x).
    /// Stable for large positive x (returns a finite value where erfc(x) underflows).
    /// </summary>
    public static double Erfcx(double x)
    {
        double z = System.Math.Abs(x);
        double t = 1.0 / (1.0 + 0.5 * z);
        // erfcc folds an exp(-z^2) into its result; dropping it yields erfcx(z) for z >= 0.
        double erfcxPos = t * System.Math.Exp(-1.26551223 + Poly(t));
        if (x >= 0.0)
        {
            return erfcxPos;
        }

        // erfcx(-z) = 2*exp(z^2) - erfcx(z).
        return 2.0 * System.Math.Exp(x * x) - erfcxPos;
    }

    /// <summary>Standard normal probability density, phi(x).</summary>
    public static double NormalPdf(double x) =>
        System.Math.Exp(-0.5 * x * x) / System.Math.Sqrt(2.0 * System.Math.PI);

    /// <summary>Standard normal cumulative distribution, Phi(x).</summary>
    public static double NormalCdf(double x) =>
        0.5 * Erfc(-x / System.Math.Sqrt(2.0));

    /// <summary>Numerical Recipes erfcc polynomial in t (shared by erf/erfc/erfcx).</summary>
    private static double Poly(double t) =>
        t * (1.00002368 + t * (0.37409196 + t * (0.09678418 + t * (-0.18628806 +
        t * (0.27886807 + t * (-1.13520398 + t * (1.48851587 + t * (-0.82215223 +
        t * 0.17087277))))))));
}
