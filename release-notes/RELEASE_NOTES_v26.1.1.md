# Datum v26.1.1 Release Notes

Patch release: fixes an unreadable (white-on-white) results table under a dark system theme and
repairs the Windows ARM64 installer build so the release artifacts complete.

## Bug Fixes

- **Dark-theme display:** on Windows (or any OS) set to a dark system theme, the sweep-results
  table and the area/info panels rendered as near-white text on the app's light backgrounds, making
  them unreadable. The app now forces the Light theme variant, which its light-background design
  assumes, so the UI renders correctly regardless of the system theme (`src/Datum.App/App.axaml`).
- **Windows ARM64 installer:** the `win-arm64` release job failed because the Inno Setup script was
  given the architecture identifier `arm64compatible`, which is not a valid Inno Setup identifier
  (the correct one is `arm64`; `x64compatible` is valid, which is why `win-x64` succeeded). Corrected
  the identifier so the ARM64 installer builds (`.github/workflows/release.yml`,
  `packaging/windows/datum.iss`).

## Build

- Updated the GitHub Actions to their current Node 24 major versions (`checkout` v6,
  `setup-dotnet` v5, `upload-artifact` v7, `download-artifact` v8, `action-gh-release` v3) in both
  `ci.yml` and `release.yml`, clearing the Node 20 deprecation warnings.
