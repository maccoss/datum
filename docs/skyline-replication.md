# Replicating Skyline boundaries and area exactly

Datum reproduces Skyline's peak **boundaries** and peak **area** byte-identically by vendoring
Skyline's own algorithm code, not by approximating it.

## Why vendoring (not reimplementation)

Skyline's modern peak finder is fully-managed, plain C# and was written specifically to reproduce
the legacy native Crawdad's floating-point rounding. Reimplementing it by hand risks subtle
divergence (a single `float`-vs-`double` or a reordered sum changes the last bits). Since both
Skyline and datum are C#/.NET on IEEE-754, copying Skyline's source verbatim and preserving its
types, cast placement, and loop order yields the same bits for the same input.

## What is vendored

From [ProteoWizard/pwiz](https://github.com/ProteoWizard/pwiz) (Apache-2.0, same license as
datum), copied verbatim into
[`src/Datum.Skyline/Vendor/pwiz/PeakFinding/`](../src/Datum.Skyline/Vendor/pwiz/PeakFinding/)
(original namespace `pwiz.Common.PeakFinding`, copyright headers preserved):

| File | Role |
|------|------|
| `PeakFinder.cs` | Orchestrates detection and integration (`SetChromatogram`, `CalcPeaks`, `GetPeak`). |
| `PeakAndValleyFinder.cs` | The actual boundary detection (the managed "Crawdad"). |
| `ChromSmoother.cs` | Gaussian / derivative smoothers. |
| `FoundPeak.cs` | Per-peak area, background, height. |
| `PeakFinders.cs`, `IPeakFinder.cs`, `IFoundPeak.cs` | Factory and interfaces. |

Plus a tiny shim, `Vendor/pwiz/Collections/CollectionUtil.cs`, providing the one `BinarySearch`
overload the vendored code calls. These files are **excluded from `dotnet format`** and compiled
with warnings/nullable relaxed, so they stay verbatim and updates are a simple re-copy.

The thin wrappers that expose the vendored finder through datum's algorithm interfaces are
[`SkylineExactDetector`](../src/Datum.Skyline/SkylineExactDetector.cs) and
[`SkylineExactIntegrator`](../src/Datum.Skyline/SkylineExactIntegrator.cs). Because they live
outside `Datum.Core`, the application registers them at startup (see `MainWindowViewModel`).

## The two Skyline modes in the UI

The "Peak detection" dropdown offers two Skyline entries; both detect boundaries with the
vendored finder.

- **Skyline (boundaries only)** — uses Skyline's boundaries, but you choose the integrator, edge
  estimation, and background subtraction. The area controls stay enabled.
- **Skyline (exact area)** — uses Skyline's boundaries **and** Skyline's area calculation. The
  area controls grey out (the method is fixed): the integrator is forced to `SkylineExactIntegrator`
  and background subtraction is set to None, because Skyline's area is already background-subtracted.

## The boundary algorithm (PeakAndValleyFinder)

1. Pad the intensities with `_widthDataWings` copies of the baseline (`min` intensity) on each
   side, so floating-point rounding matches the old Crawdad.
2. Smooth with an **inverted second-derivative Gaussian** kernel. Width is auto-derived:
   `fwhm = max(6, half-height width at the global max)`, `fullWidthHalfMax = fwhm*3`,
   `sigma = fullWidthHalfMax / 2.35482`. The kernel keeps Crawdad's quirk `exp(-0.5*i*i/sd)`
   (dividing by `sd`, not `sd^2`), tails trimmed below 0.5% of the max weight.
3. Find zero crossings of the smoothed 2nd derivative (`SWITCH_LENGTH = 2` look-ahead), alternate
   them into peaks/valleys, and take the max (peak) / min (valley) between crossings.
4. From each apex, walk outward in the **raw padded intensities** to the first point `<= 0`
   (`minimum_level`); those are the boundaries (wing offset removed). Keep the peak only if it
   spans at least `min_len = round(fwhm/4)` points.
5. Candidate peaks are accepted only if `Height/RawHeight > 0.02` and `Area/RawArea > 0.02`, and
   ranked by descending area. Datum takes the top peak as the boundary.

## The area algorithm (FoundPeak + ChromPeak)

For a boundary `[startIndex, endIndex]` (see `FoundPeak.SetBoundaries` / `GetAreaUnderCurve`):

```text
rawArea        = trapezoid in index space = (sum of intensities) - yStart/2 - yEnd/2
backgroundLevel = min(I[startIndex], I[endIndex])
backgroundArea = trapezoid of  min(backgroundLevel, intensity[i])   over the same range
Area           = rawArea - backgroundArea            // background-subtracted
```

Byte-sensitive details that the vendored code (and so datum) preserves exactly:

- the accumulator is `double sum`, intensities are `float`, the result is cast `(float)`;
- summation is strictly ascending index, and the endpoint correction is exactly
  `sum - firstValue/2 - lastValue/2`;
- the background is **not** a straight endpoint-to-endpoint line — it is the area under
  `min(backgroundLevel, intensity)`, which follows the data wherever it dips below the lower
  boundary intensity. (For a flat baseline this reduces to the constant-level rectangle, which is
  why a constant background is removed exactly.)

Skyline then time-normalizes in its `ChromPeak` constructor:
`interval = (endTime - startTime) / (endIndex - startIndex)`, and with the default
`time_normalized` flag reports `Area * interval * 60` (intensity·**seconds**, interval in minutes).

### One deliberate unit difference

`SkylineExactIntegrator` returns `FoundPeak.Area * interval` — i.e. Skyline's exact arithmetic
**without** the `* 60` minutes→seconds display conversion — so the value is in
intensity·(retention-time unit) and is directly comparable to datum's ground-truth area, which is
computed in the same units. The deviation metric divides by that ground truth, so a global ×60
would corrupt it. To recover Skyline's exact reported number, supply retention time in minutes
and multiply this area by 60.

Datum also feeds the chromatogram to the finder as `float` (Skyline's storage type), so the inputs
match what Skyline would see.

## Multi-transition: "Integrate all"

Skyline's "Integrate all" applies one shared boundary to every transition of a precursor. Datum
mirrors this in `MultiTransitionEngine.DetectSharedBounds`: for any non-Osprey detector (including
Skyline) the boundary is found once on the **summed** transitions and then each transition is
re-integrated over that single boundary (Skyline reintegrates every transition to the precursor's
best peak). Osprey instead uses its median-CWT consensus.

## Validation (match now, validate later)

The vendored source is byte-identical to Skyline by construction; the remaining step is to
*prove* it against Skyline's own numbers. To add a reference fixture:

1. In Skyline, export (or read from the Results grid / a `.sky` document) a chromatogram as
   `times[]` (minutes) and `intensities[]`, plus Skyline's reported **start time**, **end time**,
   and **area**, and note the Skyline version.
2. Add a test in `tests/Datum.Core.Tests` that runs `SkylineExactDetector` / `SkylineExactIntegrator`
   on those points and asserts the boundaries match exactly and the area matches (remember the ×60:
   `datumArea * 60 == skylineArea` when `times` are in minutes).

## Known scope limits

- Datum uses Skyline's background-subtracted integration path (the normal precursor/SRM/PRM and
  MS1 case). Skyline's alternate `IntegrateWithoutBackground` path (DDA-fragment / triggered, with
  fractional interpolated endpoints) is not wired up; it can be added the same way if needed.
- Datum selects the top-area peak from the finder. Skyline's full peak *scoring* chooses among
  coeluting groups; for datum's single dominant synthetic peak these coincide.
