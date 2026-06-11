# Guarded NuGet Publish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a guarded NuGet publish workflow that can rehearse manually and publish only on `v*` tag pushes.

**Architecture:** Keep publishing in GitHub Actions, not local scripts. The workflow reuses `scripts/release-dry-run.sh` for package validation and only runs `dotnet nuget push` after tests and dry-run complete.

**Tech Stack:** GitHub Actions YAML, .NET CLI, xUnit file-contract tests.

---

### Task 1: Contract Tests

**Files:**
- Modify: `tests/Runner.Tests/ReleaseDryRunScriptTests.cs`
- Create: `tests/Runner.Tests/NuGetPublishWorkflowTests.cs`

- [ ] Update the dry-run workflow test to require `actions/upload-artifact@v7`.
- [ ] Add publish workflow tests that require tag push triggers, least-privilege permissions, action versions, release dry-run before push, artifact upload, `NUGET_API_KEY`, nuget.org source, and `--skip-duplicate`.
- [ ] Run the focused tests and confirm they fail before workflow changes.

### Task 2: Workflow Implementation

**Files:**
- Modify: `.github/workflows/release-dry-run.yml`
- Create: `.github/workflows/publish-nuget.yml`

- [ ] Update release dry-run artifact upload to `actions/upload-artifact@v7`.
- [ ] Add the publish workflow with `workflow_dispatch` and `push.tags: [ 'v*' ]`.
- [ ] Run restore, build, tests, `scripts/release-dry-run.sh`, and artifact upload.
- [ ] Gate `dotnet nuget push` with `github.event_name == 'push' && startsWith(github.ref, 'refs/tags/v')`.
- [ ] Fail clearly if `NUGET_API_KEY` is empty.

### Task 3: Documentation

**Files:**
- Modify: `README.md`
- Modify: `docs/developer-setup.md`
- Modify: `docs/wiki/index.md`
- Modify: `docs/roadmap.md`

- [ ] Document that manual publish workflow dispatch is rehearsal only.
- [ ] Document that actual publish requires a `v*` tag push and `NUGET_API_KEY`.
- [ ] Mark guarded publish workflow as present while leaving real 0.1.0 publication pending until a maintainer creates the secret and tag.

### Task 4: Verification

- [ ] Run focused workflow contract tests.
- [ ] Run `./scripts/release-dry-run.sh`.
- [ ] Run `dotnet test sdv-test-framework.slnx --no-restore --nologo`.
- [ ] Commit the feature branch.
