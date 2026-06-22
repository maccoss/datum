# Datum v26.2.0 Release Notes

Feature release: the sampled-peak plot now draws the lines the trapezoid integrates between the
sampled points, with display toggles that make the effect of sampling density obvious.

## New Features

- **Sampled-peak plot — lines between the points.** The bottom-left "Sampled" / "Sampled
  transitions" plot now draws solid lines connecting the sampled points (the piecewise-linear trace
  the trapezoid actually integrates), so coarse sampling is directly visible as a coarse polyline.
  In multi-transition mode each transition gets its own colored line. The faint full-resolution
  noisy/background trace behind the samples was lightened so the sampling is the dominant element.
- **Display toggles above the sampled plot.** Two checkboxes control the view:
  - **Noisy trace** — show or hide the faint full-resolution noisy overlay.
  - **Sample points** — show or hide the sampled-point markers; unchecking leaves only the lines
    between the points.

## Also included (rolled up from 26.1.1)

- **Dark-theme display fix:** the app now forces the Light theme variant, so the sweep-results table
  and info panels render correctly under a dark system theme (previously white text on the app's
  light backgrounds, e.g. on Windows dark mode).
- **Windows ARM64 installer fix:** corrected the Inno Setup architecture identifier
  (`arm64compatible` -> `arm64`) so the `win-arm64` release artifacts build.
- **GitHub Actions updated** to their Node 24 major versions (`checkout` v6, `setup-dotnet` v5,
  `upload-artifact` v7, `download-artifact` v8, `action-gh-release` v3), clearing the Node 20
  deprecation warnings.
