# Replicating the Osprey CWT median peak-boundary detection

Datum's "Osprey CWT" detector is a faithful port of the continuous-wavelet-transform consensus
peak detection in [Osprey](https://github.com/maccoss/osprey),
`crates/osprey-chromatography/src/cwt.rs`. The port lives in
[`OspreyCwtDetector`](../src/Datum.Core/Algorithms/Detectors/OspreyCwtDetector.cs) with the pure
math in [`CwtMath`](../src/Datum.Core/Algorithms/Cwt/CwtMath.cs).

> Scope of "exact": this is an **algorithmic** port — the steps and constants match `cwt.rs`. It
> is not byte-identical to Osprey's Rust output the way the Skyline path is, because it runs in a
> different language/runtime and on datum's uniformly-sampled trace rather than real scan-spaced
> XICs. If byte-identity to Osprey were required, we would vendor/port the Rust crate and validate
> against it, exactly as we did for Skyline (see [skyline-replication.md](skyline-replication.md)).

## The algorithm

Given one or more transition XICs sampled on a shared retention-time grid:

1. **Scale estimate** (`CwtMath.EstimateScale`). For each XIC, measure the FWHM (linear
   interpolation at half-max); take the median across XICs and set the wavelet scale
   `sigma = medianFWHM / 2.355`, clamped to `[2, 20]` samples (fallback `4.0` if no FWHM is
   measurable). Matches Osprey's `estimate_cwt_scale`.

2. **Mexican-hat (Ricker) kernel** (`CwtMath.MexicanHatKernel`), radius `ceil(5*sigma)`:

   ```text
   norm   = 2 / ( sqrt(3*sigma) * pi^(1/4) )
   psi(t) = norm * (1 - t^2) * exp(-t^2 / 2),   t = (i - center) / sigma
   ```

   followed by a zero-mean correction (subtract the mean weight) so the kernel removes any DC
   offset. Matches Osprey's `mexican_hat_kernel`.

3. **Convolve** each XIC with the kernel (`CwtMath.ConvolveSame`, direct "same"-size, zero-padded).

4. **Median consensus.** Take the pointwise **median** of the per-transition CWT responses. The
   median rejects single-transition interference (a spurious spike on one transition does not move
   the consensus). With a single transition the consensus reduces to that one response.

5. **Apex.** The consensus maximum is the apex; if it is `<= 0` no peak is reported. The apex is
   then refined to the maximum of the raw reference signal (sum of the XICs) within the
   zero-crossing window.

6. **Boundaries.** Walk out from the apex to where the consensus crosses zero (≈ ±1 sigma), then
   extend to ≈ ±2 sigma (`targetStart = apex - 2*leftSigma`, symmetric on the right).

7. **Valley guard.** While extending toward the ±2-sigma target, track the running minimum of the
   raw signal; if the signal rises more than `5%` of the apex above that running minimum, stop at
   the valley. This prevents bleeding into an adjacent co-eluting peak. Matches Osprey's valley
   logic (`ValleyRiseFraction = 0.05`).

8. **Asymmetric FWHM cap** (`cap_factor = 2.0`). Finally clamp the boundaries to
   `apex ± 2 * half-width-at-half-maximum` (measured on the raw signal). Osprey applies this cap so
   the wavelet scale cannot over-extend the boundaries past ~2 sigma. (Porting this cap was what
   brought datum's Osprey boundaries to the correct ~2-sigma width; without it the CWT scale
   produced boundaries that were too wide.)

## Osprey CWT (improved): the integration model

The registry also exposes **"Osprey CWT (improved)"** (`new OspreyCwtDetector(improved: true)`).
It keeps the interference-robust median-CWT consensus apex but changes how the integration
boundaries are placed, so that the recovered area is consistent regardless of how densely the peak
is sampled. It is the integration model we intend to adopt in Skyline and Osprey.

Differences from the faithful detector:

- **Width in retention time, not samples.** The peak standard deviation is estimated as the median
  per-transition FWHM divided by 2.355 (`CwtMath.EstimateSigmaRt`). Taking the median across
  transitions rejects the width inflation of a single interfered transition, and it is not clamped
  to a sample-count range, so it stays accurate at coarse sampling.
- **Fixed fractional boundaries.** Boundaries are placed at `apex +/- BoundarySigmaMultiple * sigma`
  (default 3.0 sigma, configurable in the UI / `DetectorParams`) as **exact fractional retention
  times**, not snapped to a sampled point. `ProducesFractionalBoundaries` is therefore true, so an
  edge-estimating integrator (the trapezoid rule with edge estimation on) integrates to the true
  boundary and the area no longer depends on where samples happen to fall.
- **Apex refined on the smoothed reference.** The apex is taken as the maximum of the same
  Savitzky-Golay-smoothed reference the boundary walk uses, so walking outward is monotonic and the
  valley guard fires only at a genuine adjacent peak rather than on the climb back to a slightly
  noise-displaced apex.
- **Interference still handled two ways:** the boundary reference is the per-point median across
  transitions (a single interfered transition cannot move it), and the valley guard truncates the
  boundary before a genuine co-eluting peak shared across transitions.

Why 3.0 sigma by default: at that boundary a Gaussian is ~1% of its apex, so a linear baseline drawn
between the two boundary points over-subtracts almost nothing while only ~0.3% of the peak area lies
beyond it. Tighter boundaries (2.5 sigma) leave ~4% of the apex height for the baseline to
over-subtract (~-8% area with a linear baseline); wider boundaries capture more noise. This is the
same reason the SciPy peak finder at relative height 0.99 (its ~1%-height, ~3 sigma boundary) with
edge estimation performs well.

## Single vs multi-transition

- `Detect(rt, intensity, params)` wraps the single trace as a one-element XIC set, so the
  consensus is just that trace's wavelet response.
- `DetectFromXics(rt, xics, params)` is the genuine multi-transition consensus and is what the
  multi-transition (Koina) pipeline calls. In that pipeline the median-CWT consensus is Osprey's
  distinguishing behavior; other detectors instead share one boundary from the summed transitions
  (see [skyline-replication.md](skyline-replication.md) and `MultiTransitionEngine.DetectSharedBounds`).

## Tests

[`SimulationTests`](../tests/Datum.Core.Tests/SimulationTests.cs) and
[`AdvancedAlgorithmTests`](../tests/Datum.Core.Tests/AdvancedAlgorithmTests.cs) cover:

- the kernel is zero-mean and symmetric, and `EstimateScale` clamps to `[2, 20]`;
- the median consensus ignores a single-transition interference spike (apex stays on the real
  peak);
- the detected apex lands near the true center, and the FWHM cap keeps the width near ~2 sigma.

## Not ported

Osprey is a full DIA search engine; only its chromatographic boundary detection is relevant here.
Not ported (and not needed for datum's boundary modeling): the EMG-fit boundary refinement option,
LOESS retention-time calibration, cross-run reconciliation, and the ~45-feature peak scoring set.
