# Adding a peak-area (integrator) model

Integrators compute the area under a detected peak. Like detectors, they implement one interface
and appear in the UI once registered.

## The contract

`IIntegrator` ([src/Datum.Core/Algorithms/Interfaces.cs](../src/Datum.Core/Algorithms/Interfaces.cs)):

```csharp
public interface IIntegrator
{
    string Name { get; }
    double Integrate(double[] rt, double[] intensity, PeakBounds bounds, IntegratorOptions options);
}
```

- `rt` / `intensity` are the sampled points. **`intensity` has already had the selected background
  subtractor applied** by the pipeline before it reaches the integrator (so most integrators do
  not subtract background themselves).
- `bounds` is the detected peak (`bounds.StartIndex..EndIndex`, plus the exact `StartRt`/`EndRt`).
- `IntegratorOptions` currently carries `bool EdgeEstimation` (insert fractional-trapezoid points
  at the exact boundaries).
- Return the area as `double`.

### Units convention (important)

Return the area as the integral of intensity over retention time, **∫ intensity d(rt)**, in the
retention-time units of `rt`. This is what the ground-truth `IPeakModel.TrueArea()` and every
built-in integrator return, so the deviation-from-truth comparison is apples-to-apples. Do not
apply a unit conversion (e.g. minutes→seconds) inside an integrator; a global constant factor
would corrupt the deviation metric, which compares against a ground truth in rt-units.

## Steps

1. **Create the class** under `src/Datum.Core/Algorithms/Integrators/`:

   ```csharp
   namespace Datum.Core.Algorithms.Integrators;

   public sealed class MidpointIntegrator : IIntegrator
   {
       public string Name => "Midpoint sum";

       public double Integrate(double[] rt, double[] intensity, PeakBounds bounds, IntegratorOptions options)
       {
           if (!bounds.Detected) return 0.0;
           double area = 0.0;
           for (int i = bounds.StartIndex; i < bounds.EndIndex && i + 1 < rt.Length; i++)
               area += intensity[i] * (rt[i + 1] - rt[i]);   // left Riemann, ∫ intensity d(rt)
           return area;
       }
   }
   ```

2. **Register it** in [AlgorithmRegistry.cs](../src/Datum.Core/Algorithms/AlgorithmRegistry.cs):
   `Register(new MidpointIntegrator());`. It then appears in the "Integration" dropdown.

3. **Test it** against `IPeakModel.TrueArea()` on a densely-sampled clean peak (see
   `AlgorithmTests.cs` / `AdvancedAlgorithmTests.cs`).

## Reusing the integration region (recommended for edge estimation)

To honor the `EdgeEstimation` option *and* keep the on-screen shaded area exactly matching the
computed area, build the integration points with the shared helper rather than rolling your own
edge logic:

```csharp
(double[] xs, double[] ys) = IntegrationGeometry.InPeakPoints(rt, intensity, bounds, options.EdgeEstimation);
```

[`IntegrationGeometry`](../src/Datum.Core/Algorithms/Integrators/IntegrationGeometry.cs) returns
the in-peak samples and, when edge estimation is on, interpolated points at the exact
`StartRt`/`EndRt`. The UI uses the same helper to draw the fill, so the shaded region always
reaches the boundaries the same way your area does. `TrapezoidIntegrator` is the reference user.

## Curve-fit integrators

Fitting integrators (Gaussian, EMG) extrapolate the analytic tails beyond the sampled boundaries.
They live alongside the others and reuse
[`LevenbergMarquardt`](../src/Datum.Core/Math/LevenbergMarquardt.cs) and the special functions in
[`MathFunctions`](../src/Datum.Core/Math/MathFunctions.cs). Return the analytic area of the fitted
model (in rt-units). See `GaussianFitIntegrator` (closed-form) and `EmgFitIntegrator` (LM).

## Integrators outside Datum.Core

An integrator needing outside dependencies lives in its own project referencing `Datum.Core` and
is registered by the application (see `_registry.Register(new SkylineExactIntegrator());` in
[MainWindowViewModel.cs](../src/Datum.App/ViewModels/MainWindowViewModel.cs)). The byte-identical
`SkylineExactIntegrator` in `Datum.Skyline` is the worked example.

## Multi-transition

In multi-transition mode each transition is integrated over the single shared boundary and the
areas are summed. Your integrator is called once per transition with the same `bounds`; no extra
work is required.
