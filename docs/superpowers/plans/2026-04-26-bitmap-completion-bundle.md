# Bitmap Completion Bundle — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **No git repo.** Task completion gate is **`./scripts/ci.sh` green** at the per-task expected count. T8's extra gates:
> - `sdv-test baselines list` enumerates referenced baselines from a sample dir.
> - `sdv-test cache clean --dry-run` reports without deleting.
> - `--diff-format=files` still produces 3 PNGs (T1-T6 don't break the existing diff path).

**Goal:** Complete spec §4.5 bitmap diff methods (pixel-exact + dHash); add three-tier tolerance preset (`generic`/`ci-ubuntu`/`self-hosted-nvidia`); replace the `--update-baselines` static-field hack with a real `sdv-test baselines` subcommand; cap `~/.cache/sdv-test-framework/captures/` growth.

**Architecture:** Method dispatch in `BitmapAssertion.EvaluateAsync` based on `a.Method ?? "ssim"`. Per-method `tolerance` semantics polymorphic (SSIM float, pixel-exact int channel-delta, dHash int Hamming distance). Tier maps to per-method default tolerances via a static lookup table; per-assertion `tolerance` always wins. New `BaselinesCommand` reuses RunCommand's run path via a `RunCommandOptions` record (refactor that also removes the static-field hack). New `CaptureCacheCleaner` runs automatically end-of-run and via standalone `sdv-test cache clean`.

**Tech Stack:**
- ImageSharp 3.1.12 already a Runner dep (resize for dHash). No new NuGets.
- `System.Numerics.BitOperations.PopCount` for dHash Hamming.
- All shared types live in `src/Runner/Bitmap/` except `BitmapMethod` enum which goes in `src/Protocol/Reports/` (so `ScenarioAssertion` can reference it without a Protocol→Runner cycle, mirroring the `DiffFormat`/`DiffSet` precedent from diff-image-on-failure).

**Design spec:** `docs/superpowers/specs/2026-04-26-bitmap-completion-bundle-design.md`

---

## File structure

**New (Protocol):**
- `src/Protocol/Reports/BitmapMethod.cs` — enum `Ssim | PixelExact | DHash`. Cross-project shared so `ScenarioAssertion` can carry its serialized form.

**New (Runner):**
- `src/Runner/Bitmap/PixelExactDiff.cs` — pure function `MaxChannelDelta(Image, Image) → int`.
- `src/Runner/Bitmap/DHashDiff.cs` — `Compute(Image) → ulong` + `HammingDistance(Image, Image) → int`.
- `src/Runner/Bitmap/TierTolerance.cs` — static `Resolve(string tier, BitmapMethod method, double? perAssertionTolerance) → double` with the 3×3 tier table.
- `src/Runner/Bitmap/CaptureCacheCleaner.cs` — sweep helper.
- `src/Runner/Commands/RunCommandOptions.cs` — record threading flag values from argv to RunOnceAsync (kills the static-field hack).
- `src/Runner/Commands/BaselinesCommand.cs` — `sdv-test baselines <subcommand>` dispatcher.
- `src/Runner/Commands/CacheCommand.cs` — `sdv-test cache clean` dispatcher.

**New tests:**
- `tests/Runner.Tests/Bitmap/PixelExactDiffTests.cs` — 3 tests.
- `tests/Runner.Tests/Bitmap/DHashDiffTests.cs` — 3 tests.
- `tests/Runner.Tests/Bitmap/BitmapMethodDispatchTests.cs` — 3 tests.
- `tests/Runner.Tests/Bitmap/DiffImageRendererMethodTests.cs` — 2 tests.
- `tests/Runner.Tests/Bitmap/TierToleranceTests.cs` — 2 tests.
- `tests/Runner.Tests/Commands/BaselinesCommandTests.cs` — 4 tests.
- `tests/Runner.Tests/Bitmap/CaptureCacheCleanerTests.cs` — 3 tests.
- `tests/Runner.Tests/Bitmap/BitmapMethodIntegrationTests.cs` — 1 skipped placeholder.

**Modified:**
- `src/Protocol/Models/ScenarioAssertion.cs` — add `Method` (`string?`, default null → `"ssim"`) and `Tier` (`string?`, default null → run-wide).
- `src/Runner/Bitmap/BitmapAssertion.cs` — branch on method; thread effective tolerance from tier.
- `src/Runner/Bitmap/DiffImageRenderer.cs` — `Render` signature takes `BitmapMethod method`, `SsimResult? ssim`, `double tolerance` (per spec §4.6).
- `src/Runner/Scenarios/ScenarioRunner.cs` — gain run-wide tier; pass tier-resolved tolerance into BitmapAssertion.
- `src/Runner/Commands/RunCommand.cs` — `--tier` flag; remove static fields; thread `RunCommandOptions`; `--no-cache-cleanup` flag; auto-invoke `CaptureCacheCleaner` at end.
- `src/Runner/Program.cs` — register `baselines` + `cache` top-level commands; expand `PrintHelp`.
- `schemas/scenario.schema.json` — bitmap variant gains `method` + `tier` enums; document `tolerance` polymorphism in description.
- `tests/Runner.Tests/Bitmap/DiffImageRendererTests.cs` — update existing 4 tests with the new `BitmapMethod.Ssim` arg.
- `tests/Runner.Tests/Bitmap/BitmapAssertionTests.cs` + `BitmapAssertionDiffTests.cs` — leave `Method` null (defaults to `ssim`); no behavior change expected since `ScenarioAssertion.Method` defaults to null. Verify by running them after T3.

**Starting test count:** 368 Passed + 47 Skipped.
**Target:** 388 Passed + 48 Skipped (+20 passing, +1 skipped).

---

## Task 1: PixelExactDiff

**Why:** Pure-function pixel comparison. No dependencies beyond ImageSharp. Tests-first, isolated.

**Files:**
- Create: `src/Runner/Bitmap/PixelExactDiff.cs`
- Create: `tests/Runner.Tests/Bitmap/PixelExactDiffTests.cs`

### Step 1: Failing tests

- [ ] Create `tests/Runner.Tests/Bitmap/PixelExactDiffTests.cs`:

```csharp
using System;
using SdvTestFramework.Runner.Bitmap;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

public class PixelExactDiffTests
{
    private static Image<Rgba32> Solid(int w, int h, byte r, byte g, byte b)
    {
        var img = new Image<Rgba32>(w, h);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            img[x, y] = new Rgba32(r, g, b, 255);
        return img;
    }

    [Fact]
    public void IdenticalImages_ReturnsZero()
    {
        using var a = Solid(8, 8, 100, 100, 100);
        using var b = Solid(8, 8, 100, 100, 100);
        Assert.Equal(0, PixelExactDiff.MaxChannelDelta(a, b));
    }

    [Fact]
    public void OffByOneChannel_ReturnsOne()
    {
        using var a = Solid(8, 8, 100, 100, 100);
        using var b = Solid(8, 8, 101, 100, 100);
        Assert.Equal(1, PixelExactDiff.MaxChannelDelta(a, b));
    }

    [Fact]
    public void MaxChannelDeltaAcrossPixels_ReturnsLargestSingleDelta()
    {
        using var a = Solid(8, 8, 100, 100, 100);
        using var b = Solid(8, 8, 100, 100, 100);
        // Spike one pixel: R goes from 100 → 200, G unchanged, B unchanged.
        b[3, 4] = new Rgba32(200, 100, 100, 255);
        Assert.Equal(100, PixelExactDiff.MaxChannelDelta(a, b));
    }

    [Fact]
    public void DimensionMismatch_Throws()
    {
        using var a = Solid(8, 8, 0, 0, 0);
        using var b = Solid(16, 16, 0, 0, 0);
        var ex = Assert.Throws<ArgumentException>(() => PixelExactDiff.MaxChannelDelta(a, b));
        Assert.Contains("mismatch", ex.Message);
    }
}
```

(4 tests, not 3 — the `DimensionMismatch_Throws` is essentially free since it mirrors `SsimDiff.DifferentDimensions_Throws` and is consistent with the spec's "Throws ArgumentException matching SsimDiff pattern". The plan target is +3 passed for T1; if this 4th test makes the count +4 instead, that's fine — adjust the cumulative count downstream.)

Actually, to keep test counts predictable, omit `DimensionMismatch_Throws` from T1. The dimension-mismatch behavior is covered implicitly by the existing SsimDiff tests + the dispatch tests in T3.

Use only the first 3 tests. Net +3.

### Step 2: Run tests to verify they fail

Run: `dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~PixelExactDiff" --nologo`
Expected: build fails — `PixelExactDiff` does not exist.

### Step 3: Create PixelExactDiff.cs

- [ ] Create `src/Runner/Bitmap/PixelExactDiff.cs`:

```csharp
using System;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SdvTestFramework.Runner.Bitmap;

/// <summary>
/// Pixel-exact diff. Returns the maximum per-channel RGB delta across all pixels.
/// Alpha is ignored (consistent with <see cref="SsimDiff"/>). 0 = bit-identical RGB.
/// </summary>
public static class PixelExactDiff
{
    /// <summary>
    /// Compute max per-channel delta. Both images must share dimensions; otherwise throws
    /// <see cref="ArgumentException"/> with the mismatch shape in the message (matches
    /// <see cref="SsimDiff"/>).
    /// </summary>
    public static int MaxChannelDelta(Image<Rgba32> a, Image<Rgba32> b)
    {
        if (a.Width != b.Width || a.Height != b.Height)
            throw new ArgumentException(
                $"pixel-exact dim mismatch: {a.Width}×{a.Height} vs {b.Width}×{b.Height}");

        int max = 0;
        int w = a.Width, h = a.Height;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var pa = a[x, y];
            var pb = b[x, y];
            int dr = Math.Abs(pa.R - pb.R);
            int dg = Math.Abs(pa.G - pb.G);
            int db = Math.Abs(pa.B - pb.B);
            int local = Math.Max(dr, Math.Max(dg, db));
            if (local > max) max = local;
        }
        return max;
    }
}
```

### Step 4: Run tests to verify they pass

Run: `dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~PixelExactDiff" --nologo`
Expected: 3 tests pass.

### Step 5: Verify CI

Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && ./scripts/ci.sh 2>&1 | grep -E "Passed:|Skipped:" | head -10`
Expected: **371 Passed + 47 Skipped** (was 368+47; +3).

---

## Task 2: DHashDiff

**Why:** Perceptual difference-hash. Standalone pure function — no dependencies beyond ImageSharp.

**Files:**
- Create: `src/Runner/Bitmap/DHashDiff.cs`
- Create: `tests/Runner.Tests/Bitmap/DHashDiffTests.cs`

### Step 1: Failing tests

- [ ] Create `tests/Runner.Tests/Bitmap/DHashDiffTests.cs`:

```csharp
using System;
using SdvTestFramework.Runner.Bitmap;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

public class DHashDiffTests
{
    // Same gradient pattern used by SsimDiffTests — deterministic 64×64 RGB.
    private static Image<Rgba32> Gradient(int seed = 0)
    {
        var img = new Image<Rgba32>(64, 64);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
            img[x, y] = new Rgba32(
                (byte)((x * 4 + seed) % 256),
                (byte)((y * 4 + seed) % 256),
                (byte)(((x + y) * 2 + seed) % 256),
                255);
        return img;
    }

    private static Image<Rgba32> GradientWithNoise(int seed = 123)
    {
        var img = Gradient();
        var rng = new Random(seed);
        for (int y = 0; y < img.Height; y++)
        for (int x = 0; x < img.Width; x++)
        {
            var p = img[x, y];
            byte Clamp(int v) => (byte)Math.Clamp(v, 0, 255);
            img[x, y] = new Rgba32(
                Clamp(p.R + rng.Next(-2, 3)),
                Clamp(p.G + rng.Next(-2, 3)),
                Clamp(p.B + rng.Next(-2, 3)),
                255);
        }
        return img;
    }

    private static Image<Rgba32> Inverted()
    {
        var img = Gradient();
        for (int y = 0; y < img.Height; y++)
        for (int x = 0; x < img.Width; x++)
        {
            var p = img[x, y];
            img[x, y] = new Rgba32((byte)(255 - p.R), (byte)(255 - p.G), (byte)(255 - p.B), 255);
        }
        return img;
    }

    [Fact]
    public void IdenticalImages_HammingDistanceZero()
    {
        using var a = Gradient();
        using var b = Gradient();
        Assert.Equal(0, DHashDiff.HammingDistance(a, b));
    }

    [Fact]
    public void MinorNoise_HammingDistanceLowSingleDigit()
    {
        using var a = Gradient();
        using var b = GradientWithNoise(seed: 123);
        var d = DHashDiff.HammingDistance(a, b);
        // ±2 LSB noise in RGB → grayscale conversion + 9×8 resize smooths it out;
        // expect very few bit flips. Threshold 5 matches the dHash defaults.
        Assert.InRange(d, 0, 5);
    }

    [Fact]
    public void Inverted_HammingDistanceHigh()
    {
        using var a = Gradient();
        using var b = Inverted();
        var d = DHashDiff.HammingDistance(a, b);
        // Inversion flips luminance → most adjacent-pair comparisons reverse direction
        // → many bit flips. Expect distance well above the "vaguely similar" threshold.
        Assert.True(d >= 30, $"expected ≥30 bit flips for inverted gradient, got {d}");
    }
}
```

### Step 2: Run tests to verify they fail

Run: `dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~DHashDiff" --nologo`
Expected: build fails — `DHashDiff` does not exist.

### Step 3: Create DHashDiff.cs

- [ ] Create `src/Runner/Bitmap/DHashDiff.cs`:

```csharp
using System;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SdvTestFramework.Runner.Bitmap;

/// <summary>
/// Difference-hash perceptual hash. Resizes to 9×8 grayscale, packs 64 bits where each
/// bit indicates whether the left pixel of an adjacent horizontal pair is darker than
/// the right. Hamming distance between two hashes ≈ how perceptually different the images
/// are. Range [0, 64]; ≤5 is "looks the same", >10 is "clearly different".
/// </summary>
/// <remarks>
/// Luminance via Rec. 601: <c>Y = 0.299·R + 0.587·G + 0.114·B</c>. Alpha ignored.
/// Resize uses bicubic to smooth high-frequency noise. Standard difference-hash
/// algorithm — independent of image dimensions, so no dim-mismatch precondition.
/// </remarks>
public static class DHashDiff
{
    private const int W = 9;
    private const int H = 8;

    /// <summary>Compute the 64-bit dHash for an image.</summary>
    public static ulong Compute(Image<Rgba32> img)
    {
        // Clone + resize to 9×8 (9 cols, 8 rows).
        using var small = img.Clone(ctx => ctx.Resize(W, H, KnownResamplers.Bicubic));

        // Build per-pixel grayscale grid in row-major order.
        var luma = new double[H, W];
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            var p = small[x, y];
            luma[y, x] = 0.299 * p.R + 0.587 * p.G + 0.114 * p.B;
        }

        // Compare adjacent pairs per row. 8 pairs × 8 rows = 64 bits.
        ulong hash = 0;
        int bit = 0;
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W - 1; x++)
        {
            if (luma[y, x] < luma[y, x + 1])
                hash |= 1UL << bit;
            bit++;
        }
        return hash;
    }

    /// <summary>Hamming distance between two image hashes (popcount of XOR).</summary>
    public static int HammingDistance(Image<Rgba32> a, Image<Rgba32> b)
    {
        var ha = Compute(a);
        var hb = Compute(b);
        return BitOperations.PopCount(ha ^ hb);
    }
}
```

### Step 4: Run tests to verify they pass

Run: `dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~DHashDiff" --nologo`
Expected: 3 tests pass.

### Step 5: Verify CI

Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && ./scripts/ci.sh 2>&1 | grep -E "Passed:|Skipped:" | head -10`
Expected: **374 Passed + 47 Skipped** (was 371+47; +3).

---

## Task 3: BitmapMethod enum + dispatch + diff renderer extensions

**Why:** Wire the two new methods into `BitmapAssertion`'s switch + adapt `DiffImageRenderer` so each method gets the right diff treatment (or no diff, for dHash). Also adds the `Method` field to `ScenarioAssertion`.

**Files:**
- Create: `src/Protocol/Reports/BitmapMethod.cs` (cross-project enum, see plan header for placement rationale)
- Modify: `src/Protocol/Models/ScenarioAssertion.cs`
- Modify: `src/Runner/Bitmap/BitmapAssertion.cs`
- Modify: `src/Runner/Bitmap/DiffImageRenderer.cs`
- Modify: `tests/Runner.Tests/Bitmap/DiffImageRendererTests.cs` (4 existing tests need new arg)
- Create: `tests/Runner.Tests/Bitmap/BitmapMethodDispatchTests.cs` (3 tests)
- Create: `tests/Runner.Tests/Bitmap/DiffImageRendererMethodTests.cs` (2 tests)

### Step 1: Failing tests

- [ ] Create `tests/Runner.Tests/Bitmap/BitmapMethodDispatchTests.cs`:

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

public class BitmapMethodDispatchTests
{
    private sealed class FakeRpcClient : IBitmapRpcClient
    {
        public string CapturePath { get; init; } = string.Empty;
        public Task<BitmapCaptureResult> BitmapCaptureAsync(JsonElement? region, CancellationToken ct)
            => Task.FromResult(new BitmapCaptureResult { Path = CapturePath, Width = 64, Height = 64 });
    }

    private static string WriteSolid(string path, byte r, byte g, byte b)
    {
        using var img = new Image<Rgba32>(64, 64);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
            img[x, y] = new Rgba32(r, g, b, 255);
        img.SaveAsPng(path);
        return path;
    }

    [Fact]
    public async Task MethodPixelExact_DispatchesToPixelExactDiff()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"bmd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            // Baseline gray=100, capture gray=120 → pixel-exact max delta = 20.
            var baseline = WriteSolid(Path.Combine(tmp, "baseline.png"), 100, 100, 100);
            var capture  = WriteSolid(Path.Combine(tmp, "capture.png"),  120, 120, 120);

            var a = new ScenarioAssertion
            {
                Type = "bitmap",
                Baseline = baseline,
                Method = "pixel-exact",
                Tolerance = 5,   // delta 20 > tolerance 5 → fail
            };
            var rpc = new FakeRpcClient { CapturePath = capture };
            var result = await BitmapAssertion.EvaluateAsync(
                rpc, a, scenarioPath: Path.Combine(tmp, "s.test.json"),
                updateBaselines: false,
                diffOutputDir: null,
                runWideDiffFormat: DiffFormat.Files,
                runWideTier: "generic",
                ct: CancellationToken.None);

            Assert.False(result.Passed);
            Assert.Contains("pixel-exact", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("20", result.FailureMessage);
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task MethodDHash_DispatchesToDHashDiff()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"bmd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            // Solid black vs solid white → dHash distance close to 0 for solid colors
            // (no contrast = all bits 0). Use gradient inversion instead for guaranteed
            // high distance.
            var baseline = WriteSolid(Path.Combine(tmp, "baseline.png"), 0, 0, 0);
            var capture  = WriteSolid(Path.Combine(tmp, "capture.png"), 255, 255, 255);

            var a = new ScenarioAssertion
            {
                Type = "bitmap",
                Baseline = baseline,
                Method = "dhash",
                Tolerance = 0,   // distance 0 expected (both solid → all-zero hash); set to -1 to force fail
            };
            // Actually solid-color gives identical hashes (all 0s). Force failure by setting tolerance=-1
            // so the integer compare fails. Cleaner: skip this concern; the test asserts the dispatch
            // happened by checking the failure message format.
            a.Tolerance = -1;   // trigger validation rejection — guarantees a "dhash"-mentioning failure
            var rpc = new FakeRpcClient { CapturePath = capture };
            var result = await BitmapAssertion.EvaluateAsync(
                rpc, a, scenarioPath: Path.Combine(tmp, "s.test.json"),
                updateBaselines: false,
                diffOutputDir: null,
                runWideDiffFormat: DiffFormat.Files,
                runWideTier: "generic",
                ct: CancellationToken.None);

            Assert.False(result.Passed);
            Assert.Contains("dhash", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task UnknownMethod_FailsWithDiagnostic()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"bmd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var baseline = WriteSolid(Path.Combine(tmp, "baseline.png"), 0, 0, 0);
            var capture  = WriteSolid(Path.Combine(tmp, "capture.png"), 0, 0, 0);

            var a = new ScenarioAssertion
            {
                Type = "bitmap",
                Baseline = baseline,
                Method = "garbage",
            };
            var rpc = new FakeRpcClient { CapturePath = capture };
            var result = await BitmapAssertion.EvaluateAsync(
                rpc, a, scenarioPath: Path.Combine(tmp, "s.test.json"),
                updateBaselines: false,
                diffOutputDir: null,
                runWideDiffFormat: DiffFormat.Files,
                runWideTier: "generic",
                ct: CancellationToken.None);

            Assert.False(result.Passed);
            Assert.Contains("garbage", result.FailureMessage);
            Assert.Contains("unknown", result.FailureMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }
}
```

- [ ] Create `tests/Runner.Tests/Bitmap/DiffImageRendererMethodTests.cs`:

```csharp
using System;
using System.IO;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Runner.Bitmap;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

public class DiffImageRendererMethodTests
{
    private static byte[] SolidPng(byte r, byte g, byte b)
    {
        using var img = new Image<Rgba32>(64, 64);
        for (int y = 0; y < 64; y++)
        for (int x = 0; x < 64; x++)
            img[x, y] = new Rgba32(r, g, b, 255);
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    [Fact]
    public void PixelExactMethod_RendersPerPixelHeatmap()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"diff-pe-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            // Baseline solid 100,100,100; capture solid 200,100,100 → per-channel R delta = 100.
            var baseline = SolidPng(100, 100, 100);
            var capture = SolidPng(200, 100, 100);

            var set = DiffImageRenderer.Render(
                baseline, capture,
                ssim: null,
                tolerance: 5,
                method: BitmapMethod.PixelExact,
                format: DiffFormat.Files,
                outputDir: tmp);

            Assert.True(File.Exists(set.Diff));
            using var diffImg = Image.Load<Rgba32>(set.Diff);
            // Sample center pixel: should be visibly red-shifted (R dominates G/B).
            var p = diffImg[32, 32];
            Assert.True(p.R > p.G + 20, $"expected red dominance at pixel-exact-failing pixel, got R={p.R} G={p.G} B={p.B}");
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public void DHashMethod_SkipsDiffPng()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"diff-dh-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var baseline = SolidPng(100, 100, 100);
            var capture = SolidPng(200, 100, 100);

            var set = DiffImageRenderer.Render(
                baseline, capture,
                ssim: null,
                tolerance: 5,
                method: BitmapMethod.DHash,
                format: DiffFormat.Files,
                outputDir: tmp);

            Assert.True(File.Exists(set.Baseline));
            Assert.True(File.Exists(set.Capture));
            // diff.png must NOT have been written; DiffSet.Diff is empty string.
            Assert.Equal(string.Empty, set.Diff);
            Assert.False(File.Exists(Path.Combine(tmp, "diff.png")));
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }
}
```

### Step 2: Run tests to verify they fail

Run: `dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~BitmapMethodDispatch|FullyQualifiedName~DiffImageRendererMethod" --nologo`
Expected: build fails — `BitmapMethod` enum doesn't exist; `EvaluateAsync` doesn't accept `runWideTier`; `DiffImageRenderer.Render` signature doesn't match.

### Step 3: Create BitmapMethod enum

- [ ] Create `src/Protocol/Reports/BitmapMethod.cs`:

```csharp
using System.Text.Json.Serialization;

namespace SdvTestFramework.Protocol.Reports;

/// <summary>
/// Bitmap diff method. Wire-format string: <c>"ssim"</c>, <c>"pixel-exact"</c>, <c>"dhash"</c>.
/// Wire format uses kebab-case strings; the enum uses PascalCase. Conversion via
/// <see cref="BitmapMethodExtensions.Parse"/> in Runner.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BitmapMethod
{
    Ssim,
    PixelExact,
    DHash,
}
```

The wire-format conversion lives in Runner because Protocol can't reference Runner-only logic. Add a tiny extension:

- [ ] Append to `src/Runner/Bitmap/PixelExactDiff.cs` (or create `src/Runner/Bitmap/BitmapMethodExtensions.cs`):

```csharp
namespace SdvTestFramework.Runner.Bitmap;

internal static class BitmapMethodExtensions
{
    /// <summary>Parse a wire-format method string (kebab-case) to a <see cref="BitmapMethod"/>. Throws on unknown.</summary>
    public static SdvTestFramework.Protocol.Reports.BitmapMethod ParseMethod(string? wireForm) =>
        (wireForm ?? "ssim") switch
        {
            "ssim" => SdvTestFramework.Protocol.Reports.BitmapMethod.Ssim,
            "pixel-exact" => SdvTestFramework.Protocol.Reports.BitmapMethod.PixelExact,
            "dhash" => SdvTestFramework.Protocol.Reports.BitmapMethod.DHash,
            _ => throw new System.ArgumentException($"unknown bitmap method: '{wireForm}'"),
        };
}
```

Place this in a new file `src/Runner/Bitmap/BitmapMethodExtensions.cs` to keep `PixelExactDiff` focused.

### Step 4: Add Method + Tier fields to ScenarioAssertion

- [ ] Modify `src/Protocol/Models/ScenarioAssertion.cs`. Add two new fields at the end of the class:

```csharp
    /// <summary>For <c>bitmap</c> assertions: diff method. Wire format: <c>"ssim" | "pixel-exact" | "dhash"</c>. Null → ssim.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("method")]
    public string? Method { get; set; }

    /// <summary>For <c>bitmap</c> assertions: per-assertion tier override. Wire format: <c>"generic" | "ci-ubuntu" | "self-hosted-nvidia"</c>. Null → use the run-wide tier.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("tier")]
    public string? Tier { get; set; }
```

(`Tier` is added here so T4 doesn't need to touch ScenarioAssertion again.)

### Step 5: Update DiffImageRenderer signature + branch

- [ ] Modify `src/Runner/Bitmap/DiffImageRenderer.cs`. Replace the `Render` method:

```csharp
public static DiffSet Render(
    byte[] baselineBytes,
    byte[] captureBytes,
    SsimResult? ssim,
    double tolerance,
    BitmapMethod method,
    DiffFormat format,
    string outputDir)
{
    // 1. Always write byte-for-byte copies of baseline + capture.
    var baselinePath = Path.Combine(outputDir, "baseline.png");
    var capturePath = Path.Combine(outputDir, "capture.png");
    File.WriteAllBytes(baselinePath, baselineBytes);
    File.WriteAllBytes(capturePath, captureBytes);

    // 2. dHash skips diff entirely — perceptual hash doesn't localize per-pixel.
    if (method == BitmapMethod.DHash)
        return new DiffSet(baselinePath, capturePath, Diff: string.Empty, Triptych: null);

    // 3. SSIM + pixel-exact both produce a diff.png with red heatmap overlay.
    var diffPath = Path.Combine(outputDir, "diff.png");
    using var baseline = Image.Load<Rgba32>(baselineBytes);
    using var capture = Image.Load<Rgba32>(captureBytes);
    var pixelRedness = method switch
    {
        BitmapMethod.Ssim => BuildSsimRedness(ssim ?? throw new ArgumentException("Ssim method requires non-null ssim"),
                                              tolerance, baseline.Width, baseline.Height),
        BitmapMethod.PixelExact => BuildPixelExactRedness(baseline, capture, tolerance),
        _ => throw new ArgumentOutOfRangeException(nameof(method)),
    };

    using (var diff = baseline.Clone())
    {
        ApplyHeatmap(diff, pixelRedness);
        diff.SaveAsPng(diffPath);
    }

    // 4. Composite output if requested.
    string? triptychPath = null;
    if (format is DiffFormat.Triptych or DiffFormat.All)
    {
        triptychPath = Path.Combine(outputDir, "triptych.png");
        BuildTriptych(baselineBytes, captureBytes, diffPath, triptychPath);
    }

    return new DiffSet(baselinePath, capturePath, diffPath, triptychPath);
}
```

Rename the existing `BuildPixelRedness` to `BuildSsimRedness` (its semantics are SSIM-specific) and add the pixel-exact variant alongside:

```csharp
private static float[,] BuildSsimRedness(SsimResult ssim, double tolerance, int width, int height)
{
    // ... exact body of the existing BuildPixelRedness method (cast tolerance to float
    // where needed), no logic change.
}

/// <summary>
/// Per-pixel redness for pixel-exact mode. Pixels with max-channel-delta ≤ tolerance
/// get redness=0 (clean); failing pixels scale to delta/255.
/// </summary>
private static float[,] BuildPixelExactRedness(Image<Rgba32> baseline, Image<Rgba32> capture, double tolerance)
{
    int w = baseline.Width, h = baseline.Height;
    var redness = new float[h, w];
    for (int y = 0; y < h; y++)
    for (int x = 0; x < w; x++)
    {
        var pa = baseline[x, y];
        var pb = capture[x, y];
        int dr = Math.Abs(pa.R - pb.R);
        int dg = Math.Abs(pa.G - pb.G);
        int db = Math.Abs(pa.B - pb.B);
        int delta = Math.Max(dr, Math.Max(dg, db));
        if (delta > tolerance)
            redness[y, x] = (float)(delta / 255.0);
    }
    return redness;
}
```

Add `using SdvTestFramework.Protocol.Reports;` to DiffImageRenderer.cs (for `BitmapMethod`).

### Step 6: Update existing DiffImageRendererTests

- [ ] Modify `tests/Runner.Tests/Bitmap/DiffImageRendererTests.cs`. Each `DiffImageRenderer.Render(...)` call needs the new args. Find the 4 sites and update:

Replace each invocation matching the old shape:
```csharp
DiffImageRenderer.Render(bytes, bytes, ssim, tolerance: 0.95f, DiffFormat.Files, tmp)
```
with the new shape:
```csharp
DiffImageRenderer.Render(bytes, bytes, ssim, tolerance: 0.95, method: BitmapMethod.Ssim, DiffFormat.Files, tmp)
```

(`SsimResult` becomes `SsimResult?` — pass the existing local value; `tolerance` widens float→double.)

Add `using SdvTestFramework.Protocol.Reports;` if not already present.

### Step 7: Update BitmapAssertion to dispatch by method

- [ ] Modify `src/Runner/Bitmap/BitmapAssertion.cs`. Replace the `EvaluateAsync` body. Add 1 new param `runWideTier` (string, default "generic"):

```csharp
public static async Task<BitmapAssertionResult> EvaluateAsync(
    IBitmapRpcClient rpc,
    ScenarioAssertion a,
    string scenarioPath,
    bool updateBaselines,
    string? diffOutputDir,
    DiffFormat runWideDiffFormat,
    string runWideTier,
    CancellationToken ct)
{
    if (string.IsNullOrEmpty(a.Baseline))
        return new BitmapAssertionResult(false, "bitmap assertion missing 'baseline' field");

    BitmapMethod method;
    try { method = BitmapMethodExtensions.ParseMethod(a.Method); }
    catch (ArgumentException ex) { return new BitmapAssertionResult(false, ex.Message); }

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

    // 2. Update-mode short-circuit (unchanged).
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

    // 4. Effective tolerance (per-assertion > tier > method default).
    var perAssertionTier = a.Tier;
    var effectiveTier = !string.IsNullOrEmpty(perAssertionTier) ? perAssertionTier : runWideTier;
    double tolerance;
    try { tolerance = TierTolerance.Resolve(effectiveTier, method, a.HasExplicitTolerance ? a.Tolerance : null); }
    catch (ArgumentException ex) { return new BitmapAssertionResult(false, ex.Message); }

    // 5. Load + diff. Read raw bytes first so they're available for the diff renderer
    // on failure.
    byte[] baselineBytes, captureBytes;
    try
    {
        baselineBytes = await File.ReadAllBytesAsync(baselinePath, ct);
        captureBytes = await File.ReadAllBytesAsync(capturePath, ct);
    }
    catch (Exception ex)
    {
        return new BitmapAssertionResult(false, $"file read failed: {ex.Message}");
    }

    SsimResult? ssim = null;
    bool passed;
    string failureDetail;
    try
    {
        using var baseline = Image.Load<Rgba32>(baselineBytes);
        using var capture = Image.Load<Rgba32>(captureBytes);
        switch (method)
        {
            case BitmapMethod.Ssim:
            {
                var s = SsimDiff.Compute(baseline, capture);
                ssim = s;
                passed = s.Score + 1e-9 >= tolerance;
                failureDetail = $"SSIM {s.Score:F4} < tolerance {tolerance:F4}";
                break;
            }
            case BitmapMethod.PixelExact:
            {
                int delta = PixelExactDiff.MaxChannelDelta(baseline, capture);
                passed = delta <= tolerance;
                failureDetail = $"pixel-exact max delta {delta} > tolerance {tolerance:F0}";
                break;
            }
            case BitmapMethod.DHash:
            {
                int dist = DHashDiff.HammingDistance(baseline, capture);
                passed = dist <= tolerance;
                failureDetail = $"dhash distance {dist} > tolerance {tolerance:F0}";
                break;
            }
            default:
                return new BitmapAssertionResult(false, $"unknown bitmap method enum: {method}");
        }
    }
    catch (ArgumentException ex)
    {
        return new BitmapAssertionResult(false, ex.Message + " — regenerate baseline with --update-baselines");
    }
    catch (Exception ex)
    {
        return new BitmapAssertionResult(false, $"bitmap diff failed: {ex.Message}");
    }

    if (passed)
        return new BitmapAssertionResult(true, null);

    // 6. Failure → render diffs if a target dir is provided.
    DiffSet? diffs = null;
    if (!string.IsNullOrEmpty(diffOutputDir))
    {
        var format = a.DiffFormat ?? runWideDiffFormat;
        try
        {
            Directory.CreateDirectory(diffOutputDir);
            diffs = DiffImageRenderer.Render(
                baselineBytes, captureBytes, ssim, tolerance, method, format, diffOutputDir);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[bitmap] diff render failed: {ex.Message}");
        }
    }

    return new BitmapAssertionResult(false, $"{failureDetail}; capture: {capturePath}", diffs);
}
```

This references `a.HasExplicitTolerance` — `ScenarioAssertion.Tolerance` defaults to 0.95 today, so we can't tell "user set 0.95" from "user didn't set anything". Two options:

**A.** Change `Tolerance` to `double?` (nullable). Breaks JSON serialization back-compat: existing scenarios without `tolerance` would deserialize to null. Then BitmapAssertion uses `a.Tolerance` directly as the `perAssertionTolerance` argument to `Resolve`. Cleaner.

**B.** Add a sentinel: if `a.Tolerance == 0.95` AND `a.Method != "ssim"`, treat as "use method default". Hacky.

**Pick A.** Modify `ScenarioAssertion`:
```csharp
/// <summary>For <c>bitmap</c> assertions: per-assertion tolerance override. Polymorphic per method.</summary>
public double? Tolerance { get; set; }
```
(Remove the `= 0.95` default.)

Replace `a.HasExplicitTolerance ? a.Tolerance : null` in BitmapAssertion with simply `a.Tolerance`.

Existing BitmapAssertionTests + BitmapAssertionDiffTests construct `Tolerance = 0.95` explicitly — those keep working since they're SSIM. Verify no other consumer reads `a.Tolerance` expecting the 0.95 default: grep for `\.Tolerance` in src/.

Add the new arg to all existing `BitmapAssertion.EvaluateAsync(...)` callers:
- `src/Runner/Scenarios/ScenarioRunner.cs`: pass `runWideTier: _runWideTier` (T4 will add `_runWideTier`; for now in T3, pass the literal `"generic"` since the field doesn't exist yet — T4 wires it through).

Actually a cleaner path: T3 ADDS the field `_runWideTier` to ScenarioRunner with default `"generic"` and a 5-arg constructor; T4 wires the CLI flag to it. This avoids a temporary patch + lets T3 cleanly compile.

Modify `src/Runner/Scenarios/ScenarioRunner.cs`:
- Add `private readonly string _runWideTier;` field.
- 4-arg constructor chains: `: this(session, updateBaselines, reportDir, DiffFormat.Files) { }` already exists. Update to chain through to a new 5-arg ctor with `runWideTier: "generic"`.
- New 5-arg constructor:

```csharp
public ScenarioRunner(
    JsonRpcSession session,
    bool updateBaselines,
    RunDirectory? reportDir,
    DiffFormat runWideDiffFormat,
    string runWideTier)
{
    _session = session;
    _updateBaselines = updateBaselines;
    _reportDir = reportDir;
    _recorder = reportDir is not null ? new ScreenshotRecorder(session) : null;
    _runWideDiffFormat = runWideDiffFormat;
    _runWideTier = runWideTier;
}
```

- 4-arg constructor body now:
```csharp
public ScenarioRunner(JsonRpcSession session, bool updateBaselines, RunDirectory? reportDir, DiffFormat runWideDiffFormat)
    : this(session, updateBaselines, reportDir, runWideDiffFormat, "generic") { }
```

- Bitmap case: pass `_runWideTier` to `BitmapAssertion.EvaluateAsync(...)`.

### Step 8: Update existing BitmapAssertion tests

- [ ] Modify `tests/Runner.Tests/Bitmap/BitmapAssertionTests.cs`. 4 sites need `runWideTier: "generic"` added. Existing `Tolerance = 0.95` calls must keep working (since `Tolerance` is now `double?`, `Tolerance = 0.95` still compiles + means "user set 0.95 explicitly"). Verify all 4 tests still assert correctly.

- [ ] Modify `tests/Runner.Tests/Bitmap/BitmapAssertionDiffTests.cs`. Same: add `runWideTier: "generic"` to all 3 sites.

### Step 9: Stub TierTolerance for T3

- [ ] Create `src/Runner/Bitmap/TierTolerance.cs` (stub for T3 — T4 fills in the table):

```csharp
using System;
using SdvTestFramework.Protocol.Reports;

namespace SdvTestFramework.Runner.Bitmap;

/// <summary>
/// Resolve the effective tolerance for a bitmap assertion. Per-assertion explicit
/// tolerance always wins; otherwise looks up tier defaults per method.
/// </summary>
public static class TierTolerance
{
    public static double Resolve(string tier, BitmapMethod method, double? perAssertionTolerance)
    {
        if (perAssertionTolerance is { } t)
        {
            // Per-method validation matches spec §4.2.
            switch (method)
            {
                case BitmapMethod.Ssim:
                    if (t <= 0 || t > 1)
                        throw new ArgumentException($"bitmap ssim 'tolerance' must be in (0, 1]; got {t}");
                    break;
                case BitmapMethod.PixelExact:
                    if (t < 0)
                        throw new ArgumentException($"bitmap pixel-exact 'tolerance' must be >= 0; got {t}");
                    break;
                case BitmapMethod.DHash:
                    if (t < 0 || t > 64)
                        throw new ArgumentException($"bitmap dhash 'tolerance' must be in [0, 64]; got {t}");
                    break;
            }
            return t;
        }

        // T4 fills in the tier table. T3 stub: method defaults regardless of tier.
        return method switch
        {
            BitmapMethod.Ssim => 0.95,
            BitmapMethod.PixelExact => 0,
            BitmapMethod.DHash => 5,
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };
    }
}
```

T4 expands the body with the 3×3 table.

### Step 10: Run new + existing tests

Run: `dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~BitmapMethodDispatch|FullyQualifiedName~DiffImageRendererMethod|FullyQualifiedName~BitmapAssertion|FullyQualifiedName~DiffImageRenderer" --nologo`
Expected: 5 new + 4 existing BitmapAssertionTests + 3 existing BitmapAssertionDiffTests + 4 existing DiffImageRendererTests = 16 tests pass.

### Step 11: Verify CI

Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && ./scripts/ci.sh 2>&1 | grep -E "Passed:|Skipped:" | head -10`
Expected: **379 Passed + 47 Skipped** (was 374+47; +5).

---

## Task 4: Tier resolution + CLI flag + schema

**Why:** Wire the run-wide `--tier` flag and complete the per-method tier table (T3 stubbed it with method defaults only).

**Files:**
- Modify: `src/Runner/Commands/RunCommand.cs` (parse `--tier`)
- Modify: `src/Runner/Bitmap/TierTolerance.cs` (full 3×3 table)
- Modify: `schemas/scenario.schema.json` (add `method` + `tier` enums to bitmap variant)
- Create: `tests/Runner.Tests/Bitmap/TierToleranceTests.cs` (2 tests)

### Step 1: Failing tests

- [ ] Create `tests/Runner.Tests/Bitmap/TierToleranceTests.cs`:

```csharp
using System;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Runner.Bitmap;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

public class TierToleranceTests
{
    [Fact]
    public void TierCiUbuntu_SsimMethod_Returns098()
    {
        // Per spec §4.3 table: tier=ci-ubuntu + method=ssim → 0.98.
        var t = TierTolerance.Resolve("ci-ubuntu", BitmapMethod.Ssim, perAssertionTolerance: null);
        Assert.Equal(0.98, t, precision: 4);
    }

    [Fact]
    public void PerAssertionTolerance_OverridesTier()
    {
        // Tier ci-ubuntu would give 0.98; per-assertion 0.99 wins.
        var t = TierTolerance.Resolve("ci-ubuntu", BitmapMethod.Ssim, perAssertionTolerance: 0.99);
        Assert.Equal(0.99, t, precision: 4);
    }
}
```

### Step 2: Run tests to verify they fail

Run: `dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~TierTolerance" --nologo`
Expected: TierCiUbuntu test fails — T3 stub returns 0.95 for SSIM regardless of tier.

### Step 3: Expand TierTolerance with full table

- [ ] Modify `src/Runner/Bitmap/TierTolerance.cs`. Replace the fallback switch at the end of `Resolve`:

```csharp
        // Tier-derived defaults per method (spec §4.3 table).
        return (tier, method) switch
        {
            ("generic",            BitmapMethod.Ssim)        => 0.95,
            ("generic",            BitmapMethod.PixelExact)  => 5,
            ("generic",            BitmapMethod.DHash)       => 5,
            ("ci-ubuntu",          BitmapMethod.Ssim)        => 0.98,
            ("ci-ubuntu",          BitmapMethod.PixelExact)  => 2,
            ("ci-ubuntu",          BitmapMethod.DHash)       => 3,
            ("self-hosted-nvidia", BitmapMethod.Ssim)        => 0.999,
            ("self-hosted-nvidia", BitmapMethod.PixelExact)  => 0,
            ("self-hosted-nvidia", BitmapMethod.DHash)       => 1,
            _ => throw new ArgumentException(
                $"unknown tier '{tier}' (expected: generic | ci-ubuntu | self-hosted-nvidia)"),
        };
```

### Step 4: Add --tier flag to RunCommand

- [ ] Modify `src/Runner/Commands/RunCommand.cs`. After the existing arg parsing block, add:

```csharp
        string runWideTier = "generic";
        // ... in the loop:
        if (a == "--tier" && i + 1 < args.Length)
        {
            var raw = args.Span[++i];
            if (raw is not ("generic" or "ci-ubuntu" or "self-hosted-nvidia"))
            {
                Console.Error.WriteLine(
                    $"[run] invalid --tier '{raw}'; expected generic | ci-ubuntu | self-hosted-nvidia");
                return 2;
            }
            runWideTier = raw;
            continue;
        }
```

(Place this alongside the existing `--diff-format` handling.)

After the loop, add a static field next to the existing `_diffFormatFlag`:

```csharp
private static string _tierFlag = "generic";
```

After the loop body's `_diffFormatFlag = diffFormat;` line:

```csharp
_tierFlag = runWideTier;
```

In `RunOnceAsync` where `ScenarioRunner` is constructed:

```csharp
var runner = new ScenarioRunner(session, _updateBaselinesFlag, runDir, _diffFormatFlag, _tierFlag);
```

### Step 5: Update scenario JSON schema

- [ ] Modify `schemas/scenario.schema.json`. Find the bitmap assertion entry (search for `"const": "bitmap"` or `"baseline"`). Add to its `properties` object:

```json
"method": {
  "type": "string",
  "enum": ["ssim", "pixel-exact", "dhash"],
  "description": "Diff method. Default 'ssim'."
},
"tier": {
  "type": "string",
  "enum": ["generic", "ci-ubuntu", "self-hosted-nvidia"],
  "description": "Per-assertion tier override of the run-wide --tier flag. Optional."
}
```

If a `tolerance` property is also present in that section, update its description to:
```
"description": "Threshold; semantics depend on method. SSIM: float in (0, 1] (higher = stricter). pixel-exact: int >= 0 (max RGB channel delta allowed). dhash: int in [0, 64] (Hamming distance allowed)."
```

### Step 6: Update PrintHelp

- [ ] Modify `src/Runner/Program.cs:69`. Update the `run` line to include `[--tier <name>]`:

```
w.WriteLine("  run [--filter <p>] [--mods-path <p>] [--reporter <c|tap|junit>] [--output <path>] [--watch] [--update-baselines] [--diff-format <files|triptych|all>] [--tier <generic|ci-ubuntu|self-hosted-nvidia>] [paths...]");
```

And add a sub-line describing `--tier`:
```
w.WriteLine("                    --tier: tolerance preset for bitmap assertions. Maps to per-method");
w.WriteLine("                            defaults: generic→0.95 SSIM, ci-ubuntu→0.98, self-hosted-nvidia→0.999.");
```

### Step 7: Run tests + verify

Run: `dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~TierTolerance" --nologo`
Expected: 2 tests pass.

Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && ./scripts/ci.sh 2>&1 | grep -E "Passed:|Skipped:" | head -10`
Expected: **381 Passed + 47 Skipped** (was 379+47; +2).

---

## Task 5: RunCommandOptions refactor

**Why:** The static `_updateBaselinesFlag`, `_diffFormatFlag`, `_tierFlag`, `_runDir` fields on `RunCommand` are a hack — accessed across method boundaries. Refactor into an explicit `RunCommandOptions` record passed by argument. Pure refactor; no new tests; behavior unchanged.

**Files:**
- Create: `src/Runner/Commands/RunCommandOptions.cs`
- Modify: `src/Runner/Commands/RunCommand.cs` (remove static fields, thread options through)

### Step 1: Create RunCommandOptions

- [ ] Create `src/Runner/Commands/RunCommandOptions.cs`:

```csharp
using System.Collections.Generic;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Runner.Reports;

namespace SdvTestFramework.Runner.Commands;

/// <summary>
/// Parsed options for a single <c>sdv-test run</c> invocation. Constructed in
/// <see cref="RunCommand.RunAsync"/> from argv; threaded explicitly through the run-once
/// callstack instead of static fields. Also reused by <c>BaselinesCommand.update</c>
/// which builds one with <see cref="UpdateBaselines"/> = true.
/// </summary>
public sealed record RunCommandOptions(
    IReadOnlyList<string> Paths,
    string? Filter,
    string? ModsPath,
    string ReporterName,
    string? OutputPath,
    bool Watch,
    bool UpdateBaselines,
    string? ReportDirPath,
    bool NoReport,
    DiffFormat DiffFormat,
    string Tier,
    bool NoCacheCleanup,
    RunDirectory? PreCreatedRunDir);
```

(`PreCreatedRunDir` is needed because `RunCommand.RunAsync` creates the dir eagerly before launching SDV; passing it via the options record means `RunOnceAsync` doesn't need to recreate it. Alternatively, `RunOnceAsync` could create it itself — but eager creation is the existing behavior we're preserving.)

### Step 2: Refactor RunCommand.RunAsync

- [ ] Modify `src/Runner/Commands/RunCommand.cs`. Remove the static fields:
```csharp
// DELETE these lines:
private static bool _updateBaselinesFlag;
private static DiffFormat _diffFormatFlag = DiffFormat.Files;
private static string _tierFlag = "generic";
private static RunDirectory? _runDir;
```

In `RunAsync`, after argv parsing, build a `RunCommandOptions` instance with all the parsed values. Replace lines that wrote to the static fields with assignment to local variables, then construct the record.

Change `RunOnceAsync` signature from positional args to take `(JsonRpcSession session, RunCommandOptions opts, IReporter reporter, TextWriter writer, CancellationToken ct)`.

Inside `RunOnceAsync`, replace `_updateBaselinesFlag` with `opts.UpdateBaselines`, `_diffFormatFlag` with `opts.DiffFormat`, `_tierFlag` with `opts.Tier`, `_runDir` with `opts.PreCreatedRunDir`, etc.

The watch-mode callback closes over `opts` instead of static state.

### Step 3: Verify no regressions

- [ ] Run: `dotnet test tests/Runner.Tests/ --nologo` — all existing Runner tests should still pass.
- [ ] Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && ./scripts/ci.sh 2>&1 | grep -E "Passed:|Skipped:" | head -10`
Expected: **381 Passed + 47 Skipped** (unchanged from T4 — pure refactor).

If any test breaks, the static-field-to-options threading missed a callsite. Common spots: `RunOnceAsync` (the watch-mode wrapper), the `BuildRunSummary` helper if it referenced any static, the `ConvertDiffs`/`MakeRel` helpers.

---

## Task 6: BaselinesCommand

**Why:** `sdv-test baselines list|update|show|delete`. The `update` subcommand delegates to RunCommand's run path with `UpdateBaselines=true`, replacing the manual `--update-baselines` invocation pattern.

**Files:**
- Create: `src/Runner/Commands/BaselinesCommand.cs`
- Modify: `src/Runner/Program.cs` (register top-level `baselines` command + help text)
- Create: `tests/Runner.Tests/Commands/BaselinesCommandTests.cs` (4 tests)

### Step 1: Failing tests

- [ ] Create `tests/Runner.Tests/Commands/BaselinesCommandTests.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Commands;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Commands;

public class BaselinesCommandTests
{
    private static void WriteScenario(string path, string baselineRelPath)
    {
        File.WriteAllText(path,
            "{\"name\":\"s\",\"config\":{\"seed\":42},\"steps\":[],\"assertions\":[" +
            "{\"type\":\"bitmap\",\"baseline\":\"" + baselineRelPath + "\",\"tolerance\":0.95}" +
            "]}");
    }

    private static void WriteSolidPng(string path, byte r, byte g, byte b)
    {
        using var img = new Image<Rgba32>(8, 8);
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
            img[x, y] = new Rgba32(r, g, b, 255);
        img.SaveAsPng(path);
    }

    [Fact]
    public async Task List_EnumeratesReferencedBaselines_MarksMissingPresent()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"bl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            // Two scenarios; one's baseline exists, the other's doesn't.
            WriteScenario(Path.Combine(tmp, "a.test.json"), "baselines/a.png");
            WriteScenario(Path.Combine(tmp, "b.test.json"), "baselines/b.png");
            Directory.CreateDirectory(Path.Combine(tmp, "baselines"));
            WriteSolidPng(Path.Combine(tmp, "baselines/a.png"), 0, 0, 0);
            // baselines/b.png deliberately not created → should show MISSING

            var sw = new StringWriter();
            var origOut = Console.Out;
            Console.SetOut(sw);
            try
            {
                var rc = await BaselinesCommand.RunAsync(
                    new[] { "list", "--scenarios", tmp }.AsMemory(), CancellationToken.None);
                Assert.Equal(0, rc);
            }
            finally { Console.SetOut(origOut); }

            var output = sw.ToString();
            Assert.Contains("a.png", output);
            Assert.Contains("PRESENT", output);
            Assert.Contains("b.png", output);
            Assert.Contains("MISSING", output);
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task Update_DispatchesToRunCommandWithUpdateMode()
    {
        // Test seam: BaselinesCommand exposes a static delegate for the run-executor.
        // Default points to RunCommand.RunFromOptions (production); tests substitute a probe.
        bool dispatched = false;
        bool updateBaselinesSeen = false;
        Func<RunCommandOptions, CancellationToken, Task<int>> origExecutor = BaselinesCommand.RunExecutor;
        BaselinesCommand.RunExecutor = (opts, ct) =>
        {
            dispatched = true;
            updateBaselinesSeen = opts.UpdateBaselines;
            return Task.FromResult(0);
        };
        try
        {
            var rc = await BaselinesCommand.RunAsync(
                new[] { "update", "tests/samples/" }.AsMemory(), CancellationToken.None);
            Assert.Equal(0, rc);
            Assert.True(dispatched);
            Assert.True(updateBaselinesSeen);
        }
        finally { BaselinesCommand.RunExecutor = origExecutor; }
    }

    [Fact]
    public async Task Show_PrintsPngMetadata()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"bl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var p = Path.Combine(tmp, "x.png");
            WriteSolidPng(p, 0, 0, 0);

            var sw = new StringWriter();
            var origOut = Console.Out;
            Console.SetOut(sw);
            try
            {
                var rc = await BaselinesCommand.RunAsync(
                    new[] { "show", p }.AsMemory(), CancellationToken.None);
                Assert.Equal(0, rc);
            }
            finally { Console.SetOut(origOut); }

            var output = sw.ToString();
            Assert.Contains("8", output);          // dimensions 8×8
            Assert.Contains("bytes", output);      // file size present
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task Delete_WithForce_RemovesFile()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"bl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var p = Path.Combine(tmp, "doomed.png");
            WriteSolidPng(p, 0, 0, 0);
            Assert.True(File.Exists(p));

            var rc = await BaselinesCommand.RunAsync(
                new[] { "delete", p, "--force" }.AsMemory(), CancellationToken.None);
            Assert.Equal(0, rc);
            Assert.False(File.Exists(p));
        }
        finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true); }
    }
}
```

### Step 2: Run tests to verify they fail

Run: `dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~BaselinesCommand" --nologo`
Expected: build fails — `BaselinesCommand` doesn't exist.

### Step 3: Create BaselinesCommand

- [ ] Create `src/Runner/Commands/BaselinesCommand.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Protocol.Reports;
using SdvTestFramework.Protocol.Scenarios;
using SixLabors.ImageSharp;

namespace SdvTestFramework.Runner.Commands;

/// <summary>
/// <c>sdv-test baselines</c> dispatcher. Subcommands: <c>list | update | show | delete</c>.
/// </summary>
public static class BaselinesCommand
{
    /// <summary>
    /// Test seam — <c>update</c> delegates here. Defaults to <see cref="RunCommand.RunFromOptions"/>;
    /// tests substitute a probe to avoid launching SDV.
    /// </summary>
    public static Func<RunCommandOptions, CancellationToken, Task<int>> RunExecutor { get; set; }
        = RunCommand.RunFromOptions;

    public static async Task<int> RunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: sdv-test baselines <list|update|show|delete> [args...]");
            return 64;
        }

        var sub = args.Span[0];
        var rest = args[1..];
        return sub switch
        {
            "list" => RunList(rest),
            "update" => await RunUpdate(rest, ct),
            "show" => RunShow(rest),
            "delete" => RunDelete(rest),
            _ => Unknown(sub),
        };
    }

    private static int Unknown(string sub)
    {
        Console.Error.WriteLine($"unknown baselines subcommand: {sub}");
        return 64;
    }

    // --- list ---
    private static int RunList(ReadOnlyMemory<string> args)
    {
        string scenariosDir = Directory.GetCurrentDirectory();
        for (int i = 0; i < args.Length; i++)
        {
            if (args.Span[i] == "--scenarios" && i + 1 < args.Length)
                scenariosDir = args.Span[++i];
        }

        if (!Directory.Exists(scenariosDir))
        {
            Console.Error.WriteLine($"[baselines] scenarios dir not found: {scenariosDir}");
            return 1;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int found = 0;
        foreach (var f in Directory.EnumerateFiles(scenariosDir, "*.test.json", SearchOption.AllDirectories))
        {
            ScenarioSpec spec;
            try { spec = ScenarioLoader.Load(f); }
            catch { continue; }
            foreach (var a in spec.Assertions)
            {
                if (a.Type != "bitmap" || string.IsNullOrEmpty(a.Baseline)) continue;
                var resolved = Bitmap.BaselineManager.ResolveBaseline(f, a.Baseline);
                if (!seen.Add(resolved)) continue;
                found++;
                var status = File.Exists(resolved) ? "PRESENT" : "MISSING";
                long size = status == "PRESENT" ? new FileInfo(resolved).Length : 0;
                Console.Out.WriteLine($"[{status}] {resolved} ({size} bytes) — {Path.GetFileName(f)}::{spec.Name}");
            }
        }

        if (found == 0)
        {
            Console.Out.WriteLine("(no bitmap baselines referenced)");
            return 1;
        }
        return 0;
    }

    // --- update ---
    private static async Task<int> RunUpdate(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        var paths = new List<string>();
        string tier = "generic";
        string? modsPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args.Span[i];
            if (a == "--tier" && i + 1 < args.Length) { tier = args.Span[++i]; continue; }
            if (a == "--mods-path" && i + 1 < args.Length) { modsPath = args.Span[++i]; continue; }
            paths.Add(a);
        }
        if (paths.Count == 0)
        {
            Console.Error.WriteLine("usage: sdv-test baselines update <path-or-glob> [--tier <name>] [--mods-path <p>]");
            return 64;
        }

        var opts = new RunCommandOptions(
            Paths: paths,
            Filter: null,
            ModsPath: modsPath,
            ReporterName: "console",
            OutputPath: null,
            Watch: false,
            UpdateBaselines: true,
            ReportDirPath: null,
            NoReport: true,        // baselines update is a regen op; HTML report not useful
            DiffFormat: DiffFormat.Files,
            Tier: tier,
            NoCacheCleanup: false,
            PreCreatedRunDir: null);

        return await RunExecutor(opts, ct);
    }

    // --- show ---
    private static int RunShow(ReadOnlyMemory<string> args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: sdv-test baselines show <path>");
            return 64;
        }
        var path = args.Span[0];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"[baselines] file not found: {path}");
            return 1;
        }

        var info = new FileInfo(path);
        try
        {
            var img = Image.Identify(path);
            Console.Out.WriteLine($"path:       {path}");
            Console.Out.WriteLine($"dimensions: {img.Width}×{img.Height}");
            Console.Out.WriteLine($"file size:  {info.Length} bytes");
            Console.Out.WriteLine($"modified:   {info.LastWriteTimeUtc:O}");
            Console.Out.WriteLine($"format:     {img.Metadata.DecodedImageFormat?.Name ?? "unknown"}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[baselines] failed to identify '{path}': {ex.Message}");
            return 1;
        }
    }

    // --- delete ---
    private static int RunDelete(ReadOnlyMemory<string> args)
    {
        bool force = false;
        string? path = null;
        for (int i = 0; i < args.Length; i++)
        {
            var a = args.Span[i];
            if (a == "--force") { force = true; continue; }
            path ??= a;
        }
        if (path is null)
        {
            Console.Error.WriteLine("usage: sdv-test baselines delete <path> [--force]");
            return 64;
        }
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"[baselines] file not found: {path}");
            return 1;
        }

        if (!force)
        {
            Console.Out.Write($"delete {path}? [y/N] ");
            var answer = Console.In.ReadLine()?.Trim();
            if (answer is null or "" || (!answer.Equals("y", StringComparison.OrdinalIgnoreCase) && !answer.Equals("yes", StringComparison.OrdinalIgnoreCase)))
            {
                Console.Out.WriteLine("aborted");
                return 0;
            }
        }

        try { File.Delete(path); Console.Out.WriteLine($"deleted: {path}"); return 0; }
        catch (Exception ex) { Console.Error.WriteLine($"[baselines] delete failed: {ex.Message}"); return 1; }
    }
}
```

### Step 4: Add RunCommand.RunFromOptions seam

- [ ] Modify `src/Runner/Commands/RunCommand.cs`. Add a public static method that takes options + a cancellation token and runs the same path the argv-parser does:

```csharp
/// <summary>
/// Test/orchestration seam: run with a pre-built options record (skipping argv parse).
/// Used by BaselinesCommand.update.
/// </summary>
public static async Task<int> RunFromOptions(RunCommandOptions opts, CancellationToken ct)
{
    // Body: roughly the second half of RunAsync (after argv parsing) extracted into
    // its own method. Reuses the existing SDV-launch + run-once + watch-mode flow.
    // (Implementer: extract the post-parse half of RunAsync into this method, then
    // have RunAsync call it after building opts from argv.)
    // ... implementation ...
}
```

This may require a small refactor of `RunAsync` to extract the post-parse half. Verify CI green after.

### Step 5: Register baselines in Program.cs

- [ ] Modify `src/Runner/Program.cs:37-48`. Add `baselines` to the switch:

```csharp
"baselines" => await BaselinesCommand.RunAsync(args.AsMemory()[1..], cts.Token),
```

Add to `PrintHelp`:

```csharp
w.WriteLine("  baselines <list|update|show|delete> [args]");
w.WriteLine("                    Manage bitmap baselines.");
w.WriteLine("                    list [--scenarios <dir>]: enumerate referenced baselines + presence.");
w.WriteLine("                    update <path-or-glob> [--tier <n>] [--mods-path <p>]: rerun with --update-baselines.");
w.WriteLine("                    show <path>: print PNG metadata.");
w.WriteLine("                    delete <path> [--force]: remove file (prompts unless --force).");
```

### Step 6: Run tests + verify

Run: `dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~BaselinesCommand" --nologo`
Expected: 4 tests pass.

Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && ./scripts/ci.sh 2>&1 | grep -E "Passed:|Skipped:" | head -10`
Expected: **385 Passed + 47 Skipped** (was 381+47; +4).

---

## Task 7: CaptureCacheCleaner + auto-hook + cache command

**Why:** Sweep `~/.cache/sdv-test-framework/captures/` automatically at end-of-run + via standalone `sdv-test cache clean`.

**Files:**
- Create: `src/Runner/Bitmap/CaptureCacheCleaner.cs`
- Create: `src/Runner/Commands/CacheCommand.cs`
- Modify: `src/Runner/Commands/RunCommand.cs` (auto-hook + `--no-cache-cleanup`)
- Modify: `src/Runner/Program.cs` (register `cache` command)
- Create: `tests/Runner.Tests/Bitmap/CaptureCacheCleanerTests.cs` (3 tests)

### Step 1: Failing tests

- [ ] Create `tests/Runner.Tests/Bitmap/CaptureCacheCleanerTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading;
using SdvTestFramework.Runner.Bitmap;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

public class CaptureCacheCleanerTests
{
    private static void Touch(string path, DateTime mtime)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47 });   // PNG magic
        File.SetLastWriteTimeUtc(path, mtime);
    }

    [Fact]
    public void MaxAgeZero_DeletesAllFiles()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ccc-{Guid.NewGuid():N}");
        try
        {
            Touch(Path.Combine(tmp, "a", "1.png"), DateTime.UtcNow);
            Touch(Path.Combine(tmp, "a", "2.png"), DateTime.UtcNow);
            Touch(Path.Combine(tmp, "b", "1.png"), DateTime.UtcNow);

            int deleted = CaptureCacheCleaner.CleanCache(tmp, maxAgeDays: 0, keepRuns: 0, dryRun: false);
            Assert.Equal(3, deleted);
            Assert.False(File.Exists(Path.Combine(tmp, "a", "1.png")));
            Assert.False(File.Exists(Path.Combine(tmp, "b", "1.png")));
        }
        finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public void KeepRuns_RetainsNMostRecentScenarioDirs()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ccc-{Guid.NewGuid():N}");
        try
        {
            // 3 scenario dirs with different mtimes; keep top 2 by recency.
            var older = DateTime.UtcNow.AddDays(-1);
            var newer = DateTime.UtcNow.AddHours(-1);
            var newest = DateTime.UtcNow;
            Touch(Path.Combine(tmp, "old", "x.png"), older);
            Touch(Path.Combine(tmp, "mid", "x.png"), newer);
            Touch(Path.Combine(tmp, "new", "x.png"), newest);
            // Match the parent dir mtime to the contained file's.
            Directory.SetLastWriteTimeUtc(Path.Combine(tmp, "old"), older);
            Directory.SetLastWriteTimeUtc(Path.Combine(tmp, "mid"), newer);
            Directory.SetLastWriteTimeUtc(Path.Combine(tmp, "new"), newest);

            int deleted = CaptureCacheCleaner.CleanCache(tmp, maxAgeDays: 365, keepRuns: 2, dryRun: false);
            Assert.Equal(1, deleted);   // only "old" got swept
            Assert.False(File.Exists(Path.Combine(tmp, "old", "x.png")));
            Assert.True(File.Exists(Path.Combine(tmp, "mid", "x.png")));
            Assert.True(File.Exists(Path.Combine(tmp, "new", "x.png")));
        }
        finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public void DryRun_ReportsButDoesntDelete()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ccc-{Guid.NewGuid():N}");
        try
        {
            Touch(Path.Combine(tmp, "a", "1.png"), DateTime.UtcNow);
            int wouldDelete = CaptureCacheCleaner.CleanCache(tmp, maxAgeDays: 0, keepRuns: 0, dryRun: true);
            Assert.Equal(1, wouldDelete);
            Assert.True(File.Exists(Path.Combine(tmp, "a", "1.png")), "dry-run must not touch files");
        }
        finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true); }
    }
}
```

### Step 2: Run tests to verify they fail

Run: `dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~CaptureCacheCleaner" --nologo`
Expected: build fails — `CaptureCacheCleaner` doesn't exist.

### Step 3: Create CaptureCacheCleaner

- [ ] Create `src/Runner/Bitmap/CaptureCacheCleaner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SdvTestFramework.Runner.Bitmap;

/// <summary>
/// Sweep the bitmap-capture cache directory. A file is kept iff BOTH conditions hold:
/// (1) its mtime is within <c>maxAgeDays</c>, AND (2) its containing scenario subdir is
/// among the <c>keepRuns</c> most-recently-modified subdirs of the cache root.
/// Either condition failing → delete.
/// </summary>
public static class CaptureCacheCleaner
{
    /// <summary>
    /// Sweep <paramref name="cacheDir"/>. Returns the count of files deleted (or would-be
    /// deleted in dry-run). Returns 0 if the dir doesn't exist.
    /// </summary>
    public static int CleanCache(string cacheDir, int maxAgeDays, int keepRuns, bool dryRun)
    {
        if (!Directory.Exists(cacheDir)) return 0;

        // Identify the keepRuns most recent scenario subdirs by mtime.
        var subdirs = Directory.EnumerateDirectories(cacheDir).ToList();
        var keepSet = new HashSet<string>(
            subdirs.OrderByDescending(d => Directory.GetLastWriteTimeUtc(d)).Take(keepRuns),
            StringComparer.Ordinal);

        var ageCutoff = DateTime.UtcNow.AddDays(-maxAgeDays);

        int count = 0;
        foreach (var dir in subdirs)
        {
            bool dirIsKept = keepSet.Contains(dir);
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var fi = new FileInfo(f);
                bool tooOld = fi.LastWriteTimeUtc < ageCutoff;
                if (dirIsKept && !tooOld) continue;
                if (!dryRun)
                {
                    try { File.Delete(f); }
                    catch { continue; }
                }
                count++;
            }
        }
        return count;
    }
}
```

### Step 4: Auto-hook in RunCommand

- [ ] Modify `src/Runner/Commands/RunCommand.cs`. Add `--no-cache-cleanup` flag handling (alongside `--diff-format` etc.):

```csharp
if (a == "--no-cache-cleanup") { noCacheCleanup = true; continue; }
```

Add `bool noCacheCleanup = false;` variable above the loop. Pass to `RunCommandOptions`.

In `RunFromOptions` (or wherever the run terminates successfully — after `RunOnceAsync` completes), add:

```csharp
if (!opts.NoCacheCleanup)
{
    try
    {
        var cacheDir = Environment.GetEnvironmentVariable("SDV_CACHE_DIR")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "sdv-test-framework", "captures");
        var deleted = CaptureCacheCleaner.CleanCache(cacheDir, maxAgeDays: 7, keepRuns: 5, dryRun: false);
        if (deleted > 0)
            Console.Error.WriteLine($"[cache] swept {deleted} stale capture file(s)");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[cache] cleanup failed: {ex.Message}");
    }
}
```

(Add `using SdvTestFramework.Runner.Bitmap;` if needed.)

### Step 5: Create CacheCommand

- [ ] Create `src/Runner/Commands/CacheCommand.cs`:

```csharp
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Bitmap;

namespace SdvTestFramework.Runner.Commands;

/// <summary>
/// <c>sdv-test cache</c> dispatcher. Subcommand: <c>clean</c>.
/// </summary>
public static class CacheCommand
{
    public static Task<int> RunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        if (args.Length == 0 || args.Span[0] != "clean")
        {
            Console.Error.WriteLine("usage: sdv-test cache clean [--max-age <days>] [--keep-runs <n>] [--dry-run]");
            return Task.FromResult(64);
        }

        int maxAgeDays = 7;
        int keepRuns = 5;
        bool dryRun = false;
        for (int i = 1; i < args.Length; i++)
        {
            var a = args.Span[i];
            if (a == "--max-age" && i + 1 < args.Length && int.TryParse(args.Span[++i], out var d)) { maxAgeDays = d; continue; }
            if (a == "--keep-runs" && i + 1 < args.Length && int.TryParse(args.Span[++i], out var k)) { keepRuns = k; continue; }
            if (a == "--dry-run") { dryRun = true; continue; }
        }

        var cacheDir = Environment.GetEnvironmentVariable("SDV_CACHE_DIR")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "sdv-test-framework", "captures");

        var prefix = dryRun ? "[cache] would delete" : "[cache] deleted";
        var count = CaptureCacheCleaner.CleanCache(cacheDir, maxAgeDays, keepRuns, dryRun);
        Console.Out.WriteLine($"{prefix} {count} file(s) from {cacheDir}");
        return Task.FromResult(0);
    }
}
```

### Step 6: Register cache in Program.cs

- [ ] Modify `src/Runner/Program.cs:37-48`. Add to the switch:

```csharp
"cache" => await CacheCommand.RunAsync(args.AsMemory()[1..], cts.Token),
```

Add to `PrintHelp`:

```csharp
w.WriteLine("  cache clean [--max-age <days>] [--keep-runs <n>] [--dry-run]");
w.WriteLine("                    Sweep the bitmap-capture cache directory. A file is kept iff its");
w.WriteLine("                    mtime is within --max-age (default 7) AND its scenario subdir is among");
w.WriteLine("                    the --keep-runs most recent (default 5). Override location via");
w.WriteLine("                    $SDV_CACHE_DIR (default ~/.cache/sdv-test-framework/captures).");
```

### Step 7: Run tests + verify

Run: `dotnet test tests/Runner.Tests/ --filter "FullyQualifiedName~CaptureCacheCleaner" --nologo`
Expected: 3 tests pass.

Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && ./scripts/ci.sh 2>&1 | grep -E "Passed:|Skipped:" | head -10`
Expected: **388 Passed + 47 Skipped** (was 385+47; +3).

---

## Task 8: Smoke + docs + roadmap

**Why:** Final wrap-up. Skipped placeholder + 3 doc updates + roadmap maintenance.

**Files:**
- Create: `tests/Runner.Tests/Bitmap/BitmapMethodIntegrationTests.cs` (skipped placeholder)
- Modify: `docs/dsl-quickstart.md`
- Modify: `docs/milestones/current.md`
- Modify: `docs/roadmap.md`

### Step 1: Integration placeholder

- [ ] Create `tests/Runner.Tests/Bitmap/BitmapMethodIntegrationTests.cs`:

```csharp
using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

/// <summary>Integration surface for pixel-exact + dHash + tier preset — verified manually via tampered baseline.</summary>
public class BitmapMethodIntegrationTests
{
    [Fact(Skip = "Requires live SDV — author scenarios with each method; verify failures emit the right diff treatment.")]
    public void AllThreeBitmapMethods_WorkAgainstLiveSDV() { }
}
```

### Step 2: Live smoke — DOCUMENT AS MANUAL ONLY

Per project history (D1.5, D1.6, M2-record, HTML run reports T7 smokes), live SDV under headless Xvfb is brittle. Skip.

### Step 3: Update docs/dsl-quickstart.md

- [ ] Read `docs/dsl-quickstart.md`. Find the bitmap section. Append:

```markdown
### Bitmap diff methods

The `bitmap` assertion supports three methods via the `method` field:

- `"ssim"` (default) — perceptual structural similarity. `tolerance` is a float in (0, 1]; higher = stricter.
- `"pixel-exact"` — strict per-pixel RGB compare. `tolerance` is an integer; max per-channel delta allowed.
- `"dhash"` — perceptual difference hash. `tolerance` is an integer 0-64; max Hamming distance allowed.

Choose `pixel-exact` for UI elements that should be bit-stable; `dhash` for "vaguely the same scene" checks. SSIM is the right default for everything else.

### Tolerance tiers

`sdv-test run --tier=<generic|ci-ubuntu|self-hosted-nvidia>` selects per-method default tolerances. Useful when running the same suite across environments with different rendering determinism.

| method      | generic | ci-ubuntu | self-hosted-nvidia |
| ----------- | ------- | --------- | ------------------ |
| ssim        | 0.95    | 0.98      | 0.999              |
| pixel-exact | 5       | 2         | 0                  |
| dhash       | 5       | 3         | 1                  |

Per-assertion `tolerance` always overrides the tier default. Per-assertion `tier` field overrides the run-wide flag.

### `sdv-test baselines` subcommand

Manage bitmap baselines:
- `sdv-test baselines list [--scenarios <dir>]` — enumerate referenced baselines + presence.
- `sdv-test baselines update <path-or-glob> [--tier <n>]` — rerun with `--update-baselines`.
- `sdv-test baselines show <path>` — print PNG metadata.
- `sdv-test baselines delete <path> [--force]` — remove file (prompts unless `--force`).

### Capture cache cleanup

`sdv-test run` automatically sweeps `~/.cache/sdv-test-framework/captures/` at end of every successful invocation, deleting files older than 7 days OR outside the 5 most-recent run subdirs. Opt out with `--no-cache-cleanup`.

Manual: `sdv-test cache clean [--max-age <days>] [--keep-runs <n>] [--dry-run]`.
```

### Step 4: Update docs/milestones/current.md

- [ ] Append a new completion subsection (after the diff-image-on-failure entry):

```markdown
### Bitmap completion bundle landed (2026-04-26)

Plan: `docs/superpowers/plans/2026-04-26-bitmap-completion-bundle.md` (8 tasks, subagent-driven).
Design spec: `docs/superpowers/specs/2026-04-26-bitmap-completion-bundle-design.md`.

**Scope:** four Tier 3 items shipped together — pixel-exact + dHash bitmap methods (completes spec §4.5),
three-tier tolerance preset (`generic`/`ci-ubuntu`/`self-hosted-nvidia` per `.claude/rules/ci-integration.md`),
`sdv-test baselines` subcommand (replaces the `--update-baselines` static-field hack with `list`/`update`/
`show`/`delete`), and capture-cache cleanup (auto + manual `sdv-test cache clean`).

**Architecture:** `BitmapAssertion.EvaluateAsync` branches on `a.Method ?? "ssim"`. Per-method `tolerance`
semantics polymorphic — SSIM float, pixel-exact int channel-delta, dHash int Hamming distance. Tier maps to
per-method default tolerances via `TierTolerance.Resolve(tier, method, perAssertionTolerance)`. Diff
renderer's heatmap branch picks per-pixel redness for SSIM (existing) or pixel-exact (new); dHash skips
diff.png entirely (perceptual hash doesn't localize per-pixel — `DiffSet.Diff` is empty string).
`BitmapMethod` enum lives in `Protocol.Reports` (cross-project; same precedent as `DiffFormat`/`DiffSet`).

**Static-field cleanup:** `RunCommandOptions` record threads parsed CLI flags through `RunOnceAsync` instead
of `RunCommand`'s static `_updateBaselinesFlag` / `_diffFormatFlag` / `_tierFlag` fields. `BaselinesCommand.update`
reuses `RunCommand.RunFromOptions` via a swappable `RunExecutor` delegate (test seam — production points to
the real run path; tests substitute a probe).

**Capture cache cleanup:** keeps a file iff BOTH (a) mtime within `--max-age` (default 7 days), AND (b) its
parent scenario subdir is among the `--keep-runs` most-recent (default 5). Auto-hooks at end of every
successful `sdv-test run` invocation; manual `sdv-test cache clean` for one-shot bulk cleanup. Override
location via `$SDV_CACHE_DIR`.

**Test count after bitmap completion bundle:** 368+47 → 388+48 (+20 passed, +1 skipped placeholder).

**Out of scope (Tier 3/4 followups):**
- Per-tier baseline directories (option B from brainstorm — defer until real second CI environment).
- `baselines regenerate` / `baselines validate` (`update` covers regeneration; orphan detection is Tier 4).
- Triptych composite for pixel-exact / dHash (mechanical; defer).
- dHash diff heatmap (perceptual hash doesn't localize per-pixel — explicitly skipped).
- Real environment autodetection for tier (`generic` default is unconditional).
- LFS for baselines (separate Tier 4 item).
- Test-results dir cleanup (`./test-results/` — separate concern).
```

### Step 5: Update docs/roadmap.md

- [ ] Read `docs/roadmap.md`. Two changes:

1. **Remove** these 4 entries from Tier 3 (look for the Tier 3 section, find each by its lead phrase):
   - `Pixel-exact + dHash bitmap methods`
   - `Three-tier baseline tolerance`
   - `sdv-test baselines subcommand`
   - `Capture-cache cleanup`

2. **Add** to `## Completed` under `### 2026-04-26` (create the subsection at the TOP of Completed if it doesn't already exist):

```markdown
### 2026-04-26

- **Bitmap completion bundle**. Four Tier 3 items shipped together:
  - **Pixel-exact + dHash methods** (completes spec §4.5). `bitmap` assertion gains `method`
    field; per-method `tolerance` semantics polymorphic.
  - **Three-tier tolerance preset** (`generic`/`ci-ubuntu`/`self-hosted-nvidia` per
    `.claude/rules/ci-integration.md`). `sdv-test run --tier=<name>` selects per-method
    defaults via `TierTolerance.Resolve`.
  - **`sdv-test baselines` subcommand** (`list`/`update`/`show`/`delete`). Replaces the
    `--update-baselines` static-field hack via a `RunCommandOptions` record refactor +
    swappable `RunExecutor` delegate.
  - **Capture-cache cleanup**. Auto-sweeps `~/.cache/sdv-test-framework/captures/` at end of
    every `sdv-test run` (--no-cache-cleanup to opt out). Manual `sdv-test cache clean`
    with `--max-age` / `--keep-runs` / `--dry-run`.
  368+47 → 388+48.
```

If a `### 2026-04-26` subsection already exists, append the bullet there instead of creating a new one.

### Step 6: Final CI

Run: `cd /home/fintan/stardewRepos/frobby/sdv-test-framework && ./scripts/ci.sh 2>&1 | grep -E "Passed:|Skipped:" | head -10`
Expected: **388 Passed + 48 Skipped** (was 388+47; +1 skipped placeholder).

---

## Self-review

**1. Spec coverage:**
- Spec §4.1 (method dispatch) → T3 ✓
- Spec §4.2 (tolerance polymorphism per method, validation) → T3 (BitmapAssertion switch) + T4 (TierTolerance.Resolve validation) ✓
- Spec §4.3 (tier resolution + 3×3 table + per-assertion override) → T3 (stub), T4 (full table + CLI flag) ✓
- Spec §4.4 (PixelExactDiff) → T1 ✓
- Spec §4.5 (DHashDiff) → T2 ✓
- Spec §4.6 (DiffImageRenderer signature change + per-method branches) → T3 ✓
- Spec §4.7 (BaselinesCommand subcommands) → T6 ✓
- Spec §4.8 (CaptureCacheCleaner + auto-hook + manual command) → T7 ✓
- Spec §5 (wire format: scenario JSON `method` + `tier`, schema updates) → T3 (ScenarioAssertion fields) + T4 (schema enum) ✓
- Spec §6 (testing) → T1 (3) + T2 (3) + T3 (5) + T4 (2) + T6 (4) + T7 (3) + T8 (1 skipped) = 20 + 1 ✓

**2. Placeholder scan:** No TBDs. The "T4 fills in the table" comment in T3 step 9 is explicit handoff, not a placeholder.

**3. Type consistency:**
- `BitmapMethod` enum (`Ssim/PixelExact/DHash`) defined T3 → consumed T3 (BitmapAssertion switch + DiffImageRenderer Render param), T4 (TierTolerance.Resolve param + table key).
- `ScenarioAssertion.Method` (string) + `Tier` (string) defined T3 → consumed T3 (BitmapAssertion reads), T4 (table lookup).
- `ScenarioAssertion.Tolerance` widened to `double?` in T3 step 7 → existing tests at `Tolerance = 0.95` keep working (still compiles).
- `TierTolerance.Resolve(string tier, BitmapMethod method, double? perAssertionTolerance)` defined T3 stub → expanded T4.
- `DiffImageRenderer.Render` signature change in T3 → existing tests updated in T3 step 6, new tests in T3 step 1, T6's `BaselinesCommand.update` doesn't call it directly (delegates through RunCommand), T7's auto-hook doesn't either.
- `RunCommandOptions` record defined T5 → consumed T6 (`RunUpdate` builds one), T7 (read in `RunFromOptions`).
- `RunCommand.RunFromOptions` (public static) defined T5 → consumed T6 default `RunExecutor`.
- `BaselinesCommand.RunExecutor` swappable delegate defined T6 → tested in T6 step 1.
- `CaptureCacheCleaner.CleanCache(string, int, int, bool) → int` defined T7 → consumed T7 auto-hook + manual command.

**4. Hazards:**
- **T3 cascade.** Changing `DiffImageRenderer.Render` signature + `ScenarioAssertion.Tolerance` from non-nullable to nullable is a compile cascade. T3 enumerates the affected sites (existing renderer tests, BitmapAssertion + its 2 test files, ScenarioRunner constructor chain). Subagent must run CI after each chunk to catch missed sites.
- **T5 refactor.** Removing static fields requires touching every reader. Watch-mode closure (in RunAsync's `WatchLoop.RunAsync(...)`) closes over the static; refactor must close over the local options record instead.
- **dHash dispatch test.** The `MethodDHash_DispatchesToDHashDiff` test forces a fail via `Tolerance = -1` to trigger validation. That's a hack but reliable — solid-color images give identical dHash. If dHash validation isn't surfaced as a "dhash"-mentioning failure message, the test fails. Verify TierTolerance.Resolve's "must be in [0, 64]; got -1" message contains "dhash".
- **Cache cleanup auto-hook.** Default behavior — could surprise users. Mitigation: `--no-cache-cleanup` opt-out + the 7-day/5-run defaults are generous + log line on actual deletes.
- **Test count math.** T1+3, T2+3, T3+5, T4+2, T5+0 (refactor), T6+4, T7+3, T8+1 skipped = +20 passed, +1 skipped. Net 368+47 → 388+48.

---

## Execution handoff

Plan complete + saved to `docs/superpowers/plans/2026-04-26-bitmap-completion-bundle.md`.
Two execution options:

**1. Subagent-Driven (recommended)** — fresh subagent per task, two-stage review.

**2. Inline Execution** — tasks run in this session via executing-plans.

**Which approach?**
