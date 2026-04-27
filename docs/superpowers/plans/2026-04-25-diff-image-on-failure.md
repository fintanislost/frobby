# Diff-Image-on-Failure — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **No git repo.** Task completion gate is **`./scripts/ci.sh` green** at the per-task expected count. T6's extra gates:
> - `--diff-format=triptych` produces a 4th composite PNG when a bitmap assertion fails.
> - HTML per-scenario report shows a `<section class="forensics">` with baseline/capture/diff thumbnails.
> - `--update-baselines` writes the baseline as before and produces no diffs (verified via unit test, not live smoke).

**Goal:** On `bitmap` assertion failure, write per-failure forensics PNGs (`baseline.png`, `capture.png`, `diff.png`) into the per-run report dir and surface them in the HTML report. Optional `triptych.png` composite for one-glance review.

**Architecture:** Pure-function diff renderer (`DiffImageRenderer`) takes baseline+capture bytes plus `SsimResult` (extended to carry the per-block score grid) and writes PNGs. `BitmapAssertion` calls it on failure unless `--update-baselines`. `ScenarioRunner` collects DiffSets per failed bitmap assertion. `HtmlReportGenerator` renders a forensics section. Resolution: per-assertion `diff_format` > run-wide CLI/MCP flag > default `Files`.

**Tech Stack:**
- ImageSharp 3.1.12 (already a Runner dep from M2-bitmap). No new NuGets.
- `System.Text.Json` for `summary.json` shape extension.

**Design spec:** `docs/superpowers/specs/2026-04-25-diff-image-on-failure-design.md`

---

## File structure

**New (Runner):**
- `src/Runner/Bitmap/SsimResult.cs` — record struct carrying composite score + per-block grid + dims.
- `src/Runner/Bitmap/DiffSet.cs` — record carrying the four written paths (baseline/capture/diff + nullable triptych).
- `src/Runner/Bitmap/DiffFormat.cs` — enum: `Files | Triptych | All`.
- `src/Runner/Bitmap/DiffImageRenderer.cs` — pure-function renderer.

**New (Protocol):**
- `src/Protocol/Reports/DiffSet.cs` — Wait, no: keep `DiffSet` in Runner; only the wire shape on `ScenarioOutcome.Diffs` needs to cross. Spec §4.6 says `ScenarioOutcome` extends to `Diffs: IReadOnlyList<DiffSet>`. Simplest: move `DiffSet` to Protocol.Reports alongside the other shared types, namespace `SdvTestFramework.Protocol.Reports`. `DiffImageRenderer` (in Runner) imports it. Yes — this matches the HTML-reports T5 fixup precedent (pure types live in Protocol).

So restated:

**New (Runner):**
- `src/Runner/Bitmap/SsimResult.cs`
- `src/Runner/Bitmap/DiffFormat.cs`
- `src/Runner/Bitmap/DiffImageRenderer.cs`

**New (Protocol):**
- `src/Protocol/Reports/DiffSet.cs` — record carrying the four written paths. Cross-project shared type.

**New tests:**
- `tests/Runner.Tests/Bitmap/SsimResultTests.cs` — 1 test.
- `tests/Runner.Tests/Bitmap/DiffImageRendererTests.cs` — 4 tests.
- `tests/Runner.Tests/Bitmap/BitmapAssertionDiffTests.cs` — 3 tests.
- `tests/Runner.Tests/Reports/HtmlReportGeneratorForensicsTests.cs` — 2 tests.
- `tests/Runner.Mcp.Tests/Tools/RunScenarioDiffFormatTests.cs` — 1 test.
- `tests/Runner.Tests/Bitmap/DiffImageIntegrationTests.cs` — 1 skipped placeholder.

**Modified:**
- `src/Runner/Bitmap/SsimDiff.cs` — `Compute` returns `SsimResult` instead of `float`.
- `src/Runner/Bitmap/BitmapAssertion.cs` — accepts diff-generation params; emits `DiffSet` on failure.
- `src/Protocol/Models/ScenarioAssertion.cs` — adds `DiffFormat` nullable field.
- `src/Protocol/Reports/RunSummary.cs` — `ScenarioOutcome` adds `Diffs: IReadOnlyList<DiffSet>`.
- `src/Runner/Scenarios/ScenarioReport.cs` — adds `Diffs: List<DiffSet>` + assertion-index tracking.
- `src/Runner/Scenarios/ScenarioRunner.cs` — passes diff-format + run-dir + assertion-index to bitmap evaluator; appends to report.Diffs.
- `src/Runner/Reports/HtmlReportGenerator.cs` — new forensics section + CSS.
- `src/Runner/Commands/RunCommand.cs` — `--diff-format` flag, plumb to ScenarioRunner; populate `ScenarioOutcome.Diffs`.
- `src/Runner.Mcp/Tools/RunScenarioTool.cs` — `diff_format` arg.
- `schemas/scenario.schema.json` — bitmap assertion gains optional `diff_format` enum.
- `tests/Runner.Tests/Bitmap/SsimDiffTests.cs` + `BitmapAssertionTests.cs` — read `.Score` from new return type.

**Starting test count:** 357 Passed + 46 Skipped.
**Target:** 368 Passed + 47 Skipped (+11 passing, +1 skipped).

---

## Task 1: SsimResult extraction

**Why:** Extend `SsimDiff` to surface the per-block score grid alongside the composite. The grid feeds the heatmap renderer in T2. Pure refactor — no behavior change to the composite score.

**Files:**
- Create: `src/Runner/Bitmap/SsimResult.cs`
- Modify: `src/Runner/Bitmap/SsimDiff.cs`
- Modify: `src/Runner/Bitmap/BitmapAssertion.cs` (read `.Score`)
- Modify: `tests/Runner.Tests/Bitmap/SsimDiffTests.cs` (read `.Score`)
- Create: `tests/Runner.Tests/Bitmap/SsimResultTests.cs`

### Step 1: Failing test

- [ ] Create `tests/Runner.Tests/Bitmap/SsimResultTests.cs`:

```csharp
using SdvTestFramework.Runner.Bitmap;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

public class SsimResultTests
{
    [Fact]
    public void Score_EqualsAverageOfBlockScores()
    {
        // 2×2 block grid = 4 blocks. Avg of 0.8, 0.9, 1.0, 1.0 = 0.925.
        var grid = new float[2, 2] { { 0.8f, 0.9f }, { 1.0f, 1.0f } };
        var result = new SsimResult(0.925f, grid, BlocksX: 2, BlocksY: 2);
        Assert.Equal(0.925f, result.Score, precision: 4);
        Assert.Equal(2, result.BlocksX);
        Assert.Equal(2, result.BlocksY);
        Assert.Equal(0.8f, result.BlockScores[0, 0]);
        Assert.Equal(1.0f, result.BlockScores[1, 1]);
    }
}
```

### Step 2: Run test to verify it fails

Run: `dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~SsimResultTests" --nologo`
Expected: build fails — `SsimResult` does not exist.

### Step 3: Create SsimResult.cs

- [ ] Create `src/Runner/Bitmap/SsimResult.cs`:

```csharp
namespace SdvTestFramework.Runner.Bitmap;

/// <summary>
/// SSIM computation result. <see cref="Score"/> is the mean of <see cref="BlockScores"/>.
/// The grid is row-major: <c>BlockScores[by, bx]</c> where 0 ≤ by &lt; <see cref="BlocksY"/>,
/// 0 ≤ bx &lt; <see cref="BlocksX"/>. Block size is fixed at 8×8 in <see cref="SsimDiff"/>.
/// </summary>
public readonly record struct SsimResult(
    float Score,
    float[,] BlockScores,
    int BlocksX,
    int BlocksY);
```

### Step 4: Update SsimDiff.Compute return type

- [ ] Modify `src/Runner/Bitmap/SsimDiff.cs`. Change the `Compute` method body to populate + return an `SsimResult`:

```csharp
public static SsimResult Compute(Image<Rgba32> a, Image<Rgba32> b)
{
    if (a.Width != b.Width || a.Height != b.Height)
        throw new ArgumentException(
            $"SSIM dim mismatch: {a.Width}×{a.Height} vs {b.Width}×{b.Height}");

    int w = a.Width, h = a.Height;
    int blocksX = w / Block;
    int blocksY = h / Block;
    if (blocksX == 0 || blocksY == 0)
        throw new ArgumentException(
            $"SSIM requires at least one 8×8 block (got {w}×{h})");

    var grid = new float[blocksY, blocksX];
    double sum = 0;
    for (int by = 0; by < blocksY; by++)
    for (int bx = 0; bx < blocksX; bx++)
    {
        var s = (float)BlockSsim(a, b, bx * Block, by * Block);
        grid[by, bx] = s;
        sum += s;
    }

    return new SsimResult(
        Score: (float)(sum / (blocksX * blocksY)),
        BlockScores: grid,
        BlocksX: blocksX,
        BlocksY: blocksY);
}
```

### Step 5: Update existing SsimDiff callers

- [ ] Modify `src/Runner/Bitmap/BitmapAssertion.cs:95-100`. Replace `float score;` and the assignment with reading `.Score` from the result:

```csharp
SsimResult ssim;
try
{
    using var baseline = Image.Load<Rgba32>(baselinePath);
    using var capture = Image.Load<Rgba32>(capturePath);
    ssim = SsimDiff.Compute(baseline, capture);
}
catch (ArgumentException ex)
{
    return new BitmapAssertionResult(false, ex.Message + " — regenerate baseline with --update-baselines");
}
catch (Exception ex)
{
    return new BitmapAssertionResult(false, $"SSIM failed: {ex.Message}");
}
```

Then change the tolerance check (currently uses `score`):

```csharp
var tolerance = a.Tolerance;
if (tolerance <= 0 || tolerance > 1)
    return new BitmapAssertionResult(false,
        $"bitmap assertion 'tolerance' must be in (0, 1]; got {tolerance}");
if (ssim.Score + 1e-9 < tolerance)
    return new BitmapAssertionResult(false,
        $"SSIM {ssim.Score:F4} < tolerance {tolerance:F4}; capture: {capturePath}");
```

### Step 6: Update existing SsimDiff tests

- [ ] Modify `tests/Runner.Tests/Bitmap/SsimDiffTests.cs`. Three tests reference `var score = SsimDiff.Compute(...)`. Change each to:

```csharp
var result = SsimDiff.Compute(a, b);
Assert.InRange(result.Score, 0.999, 1.0 + 1e-6);
// (or 0.95–1.0 for the perturbed test)
```

For the `DifferentDimensions_Throws` test, no signature change — it asserts on the exception. No edits needed.

### Step 7: Verify

Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && ./scripts/ci.sh 2>&1 | grep -E "Passed:|Skipped:" | head -10`
Expected: **358 Passed + 46 Skipped** (was 357+46; +1 from the new SsimResultTests).

---

## Task 2: DiffImageRenderer

**Why:** Pure-function renderer — takes baseline + capture bytes + `SsimResult` + tolerance, writes PNGs to a directory. No external state, fully unit-testable. The bilinear-smoothed heatmap is the visual artifact.

**Files:**
- Create: `src/Protocol/Reports/DiffSet.cs` (cross-project type per spec §4.6)
- Create: `src/Runner/Bitmap/DiffFormat.cs`
- Create: `src/Runner/Bitmap/DiffImageRenderer.cs`
- Create: `tests/Runner.Tests/Bitmap/DiffImageRendererTests.cs`

### Step 1: Failing tests

- [ ] Create `tests/Runner.Tests/Bitmap/DiffImageRendererTests.cs`:

```csharp
using System;
using System.IO;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Runner.Bitmap;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

public class DiffImageRendererTests
{
    // 64×64 deterministic gradient. Same shape as SsimDiffTests for consistency.
    private static byte[] GradientPng(int seed = 0)
    {
        using var img = new Image<Rgba32>(64, 64);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
        {
            byte r = (byte)((x * 4 + seed) % 256);
            byte g = (byte)((y * 4 + seed) % 256);
            byte b = (byte)(((x + y) * 2 + seed) % 256);
            img[x, y] = new Rgba32(r, g, b, 255);
        }
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static SsimResult MakeSsim(float[,] blockScores)
    {
        int by = blockScores.GetLength(0);
        int bx = blockScores.GetLength(1);
        double sum = 0;
        for (int j = 0; j < by; j++)
            for (int i = 0; i < bx; i++)
                sum += blockScores[j, i];
        return new SsimResult((float)(sum / (by * bx)), blockScores, bx, by);
    }

    [Fact]
    public void IdenticalImages_DiffPngHasNoRedTint()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"diff-id-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var bytes = GradientPng();
            // 8×8 block grid = 64 blocks. All blocks score 1.0.
            var grid = new float[8, 8];
            for (int j = 0; j < 8; j++)
                for (int i = 0; i < 8; i++)
                    grid[j, i] = 1.0f;
            var ssim = MakeSsim(grid);

            var set = DiffImageRenderer.Render(bytes, bytes, ssim, tolerance: 0.95f, DiffFormat.Files, tmp);

            Assert.True(File.Exists(set.Baseline));
            Assert.True(File.Exists(set.Capture));
            Assert.True(File.Exists(set.Diff));
            Assert.Null(set.Triptych);

            // Diff PNG should be visually identical to baseline (no red tint applied
            // because all blocks scored above tolerance). Sample a few pixels.
            using var diff = Image.Load<Rgba32>(set.Diff);
            using var baseline = Image.Load<Rgba32>(bytes);
            for (int i = 0; i < 10; i++)
            {
                int x = (i * 7) % 64;
                int y = (i * 11) % 64;
                var dp = diff[x, y];
                var bp = baseline[x, y];
                Assert.InRange((int)dp.R, bp.R - 2, bp.R + 2);
                Assert.InRange((int)dp.G, bp.G - 2, bp.G + 2);
                Assert.InRange((int)dp.B, bp.B - 2, bp.B + 2);
            }
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public void DifferingImages_DiffPngHasRedRegionsAtFailingBlocks()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"diff-fail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var baselineBytes = GradientPng();
            var captureBytes = GradientPng(seed: 50);
            // Mark the top-left 3×3 region of blocks as failing (score 0.5),
            // rest pass (1.0). Tolerance 0.95 → only the top-left corner gets red tint.
            var grid = new float[8, 8];
            for (int j = 0; j < 8; j++)
                for (int i = 0; i < 8; i++)
                    grid[j, i] = (j < 3 && i < 3) ? 0.5f : 1.0f;
            var ssim = MakeSsim(grid);

            var set = DiffImageRenderer.Render(baselineBytes, captureBytes, ssim, tolerance: 0.95f, DiffFormat.Files, tmp);

            using var diff = Image.Load<Rgba32>(set.Diff);
            // Sample center of failing block (4, 4): should be visibly red-shifted.
            // R should be elevated relative to G/B.
            var pp = diff[4, 4];
            Assert.True(pp.R > pp.G + 20, $"expected red dominance at failing block, got R={pp.R} G={pp.G} B={pp.B}");
            Assert.True(pp.R > pp.B + 20, $"expected red dominance at failing block, got R={pp.R} G={pp.G} B={pp.B}");

            // Sample a passing block (e.g., (40, 40)): should NOT have heavy red tint.
            var pq = diff[40, 40];
            Assert.True(pq.R - System.Math.Max(pq.G, pq.B) < 30, $"unexpected red tint at passing block: R={pq.R} G={pq.G} B={pq.B}");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public void Triptych_ProducesFourthFile_3xWidth()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"diff-tri-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var bytes = GradientPng();
            var grid = new float[8, 8];
            for (int j = 0; j < 8; j++)
                for (int i = 0; i < 8; i++)
                    grid[j, i] = 1.0f;
            var ssim = MakeSsim(grid);

            var set = DiffImageRenderer.Render(bytes, bytes, ssim, tolerance: 0.95f, DiffFormat.Triptych, tmp);

            Assert.NotNull(set.Triptych);
            Assert.True(File.Exists(set.Triptych));
            using var img = Image.Load<Rgba32>(set.Triptych!);
            Assert.Equal(64 * 3, img.Width);
            Assert.Equal(64, img.Height);
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public void BilinearSmoothing_NoHardBlockBoundaries()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"diff-smooth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var bytes = GradientPng();
            // Sharp gradient: leftmost block fails (0.0), all others pass (1.0).
            var grid = new float[8, 8];
            for (int j = 0; j < 8; j++)
                for (int i = 0; i < 8; i++)
                    grid[j, i] = i == 0 ? 0.0f : 1.0f;
            var ssim = MakeSsim(grid);

            var set = DiffImageRenderer.Render(bytes, bytes, ssim, tolerance: 0.95f, DiffFormat.Files, tmp);
            using var diff = Image.Load<Rgba32>(set.Diff);
            using var baseline = Image.Load<Rgba32>(bytes);

            // Pixel just inside the failing block (x=7, mid-row): heavily red.
            // Pixel just outside (x=8): with bilinear smoothing, redness should taper.
            // Pixel further out (x=12, midway to the next block center at x=12): less red.
            int redAt7 = diff[7, 32].R - baseline[7, 32].R;
            int redAt9 = diff[9, 32].R - baseline[9, 32].R;
            int redAt15 = diff[15, 32].R - baseline[15, 32].R;
            // Strict block-tinting (no bilinear) would make redAt7 high and redAt9 ≈ 0.
            // With bilinear smoothing, redAt9 is between redAt7 and redAt15 (continuous gradient).
            Assert.True(redAt7 > redAt9, $"expected redAt7({redAt7}) > redAt9({redAt9})");
            Assert.True(redAt9 > redAt15, $"expected redAt9({redAt9}) > redAt15({redAt15})");
            // And redAt9 must be > 0 — block-strict tinting would zero this out at the boundary.
            Assert.True(redAt9 > 5, $"expected smoothed tint at block boundary, got redAt9={redAt9}");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }
}
```

### Step 2: Run tests to verify they fail

Run: `dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~DiffImageRenderer" --nologo`
Expected: build fails — `DiffImageRenderer`, `DiffFormat`, `DiffSet` don't exist.

### Step 3: Create DiffSet (Protocol)

- [ ] Create `src/Protocol/Reports/DiffSet.cs`:

```csharp
namespace SdvTestFramework.Protocol.Reports;

/// <summary>
/// File paths produced by a single bitmap-assertion failure. Paths are absolute on
/// generation but relative to the run directory when serialized in <c>summary.json</c>.
/// </summary>
public sealed record DiffSet(
    string Baseline,
    string Capture,
    string Diff,
    string? Triptych);
```

### Step 4: Create DiffFormat enum

- [ ] Create `src/Runner/Bitmap/DiffFormat.cs`:

```csharp
using System.Text.Json.Serialization;

namespace SdvTestFramework.Runner.Bitmap;

/// <summary>
/// Diff artifact set produced when a bitmap assertion fails.
/// <list type="bullet">
///   <item><see cref="Files"/> — write only the 3 separate PNGs (baseline, capture, diff).</item>
///   <item><see cref="Triptych"/> — also write a horizontal stitch composite.</item>
///   <item><see cref="All"/> — same as <see cref="Triptych"/> today; reserved for future composites.</item>
/// </list>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DiffFormat>))]
public enum DiffFormat
{
    Files,
    Triptych,
    All,
}
```

### Step 5: Create DiffImageRenderer

- [ ] Create `src/Runner/Bitmap/DiffImageRenderer.cs`:

```csharp
using System;
using System.IO;
using SdvTestFramework.Protocol.Reports;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SdvTestFramework.Runner.Bitmap;

/// <summary>
/// Pure-function renderer producing forensics PNGs for a failed bitmap assertion.
/// Outputs (always): <c>baseline.png</c>, <c>capture.png</c>, <c>diff.png</c>.
/// Diff is the baseline image with a bilinear-smoothed red heatmap overlaid; redness per
/// pixel scales with how far the sampled block's SSIM falls below tolerance. Optional
/// composite output: <c>triptych.png</c> (3-wide horizontal stitch).
/// </summary>
public static class DiffImageRenderer
{
    private const int Block = 8;

    /// <summary>
    /// Render the diff set into <paramref name="outputDir"/>. Caller is responsible for
    /// pre-creating the directory. Returns absolute paths to written files.
    /// </summary>
    public static DiffSet Render(
        byte[] baselineBytes,
        byte[] captureBytes,
        SsimResult ssim,
        float tolerance,
        DiffFormat format,
        string outputDir)
    {
        // 1. Byte-for-byte copies of inputs — no re-encoding (preserves source fidelity).
        var baselinePath = Path.Combine(outputDir, "baseline.png");
        var capturePath = Path.Combine(outputDir, "capture.png");
        var diffPath = Path.Combine(outputDir, "diff.png");
        File.WriteAllBytes(baselinePath, baselineBytes);
        File.WriteAllBytes(capturePath, captureBytes);

        // 2. Decode baseline, build heatmap, encode diff.
        using var baseline = Image.Load<Rgba32>(baselineBytes);
        var pixelRedness = BuildPixelRedness(ssim, tolerance, baseline.Width, baseline.Height);

        using (var diff = baseline.Clone())
        {
            ApplyHeatmap(diff, pixelRedness);
            diff.SaveAsPng(diffPath);
        }

        // 3. Composite output if requested.
        string? triptychPath = null;
        if (format is DiffFormat.Triptych or DiffFormat.All)
        {
            triptychPath = Path.Combine(outputDir, "triptych.png");
            BuildTriptych(baselineBytes, captureBytes, diffPath, triptychPath);
        }

        return new DiffSet(baselinePath, capturePath, diffPath, triptychPath);
    }

    /// <summary>
    /// Compute per-pixel redness in [0, 1] via bilinear interpolation of per-block scores.
    /// Block centers are at <c>(bx*8 + 4, by*8 + 4)</c>. Edge pixels clamp to boundary
    /// blocks so the 4-neighbour interpolation always has 4 valid samples.
    /// </summary>
    private static float[,] BuildPixelRedness(SsimResult ssim, float tolerance, int width, int height)
    {
        var blocks = ssim.BlockScores;
        int bx = ssim.BlocksX;
        int by = ssim.BlocksY;

        // Per-block redness: 0 if score >= tolerance, else proportional severity.
        var blockRedness = new float[by, bx];
        for (int j = 0; j < by; j++)
            for (int i = 0; i < bx; i++)
            {
                float s = blocks[j, i];
                blockRedness[j, i] = s >= tolerance ? 0f : Math.Clamp((tolerance - s) / tolerance, 0f, 1f);
            }

        var pixelRedness = new float[height, width];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            // Find the block whose center is just to the upper-left of (x, y).
            // Block center for (bi, bj) is at ((bi*8 + 4), (bj*8 + 4)).
            float fx = (x - 4) / (float)Block;   // can go negative for x < 4
            float fy = (y - 4) / (float)Block;
            int bi = (int)Math.Floor(fx);
            int bj = (int)Math.Floor(fy);
            float u = fx - bi;
            float v = fy - bj;

            // Clamp the 4 neighbour indices to valid block range.
            int bi0 = Math.Clamp(bi, 0, bx - 1);
            int bi1 = Math.Clamp(bi + 1, 0, bx - 1);
            int bj0 = Math.Clamp(bj, 0, by - 1);
            int bj1 = Math.Clamp(bj + 1, 0, by - 1);

            // Bilinear: weighted sum of 4 corners.
            float r00 = blockRedness[bj0, bi0];
            float r10 = blockRedness[bj0, bi1];
            float r01 = blockRedness[bj1, bi0];
            float r11 = blockRedness[bj1, bi1];
            float top = r00 * (1 - u) + r10 * u;
            float bot = r01 * (1 - u) + r11 * u;
            pixelRedness[y, x] = top * (1 - v) + bot * v;
        }
        return pixelRedness;
    }

    /// <summary>
    /// Apply the heatmap to the image in place. Per pixel:
    /// <c>R' = lerp(R, 255, redness*0.6); G' = G * (1 - redness*0.4); B' = B * (1 - redness*0.4)</c>.
    /// Keeps underlying detail visible while making hot regions obvious.
    /// </summary>
    private static void ApplyHeatmap(Image<Rgba32> img, float[,] pixelRedness)
    {
        int w = img.Width, h = img.Height;
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < w; x++)
                {
                    float r = pixelRedness[y, x];
                    if (r <= 0) continue;
                    var p = row[x];
                    int newR = (int)(p.R + (255 - p.R) * (r * 0.6f));
                    int newG = (int)(p.G * (1f - r * 0.4f));
                    int newB = (int)(p.B * (1f - r * 0.4f));
                    row[x] = new Rgba32(
                        (byte)Math.Clamp(newR, 0, 255),
                        (byte)Math.Clamp(newG, 0, 255),
                        (byte)Math.Clamp(newB, 0, 255),
                        p.A);
                }
            }
        });
    }

    /// <summary>
    /// Build a 3-wide horizontal triptych from baseline | capture | diff. All three are
    /// expected to share dimensions — the renderer guarantees this by construction.
    /// </summary>
    private static void BuildTriptych(byte[] baselineBytes, byte[] captureBytes, string diffPath, string outputPath)
    {
        using var baseline = Image.Load<Rgba32>(baselineBytes);
        using var capture = Image.Load<Rgba32>(captureBytes);
        using var diff = Image.Load<Rgba32>(diffPath);
        int w = baseline.Width, h = baseline.Height;
        using var composite = new Image<Rgba32>(w * 3, h);
        composite.Mutate(ctx =>
        {
            ctx.DrawImage(baseline, new Point(0, 0), 1f);
            ctx.DrawImage(capture, new Point(w, 0), 1f);
            ctx.DrawImage(diff, new Point(w * 2, 0), 1f);
        });
        composite.SaveAsPng(outputPath);
    }
}
```

### Step 6: Run tests to verify they pass

Run: `dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~DiffImageRenderer" --nologo`
Expected: 4 tests pass.

### Step 7: Verify CI

Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && ./scripts/ci.sh 2>&1 | grep -E "Passed:|Skipped:" | head -10`
Expected: **362 Passed + 46 Skipped** (was 358+46; +4 from DiffImageRendererTests).

---

## Task 3: BitmapAssertion plumbing

**Why:** Wire `DiffImageRenderer` into the bitmap assertion failure path. Generate diffs into the per-run report dir, skip on `--update-baselines`, surface paths via `BitmapAssertionResult`.

**Files:**
- Modify: `src/Runner/Bitmap/BitmapAssertion.cs`
- Create: `tests/Runner.Tests/Bitmap/BitmapAssertionDiffTests.cs`

### Step 1: Failing tests

- [ ] Create `tests/Runner.Tests/Bitmap/BitmapAssertionDiffTests.cs`:

```csharp
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Runner.Bitmap;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

public class BitmapAssertionDiffTests
{
    private sealed class FakeRpcClient : IBitmapRpcClient
    {
        public string CapturePath { get; init; } = string.Empty;
        public Task<BitmapCaptureResult> BitmapCaptureAsync(JsonElement? region, CancellationToken ct)
            => Task.FromResult(new BitmapCaptureResult { Path = CapturePath, Width = 64, Height = 64 });
    }

    private static string WriteGradientPng(string path, int seed = 0)
    {
        using var img = new Image<Rgba32>(64, 64);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
            img[x, y] = new Rgba32((byte)((x * 4 + seed) % 256), (byte)((y * 4 + seed) % 256), (byte)(((x + y) * 2 + seed) % 256), 255);
        img.SaveAsPng(path);
        return path;
    }

    private static string MakeBlackPng(string path)
    {
        using var img = new Image<Rgba32>(64, 64);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
            img[x, y] = new Rgba32(0, 0, 0, 255);
        img.SaveAsPng(path);
        return path;
    }

    [Fact]
    public async Task FailingAssertion_WritesThreeDiffPngs()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"bad-{Guid.NewGuid():N}");
        var outDir = Path.Combine(tmp, "out");
        Directory.CreateDirectory(tmp);
        try
        {
            var capture = WriteGradientPng(Path.Combine(tmp, "capture.png"));
            // Baseline is solid black — wildly different from the gradient capture.
            var baseline = MakeBlackPng(Path.Combine(tmp, "baseline.png"));

            var a = new ScenarioAssertion
            {
                Type = "bitmap",
                Baseline = baseline,
                Tolerance = 0.95,
            };
            var rpc = new FakeRpcClient { CapturePath = capture };
            var result = await BitmapAssertion.EvaluateAsync(
                rpc, a, scenarioPath: Path.Combine(tmp, "s.test.json"),
                updateBaselines: false,
                diffOutputDir: outDir,
                runWideDiffFormat: DiffFormat.Files,
                ct: CancellationToken.None);

            Assert.False(result.Passed);
            Assert.NotNull(result.Diffs);
            Assert.True(File.Exists(result.Diffs!.Baseline));
            Assert.True(File.Exists(result.Diffs.Capture));
            Assert.True(File.Exists(result.Diffs.Diff));
            Assert.Null(result.Diffs.Triptych);
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task PassingAssertion_WritesNoDiffPngs()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"bad-{Guid.NewGuid():N}");
        var outDir = Path.Combine(tmp, "out");
        Directory.CreateDirectory(tmp);
        try
        {
            var capture = WriteGradientPng(Path.Combine(tmp, "capture.png"));
            var baseline = WriteGradientPng(Path.Combine(tmp, "baseline.png"));

            var a = new ScenarioAssertion
            {
                Type = "bitmap",
                Baseline = baseline,
                Tolerance = 0.95,
            };
            var rpc = new FakeRpcClient { CapturePath = capture };
            var result = await BitmapAssertion.EvaluateAsync(
                rpc, a, scenarioPath: Path.Combine(tmp, "s.test.json"),
                updateBaselines: false,
                diffOutputDir: outDir,
                runWideDiffFormat: DiffFormat.Files,
                ct: CancellationToken.None);

            Assert.True(result.Passed);
            Assert.Null(result.Diffs);
            Assert.False(Directory.Exists(outDir), "diff dir should not be created on pass");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task UpdateBaselinesMode_FailingAssertion_WritesNoDiffPngs()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"bad-{Guid.NewGuid():N}");
        var outDir = Path.Combine(tmp, "out");
        Directory.CreateDirectory(tmp);
        try
        {
            var capture = WriteGradientPng(Path.Combine(tmp, "capture.png"));
            // Baseline starts solid black — would fail SSIM, but update mode should
            // overwrite it with the capture, not generate diffs.
            var baseline = MakeBlackPng(Path.Combine(tmp, "baseline.png"));

            var a = new ScenarioAssertion
            {
                Type = "bitmap",
                Baseline = baseline,
                Tolerance = 0.95,
            };
            var rpc = new FakeRpcClient { CapturePath = capture };
            var result = await BitmapAssertion.EvaluateAsync(
                rpc, a, scenarioPath: Path.Combine(tmp, "s.test.json"),
                updateBaselines: true,
                diffOutputDir: outDir,
                runWideDiffFormat: DiffFormat.Files,
                ct: CancellationToken.None);

            Assert.True(result.Passed);
            Assert.Null(result.Diffs);
            Assert.False(Directory.Exists(outDir), "diff dir should not be created in update-mode");
            // Baseline now matches the capture (existing behaviour).
            Assert.Equal(File.ReadAllBytes(capture), File.ReadAllBytes(baseline));
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }
}
```

### Step 2: Run tests to verify they fail

Run: `dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~BitmapAssertionDiff" --nologo`
Expected: build fails — `BitmapAssertionResult.Diffs` doesn't exist; `EvaluateAsync` doesn't accept `diffOutputDir`/`runWideDiffFormat`.

### Step 3: Extend BitmapAssertionResult

- [ ] Modify `src/Runner/Bitmap/BitmapAssertion.cs:45`. Replace the `BitmapAssertionResult` record:

```csharp
/// <summary>Pass/fail outcome from <see cref="BitmapAssertion.EvaluateAsync"/>.</summary>
/// <param name="Passed">True iff the assertion passed.</param>
/// <param name="FailureMessage">Human-readable failure message, null when passed.</param>
/// <param name="Diffs">Forensics PNG paths produced on SSIM failure (null otherwise — including update-baselines mode and other failure modes like missing-capture-path).</param>
public sealed record BitmapAssertionResult(bool Passed, string? FailureMessage, DiffSet? Diffs = null);
```

(Add `using SdvTestFramework.Protocol.Reports;` if not already present.)

### Step 4: Extend EvaluateAsync signature + logic

- [ ] Modify `src/Runner/Bitmap/BitmapAssertion.cs`. Replace the `EvaluateAsync` method:

```csharp
public static async Task<BitmapAssertionResult> EvaluateAsync(
    IBitmapRpcClient rpc,
    ScenarioAssertion a,
    string scenarioPath,
    bool updateBaselines,
    string? diffOutputDir,
    DiffFormat runWideDiffFormat,
    CancellationToken ct)
{
    if (string.IsNullOrEmpty(a.Baseline))
        return new BitmapAssertionResult(false, "bitmap assertion missing 'baseline' field");

    var baselinePath = BaselineManager.ResolveBaseline(scenarioPath, a.Baseline);

    // 1. Capture via RPC.
    BitmapCaptureResult captureResp;
    try { captureResp = await rpc.BitmapCaptureAsync(a.Region, ct); }
    catch (Exception ex) { return new BitmapAssertionResult(false, $"bitmap.capture failed: {ex.Message}"); }

    var capturePath = captureResp.Path;
    if (string.IsNullOrEmpty(capturePath))
        return new BitmapAssertionResult(false, "bitmap.capture response missing 'path' field");
    if (!File.Exists(capturePath))
        return new BitmapAssertionResult(false, $"bitmap.capture path does not exist: {capturePath}");

    // 2. Update-mode short-circuit.
    if (updateBaselines)
    {
        var bytes = await File.ReadAllBytesAsync(capturePath, ct);
        var existedBefore = File.Exists(baselinePath);
        BaselineManager.WriteBaseline(baselinePath, bytes);
        var action = existedBefore ? "updated" : "wrote";
        Console.WriteLine($"[bitmap] {action} baseline: {baselinePath}");
        return new BitmapAssertionResult(true, null);
    }

    // 3. Baseline existence check.
    if (!File.Exists(baselinePath))
        return new BitmapAssertionResult(false,
            $"baseline not found: {baselinePath} (re-run with --update-baselines to create it)");

    // 4. Load + diff.
    SsimResult ssim;
    byte[] baselineBytes, captureBytes;
    try
    {
        baselineBytes = await File.ReadAllBytesAsync(baselinePath, ct);
        captureBytes = await File.ReadAllBytesAsync(capturePath, ct);
        using var baseline = Image.Load<Rgba32>(baselineBytes);
        using var capture = Image.Load<Rgba32>(captureBytes);
        ssim = SsimDiff.Compute(baseline, capture);
    }
    catch (ArgumentException ex)
    {
        return new BitmapAssertionResult(false, ex.Message + " — regenerate baseline with --update-baselines");
    }
    catch (Exception ex)
    {
        return new BitmapAssertionResult(false, $"SSIM failed: {ex.Message}");
    }

    // 5. Tolerance check.
    var tolerance = a.Tolerance;
    if (tolerance <= 0 || tolerance > 1)
        return new BitmapAssertionResult(false,
            $"bitmap assertion 'tolerance' must be in (0, 1]; got {tolerance}");

    if (ssim.Score + 1e-9 >= tolerance)
        return new BitmapAssertionResult(true, null);

    // 6. SSIM failed + not in update mode → render diffs if a target dir is provided.
    DiffSet? diffs = null;
    if (!string.IsNullOrEmpty(diffOutputDir))
    {
        var format = a.DiffFormat ?? runWideDiffFormat;
        try
        {
            Directory.CreateDirectory(diffOutputDir);
            diffs = DiffImageRenderer.Render(
                baselineBytes, captureBytes, ssim, (float)tolerance, format, diffOutputDir);
        }
        catch (Exception ex)
        {
            // Don't compound a bitmap failure with a diff-render failure. Log + continue.
            Console.Error.WriteLine($"[bitmap] diff render failed: {ex.Message}");
        }
    }

    return new BitmapAssertionResult(false,
        $"SSIM {ssim.Score:F4} < tolerance {tolerance:F4}; capture: {capturePath}",
        diffs);
}
```

The `a.DiffFormat` reference will be unresolved until T5 — leave the code calling it; T3's tests pass `DiffFormat.Files` via `runWideDiffFormat` and don't exercise the per-assertion field. Compile error is expected; resolve by adding the field in T5.

**Workaround for T3 standalone build:** add a temporary `DiffFormat? DiffFormat { get; set; }` to `ScenarioAssertion` here as part of T3 (see Step 4b below), so the file compiles without waiting for T5. T5 will move it formally + add schema + tests for the JSON property.

### Step 4b: Stub DiffFormat field on ScenarioAssertion

- [ ] Modify `src/Protocol/Models/ScenarioAssertion.cs`. Add the new field at the end of the class:

```csharp
    /// <summary>For <c>bitmap</c> assertions: per-assertion override of the run-wide diff format. Null → use run-wide default.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("diff_format")]
    public SdvTestFramework.Runner.Bitmap.DiffFormat? DiffFormat { get; set; }
```

Wait — `Protocol` cannot reference `Runner.Bitmap`. Move `DiffFormat` enum from Runner to Protocol instead. Update Step 4 of T2: create `DiffFormat.cs` at `src/Protocol/Reports/DiffFormat.cs` with namespace `SdvTestFramework.Protocol.Reports`. Update DiffImageRenderer (T2 Step 5) and BitmapAssertion (T3 Step 4) to use that namespace.

(If you've already shipped `DiffFormat` in Runner per T2 as written, move it: change namespace + update imports. Verify build green after the move.)

After the move, `ScenarioAssertion.cs` adds:

```csharp
    [System.Text.Json.Serialization.JsonPropertyName("diff_format")]
    public SdvTestFramework.Protocol.Reports.DiffFormat? DiffFormat { get; set; }
```

### Step 5: Update existing BitmapAssertion callers

- [ ] Modify `src/Runner/Scenarios/ScenarioRunner.cs:294-301`. Replace the `case "bitmap":` block:

```csharp
case "bitmap":
{
    var rpc = new SessionBitmapRpcClient(_session);
    string? diffOutputDir = null;
    if (_reportDir is not null && _currentSpec is not null)
    {
        diffOutputDir = Path.Combine(
            _reportDir.ScenarioDir(_currentSpec.Name),
            "diffs",
            $"assertion-{assertionIndex:D2}-bitmap");
    }
    var result = await BitmapAssertion.EvaluateAsync(
        rpc, a, _scenarioPath, _updateBaselines,
        diffOutputDir, _runWideDiffFormat, ct);
    if (result.Diffs is { } diffSet && _currentReport is not null)
        _currentReport.Diffs.Add(diffSet);
    if (!result.Passed) await TryCaptureAssertionFailureAsync(ct);
    return (result.Passed, result.FailureMessage);
}
```

This references `assertionIndex` (added in T3 Step 6 below) and `_runWideDiffFormat` (added in T5).

### Step 6: Pass assertionIndex through

- [ ] Modify `src/Runner/Scenarios/ScenarioRunner.cs:181-187`. Replace the assertion loop + change `EvaluateAssertionAsync` signature:

```csharp
// 4. assertions
int assertionIndex = 0;
foreach (var a in spec.Assertions)
{
    report.AssertionsRun++;
    var (passed, detail) = await EvaluateAssertionAsync(a, assertionIndex, ct);
    if (passed) report.AssertionsPassed++;
    else report.Failures.Add($"{a.Type}: {detail ?? a.Message ?? "failed"}");
    assertionIndex++;
}
```

Update the method signature on line 251:

```csharp
private async Task<(bool Passed, string? Detail)> EvaluateAssertionAsync(
    ScenarioAssertion a, int assertionIndex, CancellationToken ct)
```

### Step 7: Add _runWideDiffFormat field with default

- [ ] Modify `src/Runner/Scenarios/ScenarioRunner.cs:32-34`. Add field:

```csharp
private readonly DiffFormat _runWideDiffFormat;
```

(Add `using SdvTestFramework.Protocol.Reports;` if not already present — it already is from existing `RunDirectory` import; just add the import for the enum if it lives in `Protocol.Reports` per Step 4b.)

Default to `DiffFormat.Files` in the existing 3-arg constructor body, and add a 4-arg constructor:

```csharp
public ScenarioRunner(JsonRpcSession session, bool updateBaselines, RunDirectory? reportDir)
    : this(session, updateBaselines, reportDir, DiffFormat.Files) { }

public ScenarioRunner(
    JsonRpcSession session,
    bool updateBaselines,
    RunDirectory? reportDir,
    DiffFormat runWideDiffFormat)
{
    _session = session;
    _updateBaselines = updateBaselines;
    _reportDir = reportDir;
    _recorder = reportDir is not null ? new ScreenshotRecorder(session) : null;
    _runWideDiffFormat = runWideDiffFormat;
}
```

### Step 8: Add Diffs collection to ScenarioReport

- [ ] Modify `src/Runner/Scenarios/ScenarioReport.cs`. Add at the bottom of the class:

```csharp
    /// <summary>Forensics PNG paths produced by failed bitmap assertions, indexed by assertion order.
    /// Populated when a <c>RunDirectory</c> is provided to <see cref="ScenarioRunner"/>.</summary>
    public List<DiffSet> Diffs { get; set; } = new();
```

(Add `using SdvTestFramework.Protocol.Reports;` if not already present — it is, via the existing `StepOutcome` import.)

### Step 9: Update existing BitmapAssertionTests

- [ ] Modify `tests/Runner.Tests/Bitmap/BitmapAssertionTests.cs`. Each `BitmapAssertion.EvaluateAsync(...)` call now needs `diffOutputDir` + `runWideDiffFormat` args. Use `null` for the dir (tests don't exercise diff output) and `DiffFormat.Files`:

```csharp
var result = await BitmapAssertion.EvaluateAsync(
    rpc, a, scenarioPath: Path.Combine(tmp, "s.test.json"),
    updateBaselines: false,
    diffOutputDir: null,
    runWideDiffFormat: DiffFormat.Files,
    ct: CancellationToken.None);
```

Apply this to all 4 tests (`MatchingCapture_Passes`, `MissingBaseline_WithoutUpdateFlag_Fails`, `MissingBaseline_WithUpdateFlag_WritesAndPasses`, `ExistingBaseline_WithUpdateFlag_OverwritesAndPasses`). Add `using SdvTestFramework.Protocol.Reports;` for the enum.

### Step 10: Run tests to verify they pass

Run: `dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~BitmapAssertion" --nologo`
Expected: 4 existing + 3 new = 7 tests pass.

### Step 11: Verify CI

Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && ./scripts/ci.sh 2>&1 | grep -E "Passed:|Skipped:" | head -10`
Expected: **365 Passed + 46 Skipped** (was 362+46; +3 from BitmapAssertionDiffTests).

---

## Task 4: HTML forensics section

**Why:** Surface the diff PNGs in the per-scenario HTML page so failures are immediately visible to humans + Claude.

**Files:**
- Modify: `src/Protocol/Reports/RunSummary.cs`
- Modify: `src/Runner/Reports/HtmlReportGenerator.cs`
- Modify: `src/Runner/Commands/RunCommand.cs` (BuildRunSummary populates Diffs)
- Create: `tests/Runner.Tests/Reports/HtmlReportGeneratorForensicsTests.cs`

### Step 1: Failing tests

- [ ] Create `tests/Runner.Tests/Reports/HtmlReportGeneratorForensicsTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Runner.Reports;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Reports;

public class HtmlReportGeneratorForensicsTests
{
    private static RunDirectory MakeRunDir(string testName)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"forensics-{testName}-{Guid.NewGuid():N}");
        return RunDirectory.Create(tmp);
    }

    [Fact]
    public void ScenarioWithDiffs_RendersForensicsSection()
    {
        var rd = MakeRunDir("with");
        try
        {
            var diff = new DiffSet(
                Baseline: "scenarios/x/diffs/assertion-03-bitmap/baseline.png",
                Capture:  "scenarios/x/diffs/assertion-03-bitmap/capture.png",
                Diff:     "scenarios/x/diffs/assertion-03-bitmap/diff.png",
                Triptych: null);
            var summary = new RunSummary(
                rd.RunId, "2026-04-25T15:30:00Z", 0,
                Scenarios: new[] { new ScenarioOutcome(
                    "x", null, false, 100,
                    Steps: Array.Empty<StepOutcome>(),
                    Assertions: new[] { new AssertionOutcome("bitmap", false, "SSIM 0.7234 < tolerance 0.9500") },
                    Screenshots: Array.Empty<string>(),
                    Diffs: new[] { diff }) });

            HtmlReportGenerator.Generate(rd, summary);

            var html = File.ReadAllText(Path.Combine(rd.ScenariosDir, "x", "report.html"));
            Assert.Contains("class=\"forensics\"", html);
            Assert.Contains("diffs/assertion-03-bitmap/baseline.png", html);
            Assert.Contains("diffs/assertion-03-bitmap/capture.png", html);
            Assert.Contains("diffs/assertion-03-bitmap/diff.png", html);
        }
        finally { Directory.Delete(rd.Root, recursive: true); }
    }

    [Fact]
    public void ScenarioWithoutDiffs_HasNoForensicsSection()
    {
        var rd = MakeRunDir("without");
        try
        {
            var summary = new RunSummary(
                rd.RunId, "2026-04-25T15:30:00Z", 0,
                Scenarios: new[] { new ScenarioOutcome(
                    "x", null, true, 100,
                    Steps: Array.Empty<StepOutcome>(),
                    Assertions: Array.Empty<AssertionOutcome>(),
                    Screenshots: Array.Empty<string>(),
                    Diffs: Array.Empty<DiffSet>()) });

            HtmlReportGenerator.Generate(rd, summary);

            var html = File.ReadAllText(Path.Combine(rd.ScenariosDir, "x", "report.html"));
            Assert.DoesNotContain("class=\"forensics\"", html);
            Assert.DoesNotContain("Failure forensics", html);
        }
        finally { Directory.Delete(rd.Root, recursive: true); }
    }
}
```

### Step 2: Run tests to verify they fail

Run: `dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~HtmlReportGeneratorForensics" --nologo`
Expected: build fails — `ScenarioOutcome` ctor doesn't accept `Diffs`.

### Step 3: Extend ScenarioOutcome with Diffs

- [ ] Modify `src/Protocol/Reports/RunSummary.cs`. Replace the `ScenarioOutcome` record:

```csharp
/// <summary>One scenario's outcome.</summary>
public sealed record ScenarioOutcome(
    string Name,
    string? Path,
    bool Passed,
    int DurationMs,
    IReadOnlyList<StepOutcome> Steps,
    IReadOnlyList<AssertionOutcome> Assertions,
    IReadOnlyList<string> Screenshots,
    IReadOnlyList<DiffSet> Diffs);
```

### Step 4: Add forensics section to HtmlReportGenerator

- [ ] Modify `src/Runner/Reports/HtmlReportGenerator.cs:144-145`. Insert the forensics block after the `<h2>Assertions</h2>` block (around line 144 in the current file, immediately before the screenshots block check at line 146):

```csharp
        if (s.Diffs.Count > 0)
        {
            sb.AppendLine("<section class=\"forensics\">");
            sb.AppendLine("<h2>Failure forensics</h2>");
            sb.AppendLine("<div class=\"diff-grid\">");
            int dIdx = 0;
            foreach (var d in s.Diffs)
            {
                var safe = SanitizeName(s.Name);
                sb.AppendLine("<figure class=\"diff-set\">");
                sb.Append("<h3>assertion-").Append(dIdx.ToString("D2")).AppendLine("-bitmap</h3>");
                sb.AppendLine("<div class=\"triptych\">");
                AppendDiffFigure(sb, d.Baseline, "baseline");
                AppendDiffFigure(sb, d.Capture, "capture");
                AppendDiffFigure(sb, d.Diff, "diff");
                sb.AppendLine("</div>");
                sb.AppendLine("</figure>");
                dIdx++;
            }
            sb.AppendLine("</div>");
            sb.AppendLine("</section>");
        }
```

Add the helper near `SanitizeName`:

```csharp
    private static void AppendDiffFigure(StringBuilder sb, string path, string caption)
    {
        // Path is run-dir-relative (forward slashes). The per-scenario page lives at
        // scenarios/<name>/report.html; navigate up two levels then down the relative path.
        var rel = "../../" + path;
        sb.Append("<figure><img src=\"").Append(WebUtility.HtmlEncode(rel))
          .Append("\" alt=\"").Append(WebUtility.HtmlEncode(caption)).Append("\">");
        sb.Append("<figcaption>").Append(WebUtility.HtmlEncode(caption)).AppendLine("</figcaption></figure>");
    }
```

### Step 5: Add forensics CSS

- [ ] Modify `src/Runner/Reports/HtmlReportGenerator.cs:194`. Append to the `CssTemplate` constant (just before the closing `"""`):

```csharp
        section.forensics { background: #fff5f5; border-left: 4px solid #b03030; padding: 1em; margin: 1em 0; }
        section.forensics h2 { margin-top: 0; color: #b03030; }
        .diff-set h3 { font-family: monospace; font-size: 1em; margin: 0.5em 0; }
        .triptych { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 0.5em; }
        .triptych img { width: 100%; height: auto; border: 1px solid #ddd; }
        .triptych figcaption { text-align: center; font-size: 0.85em; color: #666; }
```

### Step 6: Update RunCommand.BuildRunSummary

- [ ] Modify `src/Runner/Commands/RunCommand.cs:330-337`. Replace the `scenarioOutcomes.Add(...)` call to pass `report.Diffs`:

```csharp
            scenarioOutcomes.Add(new ScenarioOutcome(
                Name: report.Name,
                Path: string.IsNullOrEmpty(report.Path) ? null : report.Path,
                Passed: report.Passed,
                DurationMs: report.DurationMs,
                Steps: report.Steps,
                Assertions: assertions,
                Screenshots: report.Screenshots,
                Diffs: report.Diffs));
```

### Step 7: Update existing HtmlReportGenerator tests

- [ ] Modify `tests/Runner.Tests/Reports/HtmlReportGeneratorTests.cs`. The existing 4 tests construct `ScenarioOutcome` without `Diffs`. Add `Diffs: Array.Empty<DiffSet>()` to each ctor call (4 sites). Add `using SdvTestFramework.Protocol.Reports;` if not already present.

```csharp
new ScenarioOutcome(
    "shop_menu_test", "tests/samples/shop.test.json", true, 1234,
    Steps: ...,
    Assertions: ...,
    Screenshots: Array.Empty<string>(),
    Diffs: Array.Empty<DiffSet>())
```

### Step 8: Run tests to verify they pass

Run: `dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~HtmlReport" --nologo`
Expected: 4 existing + 2 new forensics = 6 tests pass.

### Step 9: Verify CI

Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && ./scripts/ci.sh 2>&1 | grep -E "Passed:|Skipped:" | head -10`
Expected: **367 Passed + 46 Skipped** (was 365+46; +2 from forensics tests).

---

## Task 5: DiffFormat knobs (CLI + MCP + schema)

**Why:** Wire `--diff-format` into `RunCommand`, the MCP `run_scenario` tool, and the scenario schema. T3 already added the per-assertion `DiffFormat` field; this task plumbs the run-wide knob.

**Files:**
- Modify: `src/Runner/Commands/RunCommand.cs`
- Modify: `src/Runner.Mcp/Tools/RunScenarioTool.cs`
- Modify: `schemas/scenario.schema.json`
- Create: `tests/Runner.Mcp.Tests/Tools/RunScenarioDiffFormatTests.cs`

### Step 1: Failing test

- [ ] Create `tests/Runner.Mcp.Tests/Tools/RunScenarioDiffFormatTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Mcp;
using SdvTestFramework.Runner.Mcp.Tools;
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests.Tools;

public class RunScenarioDiffFormatTests
{
    private sealed class RecordingLifecycle : SdvLifecycle
    {
        public List<(string Method, string ParamsJson)> Calls { get; } = new();
        public Dictionary<string, string> Responses { get; } = new();
        public override Task<JsonElement> InvokeAsync(string method, JsonElement? p, CancellationToken ct)
        {
            Calls.Add((method, p?.GetRawText() ?? ""));
            var resp = Responses.TryGetValue(method, out var r) ? r : "{}";
            return Task.FromResult(JsonDocument.Parse(resp).RootElement.Clone());
        }
    }

    [Fact]
    public async Task RunScenario_DiffFormatArg_AcceptedWithoutError()
    {
        // The MCP tool's run_scenario doesn't currently evaluate bitmap assertions
        // (it delegates to the CLI runner). The minimum contract this test enforces:
        // passing a diff_format arg doesn't error — schema accepts it, tool routes it.
        var tmp = Path.Combine(Path.GetTempPath(), $"mcp-df-{Guid.NewGuid():N}.test.json");
        File.WriteAllText(tmp, "{\"name\":\"n\",\"config\":{\"seed\":42},\"steps\":[],\"assertions\":[]}");
        var lifeBaseDir = Path.Combine(Path.GetTempPath(), $"mcp-df-base-{Guid.NewGuid():N}");
        Directory.CreateDirectory(lifeBaseDir);

        try
        {
            var life = new RecordingLifecycle();
            life.Responses["scenario.begin"] = "{\"session_id\":\"x\",\"tick\":0}";
            life.Responses["scenario.end"]   = "{\"duration_ms\":1,\"assertions_run\":0,\"assertions_passed\":0}";

            var tool = new RunScenarioTool();
            var argsJson = $"{{\"path\":{JsonSerializer.Serialize(tmp)},\"report_dir\":{JsonSerializer.Serialize(lifeBaseDir)},\"diff_format\":\"triptych\"}}";
            var args = JsonDocument.Parse(argsJson).RootElement;
            var result = await tool.InvokeAsync(args, life, CancellationToken.None);

            Assert.False(result.IsError);
            // Tool's input schema must declare diff_format; verify by inspecting InputSchema.
            var schemaText = tool.InputSchema.GetRawText();
            Assert.Contains("diff_format", schemaText);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            if (Directory.Exists(lifeBaseDir)) Directory.Delete(lifeBaseDir, recursive: true);
        }
    }
}
```

### Step 2: Run test to verify it fails

Run: `dotnet test tests/Runner.Mcp.Tests/ --filter "FullyQualifiedName~RunScenarioDiffFormat" --nologo`
Expected: fails — `InputSchema` doesn't contain `diff_format`.

### Step 3: Add --diff-format flag to RunCommand

- [ ] Modify `src/Runner/Commands/RunCommand.cs:36-50`. Add new flag variable + parse:

```csharp
        bool noReport = false;
        DiffFormat diffFormat = DiffFormat.Files;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args.Span[i];
            if (a == "--filter" && i + 1 < args.Length) { filter = args.Span[++i]; continue; }
            // ... existing flags ...
            if (a == "--diff-format" && i + 1 < args.Length)
            {
                var raw = args.Span[++i];
                if (!Enum.TryParse<DiffFormat>(raw, ignoreCase: true, out diffFormat))
                {
                    Console.Error.WriteLine($"[run] invalid --diff-format '{raw}'; expected files|triptych|all");
                    return 2;
                }
                continue;
            }
            paths.Add(a);
        }
```

(Add `using SdvTestFramework.Protocol.Reports;` to the top — `DiffFormat` enum lives there per T3 Step 4b.)

### Step 4: Plumb diffFormat to ScenarioRunner

- [ ] Modify `src/Runner/Commands/RunCommand.cs:279`. Replace the `ScenarioRunner` construction in `RunOnceAsync`:

```csharp
        var runner = new ScenarioRunner(session, _updateBaselinesFlag, runDir, _diffFormatFlag);
```

Then add a static field next to `_updateBaselinesFlag`:

```csharp
    private static bool _updateBaselinesFlag;
    private static DiffFormat _diffFormatFlag = DiffFormat.Files;
```

And in the args parsing, after `_updateBaselinesFlag = updateBaselines;`:

```csharp
        _updateBaselinesFlag = updateBaselines;
        _diffFormatFlag = diffFormat;
```

### Step 5: Add diff_format to MCP tool schema + arg parsing

- [ ] Modify `src/Runner.Mcp/Tools/RunScenarioTool.cs`. Update the `InputSchema` JSON literal to add the new property:

```csharp
    public JsonElement InputSchema { get; } = JsonDocument.Parse("""
        {"type":"object","properties":{
           "path":{"type":"string"},
           "report_dir":{"type":"string","description":"Optional output directory for the HTML run report. Default: ./test-results/<auto-id>/"},
           "diff_format":{"type":"string","enum":["files","triptych","all"],"description":"Diff artifacts produced on bitmap-assertion failure. Default: files (3 separate PNGs)."}
         },"required":["path"]}
        """).RootElement;
```

The current MCP `run_scenario` tool delegates non-trivial assertion evaluation to the CLI runner; MCP's path doesn't actually emit diffs today. The arg is accepted for forward-compat — if/when MCP gains full bitmap evaluation it can read this. Add a one-line comment:

```csharp
        // diff_format is parsed for forward-compat; the MCP run_scenario path doesn't currently
        // evaluate bitmap assertions itself (see class XML doc) — full DSL eval is a Tier 3 followup.
        if (args.TryGetProperty("diff_format", out _)) { /* no-op for now */ }
```

(Place inside `InvokeAsync` after the `report_dir` arg parsing block.)

### Step 6: Update scenario JSON schema

- [ ] Modify `schemas/scenario.schema.json`. Find the bitmap assertion entry (search for `"const": "bitmap"` or `"baseline"`). Add the `diff_format` property to its `properties` object:

```json
"diff_format": {
  "type": "string",
  "enum": ["files", "triptych", "all"],
  "description": "Per-assertion override of the run-wide --diff-format flag. Optional."
}
```

### Step 7: Run test to verify it passes

Run: `dotnet test tests/Runner.Mcp.Tests/ --filter "FullyQualifiedName~RunScenarioDiffFormat" --nologo`
Expected: 1 test passes.

### Step 8: Verify CI

Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && ./scripts/ci.sh 2>&1 | grep -E "Passed:|Skipped:" | head -10`
Expected: **368 Passed + 46 Skipped** (was 367+46; +1 from MCP test).

---

## Task 6: Smoke + docs + roadmap

**Why:** Final wrap-up. Skipped placeholder for live smoke + doc updates + roadmap maintenance.

**Files:**
- Create: `tests/Runner.Tests/Bitmap/DiffImageIntegrationTests.cs` (skipped placeholder)
- Modify: `docs/dsl-quickstart.md`
- Modify: `docs/milestones/current.md`
- Modify: `docs/roadmap.md`

### Step 1: Integration placeholder

- [ ] Create `tests/Runner.Tests/Bitmap/DiffImageIntegrationTests.cs`:

```csharp
using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

/// <summary>Integration surface for diff-image-on-failure — verified manually via tampered baseline.</summary>
public class DiffImageIntegrationTests
{
    [Fact(Skip = "Requires live SDV — tamper a baseline + run-samples.sh; verify forensics PNGs in test-results/<run-id>/scenarios/.../diffs/.")]
    public void DiffPngs_GeneratedOnBitmapFailure() { }
}
```

### Step 2: Live smoke — DOCUMENT AS MANUAL ONLY

The smoke flow is: tamper `tests/samples/baselines/bitmap_shop_menu_basic.png` (overwrite with unrelated PNG), run `./scripts/run-samples.sh`, expect scenario 11 to fail and `test-results/<run-id>/scenarios/bitmap_shop_menu_basic/diffs/assertion-XX-bitmap/{baseline,capture,diff}.png` to exist. Per project history (D1.5, D1.6, M2-record, HTML run reports T7 smokes), live SDV under headless Xvfb is brittle. Document as manual, not auto-run.

### Step 3: Update docs/dsl-quickstart.md

- [ ] Read `docs/dsl-quickstart.md` first to find the bitmap-assertion section. Add a paragraph mentioning `diff_format`:

```markdown
### Diff-image on failure

When a `bitmap` assertion fails, the runner writes forensics PNGs into the per-run report
directory at `scenarios/<scenario>/diffs/assertion-NN-bitmap/`:

- `baseline.png` — the expected image.
- `capture.png` — what was actually rendered.
- `diff.png` — baseline with a bilinear-smoothed red heatmap overlaid where blocks fell
  below the SSIM tolerance.

Optional composite via `--diff-format=triptych` (CLI) or `"diff_format": "triptych"`
(per-assertion in the scenario JSON) writes a 4th `triptych.png` with all three side-by-side.
The HTML report's per-scenario page surfaces all of these in a "Failure forensics" section.

`--update-baselines` short-circuits diff generation — the capture overwrites the baseline
instead, so there's nothing to forensics.
```

### Step 4: Update docs/milestones/current.md

- [ ] Append a new completion subsection at the end (after the HTML run reports subsection):

```markdown
### Diff-image-on-failure landed (2026-04-25)

Plan: `docs/superpowers/plans/2026-04-25-diff-image-on-failure.md` (6 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-25-diff-image-on-failure-design.md`.

**Scope:** on `bitmap` assertion failure (and not `--update-baselines`), write per-failure
forensics PNGs into the per-run report dir. Surface in HTML report's "Failure forensics"
section. Pairs naturally with HTML run reports — Claude reasons about visual regressions
from the diff PNG path, not just the SSIM number.

**Architecture:** `SsimDiff.Compute` extended to return `SsimResult { Score, BlockScores,
BlocksX, BlocksY }` instead of bare float. `DiffImageRenderer` is a pure function that
takes baseline+capture bytes + `SsimResult` + tolerance + format, writes 3 PNGs (always)
and optionally a triptych composite. Heatmap uses bilinear-smoothed per-block redness so
hot regions taper continuously rather than tiling at hard 8-pixel edges. `BitmapAssertion`
calls the renderer on SSIM failure; `ScenarioRunner` collects DiffSets per failed
assertion; `HtmlReportGenerator` renders a `<section class="forensics">` above the
existing screenshots grid.

**Knobs:**
- CLI: `sdv-test run --diff-format=<files|triptych|all>` (default `files`).
- MCP: `run_scenario` accepts `diff_format` arg (forward-compat only — MCP's run_scenario
  path doesn't currently evaluate bitmap assertions itself; CLI runner is the rich path).
- Per-assertion: `"diff_format": "triptych"` in the bitmap assertion JSON overrides the
  run-wide flag.

**`DiffSet` cross-project type:** placed in `src/Protocol/Reports/` alongside other shared
report types so both `Runner.Mcp` and `Runner` can reference it without dragging Runner-only
code transitively. Same precedent as the HTML run reports T5 fixup.

**Test count after diff-image-on-failure:** 357+46 → 368+47 (+11 passed, +1 skipped).

**Out of scope (Tier 3/4 followups):**
- Pixel-exact + dHash bitmap methods (separate Tier 3 item).
- Diff annotations (arrows, labels) and animated diffs.
- Configurable diff color scheme (red-tint heatmap is the only option).
- MCP-side bitmap-assertion evaluation (today delegates to CLI runner).
- Diff retention / cleanup policy — diffs accumulate alongside captures; pairs with
  the existing Tier 4 capture-cache cleanup item.
```

### Step 5: Update docs/roadmap.md

- [ ] Read `docs/roadmap.md`. Remove the `Diff-image-on-failure` entry from Tier 3 (the bullet near the top of the Tier 3 section). Add a new dated subsection at the top of `## Completed`:

```markdown
### 2026-04-25

- **Diff-image-on-failure**. Bitmap assertion failures (when not `--update-baselines`) now
  write `baseline.png` + `capture.png` + `diff.png` (bilinear-smoothed heatmap) into
  `<run-dir>/scenarios/<scenario>/diffs/assertion-NN-bitmap/`. Optional `triptych.png`
  composite via `--diff-format=triptych` or per-assertion override. Surfaced in HTML
  report's "Failure forensics" section. Pairs with HTML run reports for one-glance LLM-
  driven debugging. 357+46 → 368+47.
```

If a `### 2026-04-25` subsection already exists (because something else shipped today),
append the bullet to it instead of creating a new subsection.

### Step 6: Final CI

Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && ./scripts/ci.sh 2>&1 | grep -E "Passed:|Skipped:" | head -10`
Expected: **368 Passed + 47 Skipped** (was 368+46; +1 from skipped placeholder).

---

## Self-review

**1. Spec coverage:**
- Spec §4.2 (SsimResult) → T1 ✓
- Spec §4.3 (DiffImageRenderer including bilinear interpolation, heatmap math, triptych) → T2 ✓
- Spec §4.4 (BitmapAssertion failure flow + assertion-id format) → T3 ✓
- Spec §4.5 (DiffFormat resolution: per-assertion > run-wide > default) → T3 + T5 ✓
- Spec §4.6 (HTML forensics section + CSS) → T4 ✓
- Spec §4.7 (MCP integration via report_dir transitivity + diff_format arg) → T5 ✓
- Spec §4.8 (update-baselines short-circuit) → T3 ✓
- Spec §5 (wire-format additions: scenario `diff_format`, summary.json `diffs`) → T5 (schema), T4 (`ScenarioOutcome.Diffs` serialized) ✓
- Spec §6 (testing) → T1 (1) + T2 (4) + T3 (3) + T4 (2) + T5 (1) + T6 (1 skipped) = 11 + 1 ✓

**2. Placeholder scan:** No TBDs. Each step has concrete code/commands. The skipped placeholder is explicit per project convention.

**3. Type consistency:**
- `SsimResult` ctor signature defined T1 → consumed T2 (test fixture builder), T3 (BitmapAssertion).
- `DiffSet` ctor signature defined T2 → consumed T3 (BitmapAssertionResult), T4 (ScenarioOutcome).
- `DiffFormat` enum defined T2 (in Protocol.Reports per T3 Step 4b note) → consumed T3 (BitmapAssertion param), T5 (CLI flag, MCP schema).
- `BitmapAssertion.EvaluateAsync` signature gains `diffOutputDir` + `runWideDiffFormat` in T3 → existing tests in T3 Step 9 updated, ScenarioRunner caller updated in T3 Step 5.
- `ScenarioRunner` ctor gains a 4-arg overload accepting `runWideDiffFormat` in T3 Step 7 → CLI uses it in T5 Step 4.
- `ScenarioOutcome` ctor gains `Diffs` parameter in T4 Step 3 → existing tests updated in T4 Step 7 + RunCommand.BuildRunSummary updated in T4 Step 6.
- `ScenarioReport.Diffs` property defined T3 Step 8 → consumed T3 Step 5 (the bitmap case).
- Path conventions: forensics dir is `<runDir>/scenarios/<scenarioName>/diffs/assertion-{idx:D2}-bitmap/` consistently across T3 and T4.

**4. Hazards:**
- **DiffFormat enum location.** T2 originally placed it in `Runner.Bitmap`; T3 Step 4b moves it to `Protocol.Reports` so `ScenarioAssertion` (in Protocol) can reference it without crossing project boundaries the wrong way. Implementer must do the move OR catch the compile error in T3 and fix forward. The plan acknowledges this inline.
- **`a.DiffFormat` field timing.** `ScenarioAssertion.DiffFormat` is added in T3 Step 4b (early — same task that uses it) so the bitmap evaluator can read it. T5 adds the schema entry + CLI/MCP wiring on top.
- **Bilinear test brittleness.** `BilinearSmoothing_NoHardBlockBoundaries` samples at fixed pixel coordinates. If the math constants change (the 0.6/0.4 mix, block size 8) the test breaks. That's intentional — the test guards the visual-quality contract. If the constants legitimately change, update the test together.
- **Watch-mode + diff accumulation.** Watch mode reuses the run dir across reruns (per HTML run reports T4 wiring). A bitmap assertion that fails twice will overwrite the previous diffs (same `assertion-NN-bitmap/` path). That's correct — newest diff wins.
- **Test count arithmetic.** Per-task: T1+1, T2+4, T3+3, T4+2, T5+1, T6+1 skipped = 357+46 → 368+47. Per-task gates check the cumulative count at each step.

---

## Execution handoff

Plan complete + saved to `docs/superpowers/plans/2026-04-25-diff-image-on-failure.md`.
Two execution options:

**1. Subagent-Driven (recommended)** — fresh subagent per task, two-stage review.

**2. Inline Execution** — tasks run in this session via executing-plans.

**Which approach?**
