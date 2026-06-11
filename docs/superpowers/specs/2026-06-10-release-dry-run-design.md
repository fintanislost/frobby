# Release Dry-Run Design

**Status:** Approved for implementation on 2026-06-10.

## Goal

Add a release-shaped verification path for Frobby packages without publishing to
NuGet. The dry-run should prove that CI can build, pack, install, scaffold, and
archive the exact artifacts a later tagged release would publish.

## Approach

Add a local `scripts/release-dry-run.sh` wrapper around the existing package
flow. It should call `scripts/package-install-smoke.sh`, validate the expected
`.nupkg` outputs, and write a small manifest under `nupkg/release-dry-run.json`.

Add a GitHub Actions workflow that runs the ordinary CI checks, runs the dry-run
script, and uploads package artifacts. The workflow must not call
`dotnet nuget push` or require a NuGet API key. Real publishing remains a later
guarded tag-only slice.

## Acceptance

- `scripts/release-dry-run.sh` is executable and safe to run locally.
- The dry-run produces exactly the expected package set for the configured
  `SdvTestFrameworkVersion`.
- The dry-run writes a manifest useful for CI artifact review.
- `.github/workflows/release-dry-run.yml` can run on manual dispatch, `main`,
  and `v*` tags without publishing.
- README, developer setup, wiki, and roadmap describe the dry-run-first release
  flow.
