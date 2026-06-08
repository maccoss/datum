# Datum documentation

## Integration models

- [The Osprey CWT (improved) boundary and integration model](improved-integration-model.md) — the
  sampling-independent, fixed `k * sigma` fractional-boundary model intended for Skyline and Osprey.
- [Consensus EMG integration: recovering area under interference](consensus-emg-integration.md) — a
  shared EMG shape across transitions with a robust per-transition amplitude, so interference on a
  minority of transitions does not corrupt the quantified area.

## Extending the algorithms

- [Adding a peak-boundary (detector) model](extending-detectors.md)
- [Adding a peak-area (integrator) model](extending-integrators.md)

## How reference methods are replicated

- [Replicating Skyline boundaries and area exactly (byte-identical)](skyline-replication.md)
- [Replicating the Osprey CWT median peak-boundary detection](osprey-cwt-replication.md)

## Other

- [Release notes process](../release-notes/README.md)
- `images/` — screenshots used by the top-level [README](../README.md).
