# Release Notes

This directory contains per-version release notes for Datum, following the same convention as
the lab's other tools (skyline-prism, Osprey).

## Versioning scheme

Datum uses a `YY.feature.patch` versioning convention:

- **YY**: two-digit year (e.g. `26` for 2026)
- **feature**: incremented for each release containing new features
- **patch**: incremented for bug-fix-only releases within the same feature version

Examples: `26.1.0` (first feature release of 2026), `26.1.1` (patch), `26.2.0` (second feature
release). Pre-release candidates use an `rc` suffix, e.g. `26.1.0rc1`.

The canonical version lives in `Directory.Build.props` (`<Version>`) and is updated only at
release time, not during development.

## File format

Each release gets one file: `RELEASE_NOTES_v{version}.md`. During development the unreleased
draft lives in `RELEASE_NOTES_next.md` and is renamed at release time.

```text
release-notes/
  README.md                      # this file
  RELEASE_NOTES_next.md          # working draft for the next release
  RELEASE_NOTES_v26.1.0.md
  RELEASE_NOTES_v26.1.1.md
```

There is no single CHANGELOG.md; each version's file persists in the repo.

## Writing release notes

### During development

Maintain `RELEASE_NOTES_next.md` as a working draft for the next planned version. Append entries
as features and fixes land on the development branch. The file is unversioned until release so
the target version can change (a planned patch can become a feature release once new
functionality is added).

### Content structure

```markdown
# Datum v{version} Release Notes

One-sentence summary of the release.

## New Features

- Feature descriptions grouped by area (e.g. Peak models, Detectors, Integrators, UI)
- Focus on what changed from the user's perspective, not implementation details

## Bug Fixes

- Description of the bug and its impact, and what was fixed

## Performance

- Performance improvements with context

## Breaking Changes

- Any changes that require user action (config/format changes, removed options)
- Omit this section if there are none
```

Sections can be omitted if empty. For major releases, subsections within each category are fine;
for patch releases a flat list is sufficient.

### Style

- Write in past tense ("Added", "Fixed", "Removed")
- Lead with user impact, not implementation details
- Include specific numbers where relevant
- Reference options by their UI label or config key, and modified files by path

## Release process

1. Finalize `RELEASE_NOTES_next.md` on the development branch.
2. Rename it: `git mv release-notes/RELEASE_NOTES_next.md release-notes/RELEASE_NOTES_v{version}.md`
3. Update the title heading inside the file to match the version.
4. Create a fresh empty `RELEASE_NOTES_next.md` for the following release.
5. Update `<Version>` in `Directory.Build.props` to match the release.
6. Commit the version bump and renames.
7. Merge to `main`.
8. Tag: `git tag v{version}`
9. Push: `git push origin main --tags`
10. CI (`.github/workflows/release.yml`) builds the per-OS artifacts, verifies this file exists,
    and creates the GitHub Release using it as the release body.
