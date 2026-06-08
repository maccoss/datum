//! Small Levenberg-Marquardt least-squares optimizer with a numerical Jacobian.

namespace Datum.Core.Math;

/// <summary>
/// A compact Levenberg-Marquardt nonlinear least-squares fitter for low-dimensional models
/// (a handful of parameters, modest data). Uses a forward-difference Jacobian, which is
/// adequate for the smooth peak models fitted here. Used by the curve-fit integrators.
/// </summary>
public static class LevenbergMarquardt
{
    /// <summary>
    /// Fit <paramref name="model"/>(parameters, x) to the data (<paramref name="x"/>,
    /// <paramref name="y"/>) starting from <paramref name="initial"/>.
    /// </summary>
    /// <param name="x">Independent variable samples.</param>
    /// <param name="y">Observed values.</param>
    /// <param name="initial">Initial parameter guess (also defines the parameter count).</param>
    /// <param name="model">Model evaluated as <c>model(parameters, x)</c>.</param>
    /// <param name="maxIterations">Maximum iterations.</param>
    /// <returns>The fitted parameters (or the last iterate if convergence was not reached).</returns>
    public static double[] Fit(
        double[] x,
        double[] y,
        double[] initial,
        System.Func<double[], double, double> model,
        int maxIterations = 100)
    {
        int n = x.Length;
        int p = initial.Length;
        var beta = (double[])initial.Clone();
        double lambda = 1e-3;
        double cost = Cost(x, y, beta, model);

        for (int iter = 0; iter < maxIterations; iter++)
        {
            // Numerical Jacobian (forward difference).
            // Evaluate the model at the current parameters once and reuse it for both the
            // residuals and the forward-difference Jacobian (avoids re-evaluating the base model
            // p times per point, which dominated the cost for the EMG fit).
            var jac = new double[n][];
            var residual = new double[n];
            var f0 = new double[n];
            for (int i = 0; i < n; i++)
            {
                f0[i] = model(beta, x[i]);
                residual[i] = y[i] - f0[i];
                jac[i] = new double[p];
            }

            for (int k = 0; k < p; k++)
            {
                double step = System.Math.Max(1e-6, System.Math.Abs(beta[k]) * 1e-6);
                var bumped = (double[])beta.Clone();
                bumped[k] += step;
                double invStep = 1.0 / step;
                for (int i = 0; i < n; i++)
                {
                    jac[i][k] = (model(bumped, x[i]) - f0[i]) * invStep;
                }
            }

            // Normal equations: (JtJ + lambda*diag) delta = Jt r.
            var jtj = new double[p, p];
            var jtr = new double[p];
            for (int i = 0; i < n; i++)
            {
                for (int a = 0; a < p; a++)
                {
                    jtr[a] += jac[i][a] * residual[i];
                    for (int b = 0; b < p; b++)
                    {
                        jtj[a, b] += jac[i][a] * jac[i][b];
                    }
                }
            }

            var augmented = (double[,])jtj.Clone();
            for (int a = 0; a < p; a++)
            {
                augmented[a, a] += lambda * (jtj[a, a] + 1e-12);
            }

            if (!TrySolve(augmented, jtr, out double[] delta))
            {
                break;
            }

            var candidate = (double[])beta.Clone();
            for (int a = 0; a < p; a++)
            {
                candidate[a] += delta[a];
            }

            double candidateCost = Cost(x, y, candidate, model);
            if (candidateCost < cost)
            {
                if (System.Math.Abs(cost - candidateCost) < 1e-12 * (1.0 + cost))
                {
                    beta = candidate;
                    break;
                }

                beta = candidate;
                cost = candidateCost;
                lambda = System.Math.Max(lambda * 0.5, 1e-9);
            }
            else
            {
                lambda = System.Math.Min(lambda * 4.0, 1e9);
            }
        }

        return beta;
    }

    private static double Cost(double[] x, double[] y, double[] beta, System.Func<double[], double, double> model)
    {
        double sum = 0.0;
        for (int i = 0; i < x.Length; i++)
        {
            double r = y[i] - model(beta, x[i]);
            sum += r * r;
        }

        return sum;
    }

    /// <summary>Solve A·z = b by Gaussian elimination with partial pivoting.</summary>
    private static bool TrySolve(double[,] a, double[] b, out double[] z)
    {
        int n = b.Length;
        var m = (double[,])a.Clone();
        var rhs = (double[])b.Clone();
        z = new double[n];

        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            double best = System.Math.Abs(m[col, col]);
            for (int r = col + 1; r < n; r++)
            {
                double v = System.Math.Abs(m[r, col]);
                if (v > best)
                {
                    best = v;
                    pivot = r;
                }
            }

            if (best < 1e-15)
            {
                return false;
            }

            if (pivot != col)
            {
                for (int c = 0; c < n; c++)
                {
                    (m[col, c], m[pivot, c]) = (m[pivot, c], m[col, c]);
                }

                (rhs[col], rhs[pivot]) = (rhs[pivot], rhs[col]);
            }

            for (int r = col + 1; r < n; r++)
            {
                double factor = m[r, col] / m[col, col];
                for (int c = col; c < n; c++)
                {
                    m[r, c] -= factor * m[col, c];
                }

                rhs[r] -= factor * rhs[col];
            }
        }

        for (int r = n - 1; r >= 0; r--)
        {
            double sum = rhs[r];
            for (int c = r + 1; c < n; c++)
            {
                sum -= m[r, c] * z[c];
            }

            z[r] = sum / m[r, r];
        }

        return true;
    }
}
