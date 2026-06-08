using Datum.Core.Math;
using Xunit;

namespace Datum.Core.Tests;

public class MathFunctionsTests
{
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.5204998778)]
    [InlineData(1.0, 0.8427007929)]
    [InlineData(2.0, 0.9953222650)]
    [InlineData(-1.0, -0.8427007929)]
    public void Erf_matches_known_values(double x, double expected)
    {
        Assert.Equal(expected, MathFunctions.Erf(x), 6);
    }

    [Fact]
    public void Erfcx_stays_finite_and_accurate_for_large_argument()
    {
        // erfcx(x) ~ 1/(x*sqrt(pi)) for large x; erfc(x) itself underflows near x=27.
        double x = 30.0;
        double erfcx = MathFunctions.Erfcx(x);
        Assert.True(double.IsFinite(erfcx));
        double asymptotic = 1.0 / (x * System.Math.Sqrt(System.Math.PI));
        Assert.Equal(asymptotic, erfcx, 4);
    }

    [Fact]
    public void NormalCdf_is_one_half_at_zero()
    {
        Assert.Equal(0.5, MathFunctions.NormalCdf(0.0), 6);
    }
}
