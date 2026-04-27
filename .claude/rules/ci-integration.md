# CI Integration

Two target environments: GitHub Actions (default, cloud) and self-hosted Proxmox VM (better determinism via GPU passthrough).

## GitHub Actions

Template at `.github/workflows/test.yml`. Key structure:

```yaml
jobs:
  test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          lfs: true  # fixtures live in LFS
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0'
      - name: Cache SDV install
        uses: actions/cache@v4
        with:
          path: ~/.cache/sdv-test-env
          key: sdv-${{ hashFiles('.sdv-version') }}-smapi-${{ hashFiles('.smapi-version') }}
      - name: Setup SDV + SMAPI + Xvfb
        uses: ./actions/setup-sdv-test-env  # composite action in this repo
        with:
          sdv-version: '1.6.15'
          smapi-version: '4.1.10'
      - name: Build
        run: dotnet build --configuration Release
      - name: Run tests
        run: ./scripts/ci.sh
      - name: Upload diffs on failure
        if: failure()
        uses: actions/upload-artifact@v4
        with:
          name: test-diffs
          path: tests/diffs/
          retention-days: 7
```

## Xvfb setup

SDV needs a display even for draw-call tests (draws to a RenderTarget that requires a graphics device). On headless CI:

```bash
Xvfb :99 -screen 0 1280x720x24 &
export DISPLAY=:99
# Mesa software rendering
export LIBGL_ALWAYS_SOFTWARE=1
```

Mesa llvmpipe is deterministic across runs on the same hardware, which is what we need. It is NOT bit-identical across different CI runner hardware, so:

- Draw-call assertions: safe across any CI runner
- Bitmap assertions: baselines must be regenerated per CI environment, OR use tolerance-based SSIM with a generous threshold (0.98+)

## Self-hosted Proxmox runner

For the author's setup. Key advantages:
- Real GPU via PCIe passthrough → bit-identical bitmap captures across runs
- No concurrent job interference (single-tenant)
- Faster (no cold starts)

Setup documented in `docs/ci-self-hosted.md`. Broad strokes:
- Debian 12 VM with NVIDIA driver matching host
- Docker runner using `act` for local workflow testing
- Runner registered as self-hosted with labels `[self-hosted, linux, gpu]`
- Workflow conditionally targets it:
  ```yaml
  runs-on: ${{ github.repository_owner == 'finn' && 'self-hosted' || 'ubuntu-latest' }}
  ```

## Determinism across environments

Draw-call assertions: 100% stable, any environment.
Bitmap assertions: environment-sensitive. Three tiers of baselines:
1. `baselines/generic/` — pixel tolerance 0.95 SSIM, works anywhere
2. `baselines/ci-ubuntu/` — tolerance 0.98, regenerated per Ubuntu LTS
3. `baselines/self-hosted-nvidia/` — tolerance 0.999, bit-identical expected

Scenarios opt into a baseline tier via metadata.

## CI time budget

Target: full test suite under 5 minutes on GitHub Actions. If we blow past this:
1. Parallelize by test file (one SDV subprocess per file group)
2. Skip bitmap tests in fast-tier CI; run them in a nightly job
3. Reconsider whether some scenarios can be unit-tested without launching SDV

Never disable tests to hit the budget. Make them faster or move them.
