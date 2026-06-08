# Vendored ProteoWizard / Skyline source

The files under `pwiz/PeakFinding/` are copied **verbatim** from
[ProteoWizard/pwiz](https://github.com/ProteoWizard/pwiz),
`pwiz_tools/Shared/Common/PeakFinding/` (namespace `pwiz.Common.PeakFinding`). They are
Skyline's current, fully-managed peak finder, which was written to reproduce the legacy native
Crawdad's floating-point behavior. They are licensed under the Apache License 2.0, the same
license as this repository; the original copyright headers are preserved.

We vendor them (rather than reimplement) so that Datum's "Skyline" detector and integrator are
**byte-identical** to Skyline for the same input chromatogram: identical boundaries from the
peak finder and identical area arithmetic (float/double types, summation order, the Gaussian
kernel quirks, wing padding, and the `time_normalized` area scaling).

`pwiz/Collections/CollectionUtil.cs` is a minimal shim providing the single `BinarySearch`
overload the vendored code references (the full pwiz `CollectionUtil` is large and unneeded).

Do not reformat or "clean up" these files; keeping them verbatim is what guarantees the
byte-identical match and makes future updates a simple re-copy. They are excluded from
`dotnet format`.

The exact-area arithmetic and the `time_normalized` scaling that Skyline's `ChromPeak`
constructor applies on top of the peak finder are reproduced in
`../SkylineExactIntegrator.cs` (with code references to `ChromHeaderInfo.cs` /
`PeakIntegrator.cs`), since those classes are entangled with Skyline's cache I/O and are not
vendored wholesale.
