# Guarded NuGet Publish Design

**Status:** Approved for implementation on 2026-06-10.

## Goal

Add the real NuGet publish path while keeping publishing narrow, explicit, and
safe. The workflow should reuse the existing release dry-run before pushing any
package to nuget.org.

## Approach

Add `.github/workflows/publish-nuget.yml`. It runs on manual dispatch and `v*`
tag pushes. Manual dispatch is dry-run only; the `dotnet nuget push` step runs
only when the event is a tag push. That keeps maintainers able to rehearse the
workflow from GitHub Actions without accidentally publishing.

The workflow uses the same action versions as the dry-run workflow:
`actions/checkout@v6`, `actions/setup-dotnet@v5`, and
`actions/upload-artifact@v7`. It grants only `contents: read` to the
`GITHUB_TOKEN`, because NuGet publishing uses the repository secret
`NUGET_API_KEY` instead of GitHub write permissions.

## Acceptance

- The publish workflow runs restore, build, tests, and `scripts/release-dry-run.sh`
  before publishing.
- Package artifacts are uploaded for review.
- The publish step is gated to `push` events whose ref starts with
  `refs/tags/v`.
- The workflow fails clearly if the `NUGET_API_KEY` secret is missing.
- `dotnet nuget push` uses nuget.org and `--skip-duplicate`.
