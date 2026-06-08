# Datum v26.1.0 Release Notes

First public release: a cross-platform desktop app for modeling how chromatographic peak
detection and area-integration algorithms recover the true peak area under realistic
distortions, and how sampling rate (points across the peak) affects quantification accuracy.

## New Features

- **Peak models:** Gaussian, skew-normal (tailing/fronting), and exponentially-modified
  Gaussian (EMG).
- **Distortions:** constant / linear / curved chemical background; additive Gaussian
  (signal-independent) noise; Poisson (signal-dependent / shot) noise; and a configurable
  co-eluting interference peak defined relative to the main peak.
- **Sampling:** points across the peak (sets the sampling interval) and a sampling offset, with
  off-peak baseline points generated automatically across the window.
- **Pluggable detection:** threshold crossing, a port of SciPy `find_peaks` + `peak_widths`,
  an Osprey median-CWT consensus detector (with the asymmetric-FWHM boundary cap), and two
  Skyline modes: "boundaries only" (Skyline boundaries with any area method) and "exact area"
  (Skyline boundaries plus Skyline's own area calculation; area options are greyed out).
- **Osprey CWT (improved) integration model:** keeps the interference-robust median-CWT consensus
  apex but places the integration boundaries where the peak returns to a fixed fraction of its apex
  height (mapped from the configurable Boundary width x SD knob), as exact fractional retention times
  rather than snapped to a sample. The placement is **tail-aware**: each edge is set per side from a
  fitted EMG shape, so a tailed peak gets an asymmetric boundary whose trailing edge follows the tail
  (a symmetric boundary would chop it and under-report by ~10%), while a Gaussian peak reduces exactly
  to apex +/- k*sigma. Combined with fractional-trapezoid edge estimation this gives an area deviation
  that is flat across sampling density (~-2% with a linear baseline, ~0% with no background) instead of
  drifting as the boundaries snap to coarser samples. This is the integration model intended for future
  use in Skyline and Osprey. See `docs/improved-integration-model.md`.
- **Byte-identical Skyline replication:** Skyline's actual managed peak finder and area math are
  vendored from ProteoWizard/pwiz (Apache-2.0), so boundaries and area match Skyline exactly for
  the same chromatogram. Multi-transition uses Skyline's "Integrate all" (one shared boundary
  from the summed transitions). See `docs/skyline-replication.md`.
- **Developer docs:** guides for adding new detector / integrator models and for how the Skyline
  and Osprey methods are replicated (`docs/`).
- **Pluggable integration:** trapezoid with optional fractional-trapezoid edge estimation,
  Riemann sum, Gaussian fit, EMG fit, and Skyline (area minus straight-line background). The
  integrated region and its plot fill always reach the detected boundaries.
- **Consensus EMG integration (interference-robust):** for multi-transition data, fits one EMG shape
  to the per-point median across transitions and recovers each transition's area as a robust (median)
  amplitude of that shared shape, so a co-eluting interference on a minority of transitions is
  rejected and the area under it is reconstructed from the clean shape. Near-zero deviation with one
  or two of six transitions interfered, where independent per-transition fitting inflates by tens of
  percent. See `docs/consensus-emg-integration.md`.
- **Per-transition interference and background:** the multi-transition simulator can place the
  configured interference on the first N transitions and give each transition its own background
  level, so transition-specific contamination and baselines can be modeled and quantified. Each
  transition's background is subtracted on its own trace.
- **Background subtraction:** none, constant, and linear baseline.
- **Multi-transition modeling with Koina:** fetch top-N predicted fragment intensities for a
  peptide (Prosit_2020_intensity_HCD) and model several transitions sharing one elution profile,
  quantified with Osprey median-CWT consensus; transitions are overlaid per-color in the plots.
  Falls back to a synthetic fragment ladder when offline. Multi-transition is on by default and
  ships with built-in fragments for ELVISLIVESR (2+, NCE 25), so it works offline at startup;
  Fetch refreshes from Koina.
- **Monte-Carlo sweep** over points-across-peak reporting mean deviation and variance, with the
  too-few / borderline / enough regions and the ~10-point marker.
- **Cross-platform desktop UI** (Avalonia + ScottPlot) with a live preview that updates as
  parameters change; window opens centered on the primary monitor.
- **Distribution:** self-contained portable builds for Linux (x64), Windows (x64 and ARM64), and
  macOS (Intel and Apple Silicon), plus Windows installers (x64 and ARM64) and a Linux `.deb`
  package, produced by CI.

## Bug Fixes

<!-- none yet -->

## Performance

<!-- none yet -->

## Breaking Changes

<!-- none yet -->
