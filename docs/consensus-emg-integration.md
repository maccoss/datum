# Consensus EMG integration: recovering area under interference

When several transitions share one elution profile, an interference on one or two of them should not
corrupt the quantified area. The **Consensus EMG (median shape)** integrator
([ConsensusEmgIntegrator.cs](../src/Datum.Core/Algorithms/Integrators/ConsensusEmgIntegrator.cs))
extends the Osprey median-consensus idea from *boundary detection* to *integration*: it fits one
exponentially-modified Gaussian shape shared across transitions and recovers each transition's area
as a robust amplitude of that shape, so a minority of interfered transitions are rejected.

## Background: how transitions are quantified

The multi-transition engine ([MultiTransitionEngine.cs](../src/Datum.Core/Simulation/MultiTransitionEngine.cs))
determines one shared boundary for all transitions (Osprey median-CWT consensus, or "Integrate all"
for the other detectors), then for each transition:

- **background is subtracted per transition** (`IBackgroundSubtractor.Subtract` is applied to each
  transition's own trace), so a different background level or slope per transition is handled
  correctly: a linear baseline is drawn between that transition's own boundary intensities; and
- the trace is integrated.

By default each transition is integrated **independently**. With an independent EMG fit, an
interference bump on a transition is absorbed into that transition's fitted area, biasing it upward.

## What the consensus integrator does differently

A consensus integrator implements `IMultiTransitionIntegrator` and quantifies all transitions
jointly. `ConsensusEmgIntegrator.IntegrateAll`:

1. **Builds the per-point median across the (already background-subtracted) transitions.** A
   minority interference is suppressed pointwise, exactly as in the detector's apex consensus.
2. **Fits one EMG shape `(mu, sigma, tau)` to that median trace** (shared `EmgFit` helper).
3. **Recovers each transition's amplitude robustly.** For the fixed unit-area shape `g`, the area is
   the **median of `y_i / g_i`** over the in-peak points where `g_i` is at least 5% of its peak (so
   near-baseline tail points with tiny denominators do not add noise). Because the shape is taken
   from the clean consensus and the amplitude is a median, an interference bump on one or two
   transitions is rejected and the area under it is reconstructed from the shared shape.
4. Returns per-transition areas; the engine sums them.

In single-transition use it falls back to an independent EMG fit (identical to the EMG-fit
integrator), so it is also a valid standalone integrator.

## It works for a minority of interfered transitions

Six transitions sharing one symmetric (Gaussian) profile, two of them carrying a co-eluting
interference, quantified through the full pipeline (improved Osprey boundary, per-transition linear
baseline, Gaussian noise), deviation at 15 points across the peak:

| case | per-transition EMG | **consensus EMG** | trapezoid + edge |
|---|---:|---:|---:|
| clean | -1.4% | -2.0% | -2.0% |
| interference on 1/6 | +9.2% | **-2.1%** | +4.1% |
| interference on 2/6 | +20.2% | **+0.0%** | +12.4% |
| strong interference on 2/6 | +31.5% | **+6.7%** | +28.1% |

The consensus holds near zero while independent integration inflates with the interference. The
limit is a **minority**: once half or more of the transitions are interfered, the per-point median is
itself corrupted and the advantage disappears (a median rejects outliers only while they are the
minority). This matches the intended use of one or two interfered transitions out of several.

## Modelling per-transition interference and background

[MultiTransitionBuilder](../src/Datum.Core/Simulation/MultiTransitionBuilder.cs) can synthesize the
conditions needed to study this:

- **Per-transition interference:** a list of co-eluting peaks per transition (the UI places the
  configured interference on the first *N* transitions). Interference is added to the signal before
  noise and is deliberately **excluded from the true area** (it is contamination, not analyte).
- **Per-transition background:** a background per transition (the UI's "Vary background per
  transition" spreads the configured background across 0.5x..1.5x), so the per-transition
  subtraction path is exercised with genuinely different backgrounds.

In the app (multi-transition mode): set the interference shape under **Interference**, then
**Interfered transitions (first N)**; optionally tick **Vary background per transition** under
**Background**; choose **Consensus EMG (median shape)** as the integrator.

## Tailed peaks

The improved Osprey boundary is **tail-aware** (asymmetric): it places each edge at a relative-height
crossing of a fitted EMG shape, so the trailing edge reaches down the tail (see
[improved-integration-model.md](improved-integration-model.md#34-boundaries-per-side-relative-height-crossing-tail-aware-with-a-valley-guard)).
That feeds the per-transition fits enough of the tail to recover `tau`, so the consensus recovers the
area of a strongly tailed peak: in the same pipeline as above, on an EMG peak the clean deviation is
~-2% (not the ~-11% a symmetric boundary would give by chopping the tail), and with 2 of 6
transitions interfered the consensus stays near +6% while independent per-transition fitting inflates
to ~+19%.

## Future direction: replicate imputation with a fixed shape

> Design note. This describes how the consensus mechanism extends to Osprey's "impute boundaries for
> a non-detected peptide" workflow. It is **not implemented** in datum yet; it is recorded here
> because the integration math already exists and transfers directly.

In the Osprey search tool, when a peptide is not detected in a sample, the integration boundaries are
imputed from an aligned consensus of the other sample replicates. The natural question is whether an
EMG can be used with those imputed boundaries. The important distinction is between fixing the
*boundaries* and fixing the *shape*:

- **Fixing the boundaries is already supported in datum.** Detection and integration are decoupled:
  `PeakBounds` is a plain value with `StartRt`/`EndRt`/`ApexRt`, so an imputed boundary can be
  constructed directly and handed to any integrator without running a detector. With edge estimation
  the trapezoid then integrates to the imputed edges.
- **Re-fitting an EMG within fixed boundaries does not help a non-detected peptide.** `EmgFit.Fit`
  needs a real peak; with only noise inside the window it returns NA by design (too few usable points
  or an ill-conditioned fit). There is nothing to fit, so "fixed boundaries + per-sample EMG fit"
  yields no usable imputed area.
- **The right model is a fixed *shape*, not just fixed boundaries.** This is exactly the consensus
  idea with the consensus taken across **replicates** instead of across transitions:
  1. take `(mu, sigma, tau)` from the aligned consensus of the replicates where the peptide is
     well-measured;
  2. for the non-detected sample, hold that shape fixed and recover only a **robust amplitude** (the
     `median(y_i / g_i)` step against the fixed unit-area shape `g`).

  The amplitude lands at the noise floor, which is the correct imputed value, and because the
  integration is weighted by the known peak shape it is more precise than a fixed-boundary trapezoid
  that simply sums noise across the window.

What transfers directly: [`EmgFit.UnitDensity`](../src/Datum.Core/Algorithms/Integrators/EmgFit.cs)
and the robust-amplitude step in
[`ConsensusEmgIntegrator`](../src/Datum.Core/Algorithms/Integrators/ConsensusEmgIntegrator.cs). What
would need adding to model and validate this in datum:

- a `FixedShapeEmgIntegrator(mu, sigma, tau)` that skips the fit and runs only the robust-amplitude
  step against an externally supplied shape (and the imputed boundaries); and
- a **replicate / alignment axis** in the simulator (datum currently models only the within-sample,
  across-transition consensus), so the non-detected-sample case can be synthesized and the imputed
  area compared against ground truth.

The boundaries should be fixed alongside the shape (they come from the same imputation), so edge
estimation integrates the fixed shape to the imputed edges consistently.

## Tests

[MultiTransitionTests](../tests/Datum.Core.Tests/MultiTransitionTests.cs):

- `ConsensusEmg_recovers_area_under_interference_in_a_minority_of_transitions` — with 2 of 6
  transitions interfered, the consensus deviation is small and at least 3x better than independent
  per-transition EMG.
- `ConsensusEmg_matches_per_transition_emg_when_all_clean` — no penalty when there is no interference.
- `MultiTransition_builder_applies_per_transition_interference_and_background` — the builder injects
  per-transition interference and background, and excludes both from the true area.
