//! Shared peak-model behavior: numeric true-area integration and apex location.

namespace Datum.Core.Models;

/// <summary>
/// Base class providing a numeric trapezoidal <see cref="TrueArea"/> over a model-defined
/// integration window and a scanned apex location. Subclasses supply the shape via
/// <see cref="Evaluate"/> and the integration bounds.
/// </summary>
public abstract class PeakModelBase : IPeakModel
{
    /// <summary>Number of grid points used for numeric integration and apex scanning.</summary>
    protected const int IntegrationPoints = 2000;

    /// <inheritdoc/>
    public abstract double Center { get; }

    /// <inheritdoc/>
    public abstract double Sigma { get; }

    /// <inheritdoc/>
    public abstract double Evaluate(double rt);

    /// <summary>Lower bound of the numeric integration window.</summary>
    protected virtual double IntegrationStart => Center - 5.0 * Sigma;

    /// <summary>Upper bound of the numeric integration window.</summary>
    protected virtual double IntegrationEnd => Center + 5.0 * Sigma;

    private double? _apexRt;

    /// <inheritdoc/>
    public virtual double ApexRt => _apexRt ??= FindApex();

    /// <inheritdoc/>
    public virtual double TrueArea()
    {
        // Numeric trapezoid over a wide window; for skew/EMG the window includes the tail.
        // The ground truth deliberately uses the same trapezoid rule the integrators use,
        // so reported deviations isolate sampling/algorithm effects rather than quadrature.
        double a = IntegrationStart;
        double b = IntegrationEnd;
        double dx = (b - a) / (IntegrationPoints - 1);
        double sum = 0.0;
        double prev = Evaluate(a);
        for (int i = 1; i < IntegrationPoints; i++)
        {
            double cur = Evaluate(a + i * dx);
            sum += 0.5 * (prev + cur) * dx;
            prev = cur;
        }

        return sum;
    }

    /// <summary>Locate the retention time of maximum intensity by a fine scan over the window.</summary>
    private double FindApex()
    {
        double a = IntegrationStart;
        double b = IntegrationEnd;
        double dx = (b - a) / (IntegrationPoints - 1);
        double bestRt = Center;
        double bestVal = double.NegativeInfinity;
        for (int i = 0; i < IntegrationPoints; i++)
        {
            double rt = a + i * dx;
            double val = Evaluate(rt);
            if (val > bestVal)
            {
                bestVal = val;
                bestRt = rt;
            }
        }

        return bestRt;
    }
}
