# The Osprey CWT (improved) boundary and integration model

This is the integration model datum is designed to evaluate and that we intend to adopt in
Skyline and Osprey. Its goal is a recovered peak area that is **consistent regardless of how
densely the peak is sampled** (points across the peak), while keeping the interference robustness
of the Osprey median-CWT consensus.

It is selected in the UI as **"Osprey CWT (improved)"** and constructed in code as
`new OspreyCwtDetector(improved: true)` in
[OspreyCwtDetector.cs](../src/Datum.Core/Algorithms/Detectors/OspreyCwtDetector.cs). It pairs with
the [Trapezoid integrator](../src/Datum.Core/Algorithms/Integrators/TrapezoidIntegrator.cs) with
**edge estimation enabled**.

If you only want the short version, see the summary in
[osprey-cwt-replication.md](osprey-cwt-replication.md#osprey-cwt-improved-the-integration-model).
This document is the detailed rationale.

---

## 1. The problem: snapped boundaries make area sampling-dependent

Most detectors snap their integration boundaries to a sampled retention time. When you integrate
the trapezoidal area between two *samples*, the answer depends on exactly where those samples fall
relative to the true peak edges, and that changes with the sampling rate and the sampling phase.
Two failure modes follow:

- **Coarse sampling moves the boundary.** A boundary "at the baseline" snaps to the nearest sample,
  which at low points-across-peak can be far from the true edge. The faithful Osprey detector
  (index-based) over-extends at coarse sampling and lands at ~2.35 sigma at fine sampling, so its
  area drifts with sampling density (it reaches ~-7% at 30 points under a linear baseline, versus
  ~0% at 4 points; see [Section 7](#7-measured-performance)).
- **The last partial trapezoid is dropped.** Without edge estimation, the area between the
  outermost in-peak sample and the true boundary is simply not counted, which systematically
  under-integrates at low sampling (the classic Pino ~10-points-across-the-peak observation).

The SciPy peak finder happens to do well precisely because it reports a *fractional* boundary
(`peak_widths` interpolates the relative-height crossing) and, with edge estimation on, integrates
to that fractional boundary. The improved model generalizes that idea and gives it a physically
defined width.

---

## 2. The model in one paragraph

Find the apex with the interference-robust median-CWT consensus (unchanged from faithful Osprey).
Place each integration boundary **where the peak returns to a fixed fraction of its apex height**,
`exp(-k^2/2)` (default `k = 3.0`, i.e. ~1.1%), measured **per side** so a tailed peak gets an
asymmetric boundary (the trailing edge reaches farther than the leading edge). On a symmetric
(Gaussian) peak the two sides coincide and this is exactly `apex +/- k * sigma`. The boundaries are
**exact fractional retention times**, not snapped to a sample, so an edge-estimating integrator fills
the fractional trapezoid out to the true boundary. Because the boundary tracks the peak's own shape in
real time units, the integrated region is the same physical interval no matter how the peak is
sampled, so the area is flat across sampling density.

---

## 3. Step by step

The improved branch is in
[`OspreyCwtDetector.DetectFromXics`](../src/Datum.Core/Algorithms/Detectors/OspreyCwtDetector.cs).

### 3.1 Apex: median-CWT consensus (interference-robust)

Each transition XIC is convolved with a Mexican-hat (Ricker) wavelet whose scale is matched to the
peak width, and the **pointwise median** of those responses across transitions is taken. The
consensus maximum is the apex. Because it is a per-point median across transitions, interference
present in only a minority of transitions does not move it. This is the same consensus described in
[osprey-cwt-replication.md](osprey-cwt-replication.md).

### 3.2 Apex refined on the *smoothed* reference

The apex is then refined to the maximum of a 5-point Savitzky-Golay-smoothed reference, within the
consensus zero-crossing window:

```csharp
double[] smoothed = Smoothing.SavitzkyGolay5(reference);
for (int i = leftZero; i <= rightZero; i++)
    if (smoothed[i] > smoothed[apex]) apex = i;
```

This matters because the boundary walk (Section 3.4) uses the same smoothed reference. If the apex
were taken from the raw signal and noise nudged it onto a shoulder, walking back toward the true
maximum would look like a *rising* signal and trip the valley guard, collapsing one boundary to a
near-zero-area sliver. Refining on the smoothed reference keeps the walk outward monotonic. (This
fix took the catastrophic-collapse rate from ~0.6% of iterations to 0.)

### 3.3 Width: median per-transition FWHM in retention time

Sigma is estimated by
[`CwtMath.EstimateSigmaRt`](../src/Datum.Core/Algorithms/Cwt/CwtMath.cs):

```text
sigma_rt = median_over_transitions( FWHM_rt(transition) ) / 2.355
```

Four properties matter:

- **Half-maximum measured above the baseline.** The FWHM crossing is taken at
  `floor + 0.5 * (apex - floor)`, where `floor` is the minimum of the trace, **not** at half of the
  absolute apex intensity. This makes the width (and therefore the boundaries) independent of a
  constant chemical background. Measuring at half of the absolute height was a bug: with a large
  background the half-height sat far up the tails, inflating the FWHM, the CWT scale, and the
  boundaries (the boundaries visibly walked outward as the background was raised). The same
  correction is applied to the kernel-scale estimate, so the wavelet is matched to the peak, not to
  the peak-plus-background. The CWT response itself is background-independent because the Mexican-hat
  wavelet is zero-mean (the second derivative annihilates an additive constant); the baseline
  correction brings the FWHM-derived width and scale into line with that property.
- **Median across transitions** rejects the width inflation of a single interfered transition (the
  same logic as the consensus apex).
- **Measured at each transition's own half-maximum**, which sits well above the noise floor, so the
  estimate is stable.
- **Not clamped to a sample count.** The related
  [`EstimateScaleRt`](../src/Datum.Core/Algorithms/Cwt/CwtMath.cs) (used only to size the wavelet
  kernel) clamps to `[1, 50]` samples; that clamp is wrong for a boundary width because at 3 points
  across the peak it forces sigma to be at least one sample (~2x the true width), which is what
  pushed the old "RT-scaled" boundaries out to the window edge. `EstimateSigmaRt` deliberately omits
  the clamp.

> **Why not read the width directly off the consensus zero-crossings?** The Mexican-hat response of
> a Gaussian peak crosses zero at `apex +/- sqrt(sigma_p^2 + s^2)`, where `s` is the wavelet scale,
> so the zero-crossing distance is a width proxy that still depends on the chosen scale (it is *not*
> the FWHM directly). Recovering `sigma_p = sqrt(zc^2 - s^2)` works once the scale is
> background-corrected, but it is less stable than the baseline-corrected FWHM at very low sampling
> (where the scale clamp dominates and the subtraction degenerates). The baseline-corrected FWHM is
> background-independent for the same underlying reason and is more robust, so it is the width source
> used here.

### 3.4 Boundaries: per-side relative-height crossing (tail-aware), with a valley guard

The target is a fixed fraction of the apex height, mapped from the same `k` knob:
`fraction = exp(-k^2/2)` (k = 3 -> ~1.1%). The two edges are placed where the peak returns to that
fraction **independently on each side**, so a tailed peak gets an asymmetric boundary. The trailing
crossing sits near baseline on a shallow slope, where a raw data crossing would be noisy, so it is
taken on a **noise-free analytic model**: an EMG shape is fitted to the smoothed median consensus
(`FitConsensusShape` -> [`EmgFit`](../src/Datum.Core/Algorithms/Integrators/EmgFit.cs)), and
[`EmgFit.HeightCrossings`](../src/Datum.Core/Algorithms/Integrators/EmgFit.cs) returns the leading and
trailing retention times at the target fraction.

```csharp
double k = Math.Clamp(p.BoundarySigmaMultiple, 1.0, 6.0);
double startTarget = apexRt - k * sigmaRt;   // symmetric fallback if the fit fails
double endTarget   = apexRt + k * sigmaRt;
EmgFit.Result? shape = FitConsensusShape(smoothed, rt, apex, sigmaRt, smoothApex);
if (shape is { Tau: > 0.0 } s)
{
    (startTarget, endTarget) = EmgFit.HeightCrossings(s.Mu, s.Sigma, s.Tau, Math.Exp(-0.5 * k * k));
}
startRt = BoundaryRt(smoothed, rt, apex, -1, startTarget, smoothApex);
endRt   = BoundaryRt(smoothed, rt, apex, +1, endTarget,   smoothApex);
```

On a symmetric peak the fitted `tau` is negligible and the crossings reduce to `apex +/- k * sigma`,
so this is a strict generalization of the symmetric model. `BoundaryRt` then walks from the apex
toward each target and:

- returns the **exact fractional target** if it is reached first (the normal case; the boundary is
  *not* a sampled point);
- stops at a **valley** if the smoothed reference rises by more than 5% of the apex first (a genuine
  co-eluting peak shared across transitions), so interference cannot be integrated in;
- returns the array edge if that comes first.

The symmetric `sigma` (3.3) is still used as the fit-window size and as the fallback when the EMG fit
fails or is degenerate, so the detector never regresses on peaks where the fit is poor.

`StartIndex` / `EndIndex` are the first / last sampled points strictly inside `[StartRt, EndRt]`;
`StartRt` / `EndRt` carry the fractional boundary. `ProducesFractionalBoundaries` returns true for
this variant, which is what makes the UI enable edge estimation (see
[extending-detectors.md](extending-detectors.md) for the flag's contract).

Measured boundary widths (clean, 25 points across the peak), showing the asymmetry emerge with tau:

| peak | leading half-width | trailing half-width | trailing / leading |
|---|---:|---:|---:|
| Gaussian sigma=0.30 | 0.86 | 0.95 | 1.11 |
| EMG sigma=0.25 tau=0.35 | 0.87 | 1.76 | 2.02 |
| EMG sigma=0.25 tau=0.70 | 1.00 | 3.24 | 3.23 |

### 3.5 Interference handling is preserved two ways

1. The reference the boundary walks is the **per-point median across transitions**, so interference
   in a minority of transitions cannot move the boundary. (Regression test:
   `OspreyCwt_improved_boundary_rejects_single_transition_interference` shifts the boundary by
   < 0.02 RT when one of six transitions has a large co-eluting peak, versus the > 1 RT drag a
   summed reference would suffer.)
2. The **valley guard** truncates the boundary before a genuine adjacent peak that *is* present
   across transitions.

---

## 4. Integration: the fractional-trapezoid edge

The detector only defines the region. The area comes from the
[Trapezoid integrator](../src/Datum.Core/Algorithms/Integrators/TrapezoidIntegrator.cs) with
`EdgeEstimation = true`, whose geometry is in
[IntegrationGeometry.cs](../src/Datum.Core/Algorithms/Integrators/IntegrationGeometry.cs):

- collect the in-peak samples `StartIndex..EndIndex`;
- linearly interpolate the intensity at the exact `StartRt` and `EndRt`;
- prepend / append those interpolated edge points so the trapezoid sum runs from the true boundary
  to the true boundary, not from the outermost sample.

Because `StartRt`/`EndRt` are fixed at `apex +/- k * sigma`, the integrated interval is the same
real-time width on every replicate; only the interior sample positions change, and the trapezoid
rule converges to the same area. The same `IntegrationGeometry` points drive the shaded fill in the
plot, so the picture always matches the number.

Fit integrators ([Gaussian fit](../src/Datum.Core/Algorithms/Integrators/GaussianFitIntegrator.cs),
[EMG fit](../src/Datum.Core/Algorithms/Integrators/EmgFitIntegrator.cs)) ignore edge estimation
(they fit an analytic shape to the in-peak points and integrate it in closed form), but they still
benefit from the improved boundary because a stable, baseline-reaching boundary gives the fit a
clean, well-conditioned set of points and a correctly anchored linear background.

---

## 5. Why 3.0 sigma by default

The boundary width trades off two opposing biases when a **linear baseline** is subtracted between
the two boundary points:

- **Too narrow:** the boundary sits high on the peak, so the linear baseline drawn between the two
  boundary points cuts off a large slab of real peak area (over-subtraction).
- **Too wide:** the boundary captures extra noise, and any baseline curvature error grows.

For a Gaussian, the intensity at `k * sigma` above baseline is `exp(-k^2 / 2)` of the apex height:

| `k` | height at boundary | area beyond boundary | dev with linear baseline | dev with no background |
|----:|-------------------:|---------------------:|-------------------------:|-----------------------:|
| 2.0 sigma | 13.5% | 4.6% | **-23%** | -5.5% |
| 2.5 sigma | 4.4% | 1.2% | **-8%** | -1.5% |
| **3.0 sigma** | **1.1%** | **0.27%** | **-1.5 to -3%** | **~0%** |
| 3.5 sigma | 0.4% | 0.05% | +0.5 to +1% | +0.7% |

(`dev` columns are measured multi-transition deviations at >= 5 points across the peak, bg = 50,
Gaussian noise = 20; see [Section 7](#7-measured-performance).)

At 3.0 sigma the boundary is ~1% of the apex, so the linear baseline over-subtracts almost nothing
while only ~0.3% of the peak lies outside the boundary, and the two residual biases nearly cancel.
This is also why the SciPy peak finder at relative height 0.99 (its ~1%-height, ~3 sigma boundary)
with edge estimation performs well. The multiple is configurable, so you can reproduce any row of
the table.

---

## 6. Using and configuring it in the app

1. **Peak detection:** select **Osprey CWT (improved)**.
2. **Integration:** select **Trapezoid** and tick **Edge estimation** (enabled automatically for
   this detector because it reports fractional boundaries). EMG fit / Gaussian fit also work.
3. **Background subtraction:** **Linear baseline** is the realistic choice; **None** shows the pure
   sampling effect (~0% from a few points up).
4. **Boundary width (x SD):** the numeric field that appears only for this detector. Default 3.0;
   try 2.5 for tighter boundaries or 3.5 to see the bias flip positive. Backed by
   `DetectorParams.BoundarySigmaMultiple` in
   [Interfaces.cs](../src/Datum.Core/Algorithms/Interfaces.cs) (clamped to `[1, 6]`).

For multiple transitions, the model uses
[`MultiTransitionEngine.DetectSharedBounds`](../src/Datum.Core/Simulation/MultiTransitionEngine.cs):
one shared boundary from the median consensus, but **each transition's area and background are
computed separately** so that a transition-specific background is handled per transition.

---

## 7. Measured performance

Multi-transition (6 fragments of ELVISLIVESR), constant background 50, Gaussian noise 20, 100
Monte-Carlo iterations per point count. Values are `mean deviation % / SD %`.

```text
                          3p     4p     5p     8p    10p    15p    30p
Improved(3sd)+Trap+edge -5.3   -2.0   -3.0   -1.7   -1.5   -1.5   -2.3
Improved(3sd)+EMG fit     NaN    0.3   -2.2   -1.3   -1.1   -1.0   -1.9
Faithful Osprey+Trap     +2.0    0.0   -2.9   -1.5   -4.4   -4.3   -6.9
```

Two things to note:

- The improved curve is **flat across sampling density** (~-2%), which is the whole point: the
  reported area no longer depends on how finely the peak was sampled.
- It is flatter than the faithful detector, whose snapped boundary narrows toward ~2.35 sigma as
  sampling gets finer and therefore drifts increasingly negative under a linear baseline.
- EMG fit reports `NaN` below 5 in-peak points (too few points to fit), by design.

With **no background subtraction** the improved + edge curve sits at ~0% from ~4 points onward
(only the ~0.3% Gaussian tail beyond 3 sigma is lost), with SD falling below 1% by 30 points.

---

## 8. Tests

In [AdvancedAlgorithmTests](../tests/Datum.Core.Tests/AdvancedAlgorithmTests.cs):

- `OspreyCwt_improved_places_fractional_boundary_near_sigma_multiple_and_recovers_area` — the
  boundary half-width is ~`k * sigma`, the boundary is not a sampled retention time, and the
  background-subtracted sweep deviation is modest.
- `OspreyCwt_improved_boundary_rejects_single_transition_interference` — interference in one of six
  transitions shifts the shared boundary by < 0.02 RT.

In [CoverageTests](../tests/Datum.Core.Tests/CoverageTests.cs):

- `Detector_and_integrator_capability_flags_are_correct` — the improved detector reports
  `ProducesFractionalBoundaries = true` (faithful Osprey reports false).

---

## 9. Relationship to the other methods

| | Boundary basis | Boundary lands on | Edge estimation | Area drifts with sampling? |
|---|---|---|---|---|
| Faithful Osprey CWT | CWT zero-crossings + 2x FWHM cap (index) | a sample | no (snapped) | yes |
| Skyline (exact) | Crawdad 2nd-derivative (vendored) | a sample | no (Skyline's own area) | yes |
| SciPy peak finder | relative-height crossing | fractional | yes | mostly no |
| **Osprey CWT (improved)** | **`k * sigma` in retention time** | **fractional** | **yes** | **no** |

See also [skyline-replication.md](skyline-replication.md) and
[osprey-cwt-replication.md](osprey-cwt-replication.md).

---

## 10. Intended future use in Skyline / Osprey

The model is deliberately simple to reimplement in a production peak finder: one robust width
estimate, one multiple, fractional boundaries, and a fractional-trapezoid edge. The payoff is that
quantitative results stop depending on acquisition sampling rate, which is exactly the property a
cross-instrument / cross-method quantification pipeline needs. The `BoundarySigmaMultiple` default of
3.0 is the recommended starting point; datum exists so the multiple and the surrounding choices can
be validated against ground truth before they ship in Skyline and Osprey.
