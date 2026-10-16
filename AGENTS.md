# Release versioning

All packages share `VersionPrefix` and `VersionSuffix` in `Directory.Build.props`.
The .NET SDK combines these into the package version. Local builds default to
`1.1.0-rc.1` through `PrereleaseNumber=1`.

The Publish workflow passes its GitHub Actions `github.run_number` as
`PrereleaseNumber`, producing `1.1.0-rc.<run_number>` without manual version bumps.
This counter belongs to the Publish workflow, increases for each new run, and
does not reset when `VersionPrefix` changes. Gaps are expected when a run fails.
Rerunning a workflow keeps the same number so interrupted publishes can retry
using the existing duplicate-package and release checks. To publish changed code,
start a new Publish run rather than rerunning an old one.

Use dot-separated numeric identifiers without leading zeros: `rc.9 < rc.10 <
rc.11`. Do not use `rc.01` or concatenate the counter as `rc10`, which would sort
lexically. Keep release guidance here rather than in README.md.

For a stable release, remove `VersionSuffix` from `Directory.Build.props` and set
the desired `VersionPrefix`. The unused `PrereleaseNumber` then has no effect.
The publish workflow detects prereleases from the built package version and
skips public API baseline promotion for them.
