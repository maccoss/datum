# Adding a peak-boundary (detector) model

Peak detectors find the apex and the integration boundaries of a peak on a sampled
chromatogram. They implement a single interface and are picked up by the UI automatically once
registered.

## The contract

`IPeakDetector` ([src/Datum.Core/Algorithms/Interfaces.cs](../src/Datum.Core/Algorithms/Interfaces.cs)):

```csharp
public interface IPeakDetector
{
    string Name { get; }                                            // shown in the UI / registry key
    PeakBounds Detect(double[] rt, double[] intensity, DetectorParams p);
}
```

- `rt` and `intensity` are the **sampled** points (sorted by `rt`, uniform spacing in datum's
  sampling grid). They are not the high-resolution display trace.
- Return a `PeakBounds` ([PeakBounds.cs](../src/Datum.Core/Algorithms/PeakBounds.cs)):

  ```csharp
  public readonly record struct PeakBounds(
      bool Detected, int StartIndex, int EndIndex, int ApexIndex,
      double StartRt, double EndRt, double ApexRt);
  ```

  Return `PeakBounds.NotFound` when no peak is found. `StartIndex`/`EndIndex`/`ApexIndex` must be
  valid indices into the passed arrays (integrators index the arrays with them). `StartRt`/`EndRt`
  may be interpolated *between* samples (e.g. at a fractional threshold crossing); integrators
  that do edge estimation use those exact times.

- `DetectorParams` carries the shared tunables (all optional, with UI controls):

  ```csharp
  public readonly record struct DetectorParams(
      double HeightFraction = 0.5,     // min apex height as a fraction of max
      double Prominence = 0.0,         // min topographic prominence
      double BoundaryRelHeight = 0.99);// boundary at this fractional drop (0.99 = wide, 0.5 = FWHM)
  ```

  Use whichever apply to your method; ignore the rest. (The sampling "points across the peak"
  reference width is independent of `BoundaryRelHeight` — it is fixed at the 1%-of-apex extent.)

## Steps

1. **Create the class** under `src/Datum.Core/Algorithms/Detectors/` (or in another project if it
   needs outside dependencies — see "Detectors outside Datum.Core").

   ```csharp
   namespace Datum.Core.Algorithms.Detectors;

   public sealed class HalfMaxDetector : IPeakDetector
   {
       public string Name => "Half-max width";

       public PeakBounds Detect(double[] rt, double[] intensity, DetectorParams p)
       {
           if (intensity.Length < 2) return PeakBounds.NotFound;

           int apex = 0;
           double max = intensity[0];
           for (int i = 1; i < intensity.Length; i++)
               if (intensity[i] > max) { max = intensity[i]; apex = i; }
           if (max <= 0) return PeakBounds.NotFound;

           double half = 0.5 * max;
           int s = apex; while (s > 0 && intensity[s] >= half) s--;
           int e = apex; while (e < intensity.Length - 1 && intensity[e] >= half) e++;

           return new PeakBounds(true, s, e, apex, rt[s], rt[e], rt[apex]);
       }
   }
   ```

2. **Register it.** In [AlgorithmRegistry.cs](../src/Datum.Core/Algorithms/AlgorithmRegistry.cs)'s
   constructor add `Register(new HalfMaxDetector());`. The registry's `Detectors` list feeds the
   UI's "Peak detection" dropdown, so it appears automatically — no UI code to touch.

3. **(Optional) interpolate the boundary times.** For sub-sample accuracy, set `StartRt`/`EndRt`
   to the interpolated crossing rather than `rt[s]`/`rt[e]`. `ThresholdDetector` shows the
   pattern; `Chromatogram.Interpolate(rt, intensity, level)` is a handy linear interpolation.

4. **Test it.** Add a fact to [tests/Datum.Core.Tests](../tests/Datum.Core.Tests/) that samples a
   known peak (`SamplingGrid.Create` + `ChromatogramBuilder.SampleNoisy`) and asserts the apex /
   boundaries land where expected. See `AlgorithmTests.cs` for the pattern.

## Detectors outside Datum.Core

`Datum.Core` has no dependencies, so an algorithm needing extra libraries (or vendored
third-party code) lives in its own project that references `Datum.Core` — as the byte-identical
Skyline detector does in `Datum.Skyline`. Such detectors are registered by the **application**
rather than in `AlgorithmRegistry`, because Core cannot reference them. See the
`_registry.Register(new SkylineExactDetector());` line in
[MainWindowViewModel.cs](../src/Datum.App/ViewModels/MainWindowViewModel.cs).

## How the boundary is used downstream

- For **single-trace** quantification the boundary feeds the selected integrator directly.
- For **multi-transition** quantification one shared boundary is applied to every transition. The
  rule lives in `MultiTransitionEngine.DetectSharedBounds`: Osprey uses its median-CWT consensus
  across transitions; every other detector (including Skyline) is run on the **summed**
  transitions and that one boundary is applied to all (Skyline's "Integrate all" behavior). A new
  detector automatically gets the summed-signal behavior; override `DetectSharedBounds` only if it
  needs something special.

## UI parameter visibility (optional)

The Methods panel greys parameters that do not apply to the selected detector via computed
properties in the view model (`IsScipyDetector`, `DetectorUsesBoundaryRelHeight`). If your
detector ignores `BoundaryRelHeight`, add its name to the relevant predicate so the control greys
out; otherwise no UI change is needed.
