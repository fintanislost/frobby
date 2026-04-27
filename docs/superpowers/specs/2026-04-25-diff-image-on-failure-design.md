# Diff-Image-on-Failure — Design Spec

**Status:** Approved design, pre-implementation
**Author:** Brainstorm 2026-04-25
**Source:** roadmap.md Tier 3 entry "Diff-image-on-failure (~2 days)"
**Predecessor:** HTML run reports (`docs/superpowers/specs/2026-04-24-html-run-reports-design.md`)

## 1. Problem

`bitmap` assertions today report SSIM score + dimension mismatch on failure but produce no visual artifact. When an assertion fails the user (or Claude) has to:
1. Find the baseline PNG manually,
2. Find the capture PNG in `~/.cache/sdv-test-framework/captures/<scenario>/`,
3. Open both side-by-side and visually diff them.

For LLM-driven iteration this is friction. Claude can read the SSIM number but can't *see* the difference; without a diff image it has no way to know whether a visual regression is "off by 2 pixels" vs "completely wrong asset."

## 2. Goal

On `bitmap` assertion failure, write a per-failure forensics directory containing:
- `baseline.png` — what was expected
- `capture.png` — what was captured
- `diff.png` — capture/baseline composite with bilinear-smoothed heatmap overlay highlighting per-block SSIM regions below tolerance

Surface these in the HTML run report's per-scenario page, in a dedicated "Failure forensics" section above the existing screenshots grid. Surface paths in the MCP `run_scenario` result so Claude can navigate to them.

Optional triptych composite (`triptych.png` = baseline | capture | diff stitched horizontally) for one-glance review.

## 3. Non-goals

- Pixel-exact / dHash diff modes — separate Tier 3 item.
- Auto-uploading diffs anywhere (CI artifacts, S3, etc.) — covered by existing `actions/upload-artifact@v4` in `.github/workflows/test.yml`.
- Retroactive diff generation for runs without diffs — fixing the run is faster than re-rendering.
- Diff images for non-bitmap assertion types — `state` and `draw` failures are already legible from text.
- Configurable diff color schemes — red-tint heatmap is the only option.

## 4. Architecture

### 4.1 Components

**New files:**
- `src/Runner/Bitmap/SsimResult.cs` — record carrying score + per-block 2D grid.
- `src/Runner/Bitmap/DiffImageRenderer.cs` — pure function renderer.
- `src/Runner/Bitmap/DiffSet.cs` — record carrying the four written paths.

**Modified files:**
- `src/Runner/Bitmap/SsimDiff.cs` — `Compute` returns `SsimResult` instead of `float`.
- `src/Runner/Bitmap/BitmapAssertion.cs` — generate diffs on failure; skip on `--update-baselines`.
- `src/Runner/Scenarios/BitmapAssertionSpec.cs` — add optional `DiffFormat` field.
- `src/Runner/Scenarios/ScenarioReport.cs` — add `Diffs: List<DiffSet>` collection.
- `src/Protocol/Reports/RunSummary.cs` — extend `ScenarioOutcome` with `Diffs: IReadOnlyList<DiffSet>`.
- `src/Runner/Reports/HtmlReportGenerator.cs` — render forensics section.
- `src/Runner/Commands/RunCommand.cs` — `--diff-format` flag.
- `src/Runner.Mcp/Tools/RunScenarioTool.cs` — `diff_format` arg.
- `schemas/scenario.schema.json` — `diff_format` property on bitmap assertion.

### 4.2 SsimResult

```csharp
public readonly record struct SsimResult(
    float Score,
    float[,] BlockScores,   // BlocksY rows × BlocksX cols
    int BlocksX,
    int BlocksY);
```

`Score` is the mean of `BlockScores` (existing behavior). `BlocksX = imgWidth / 8`, `BlocksY = imgHeight / 8`.

### 4.3 DiffImageRenderer

```csharp
public static class DiffImageRenderer
{
    public static DiffSet Render(
        byte[] baselineBytes, byte[] captureBytes,
        SsimResult ssim, float tolerance,
        DiffFormat format,
        string outputDir);
}

public enum DiffFormat { Files, Triptych, All }
public sealed record DiffSet(string Baseline, string Capture, string Diff, string? Triptych);
```

**Heatmap construction:**
1. Decode both PNGs as `Image<Rgba32>`.
2. Compute per-block redness: `redness[bx, by] = clamp((tolerance - blockScores[by, bx]) / tolerance, 0, 1)`. Block scoring above tolerance → 0 (no tint). Block scoring at 0 → 1 (full red).
3. Bilinear-interpolate the per-block grid into per-pixel: for pixel (px, py) sample the 4 neighbouring block-center values weighted by distance. Block-center is at `(bx*8 + 4, by*8 + 4)`. Edge pixels (px < 4, py < 4, px ≥ width-4, py ≥ height-4) clamp to the boundary block coordinate so we always have 4 valid neighbours.
4. For each pixel: `R' = lerp(baselineR, 255, redness * 0.6)`, `G' = baselineG * (1 - redness * 0.4)`, `B' = baselineB * (1 - redness * 0.4)`. The 0.6/0.4 mix keeps the underlying image visible while making hot regions obvious.
5. Encode to `diff.png`.

`baseline.png` and `capture.png` are byte-for-byte copies of the inputs (no reencoding — preserves source fidelity, faster).

`triptych.png` (when format is `Triptych` or `All`): create a `Image<Rgba32>` of size `(3 * width, height)`, paste baseline at x=0, capture at x=width, diff at x=2*width. Uses ImageSharp's `Mutate(ctx => ctx.DrawImage(...))`.

### 4.4 BitmapAssertion failure path

```csharp
var ssim = SsimDiff.Compute(baselineBytes, captureBytes);
if (ssim.Score >= tolerance)
    return AssertionResult.Pass();

if (_updateBaselinesMode)
{
    BaselineManager.WriteBaseline(baselinePath, captureBytes);
    return AssertionResult.Pass();
}

DiffSet? diffs = null;
if (_runDir is not null && _scenarioName is not null)
{
    var format = spec.DiffFormat ?? _runWideDiffFormat;
    var outDir = Path.Combine(_runDir.ScenarioDir(_scenarioName), "diffs",
                              $"assertion-{_assertionIndex:D2}-bitmap");
    Directory.CreateDirectory(outDir);
    try
    {
        diffs = DiffImageRenderer.Render(baselineBytes, captureBytes, ssim, tolerance, format, outDir);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[bitmap] diff render failed: {ex.Message}");
    }
}

return AssertionResult.Fail(
    $"SSIM {ssim.Score:F4} < tolerance {tolerance:F2}",
    diffs);
```

**Assertion ID:** `assertion-{index:D2}-bitmap`. Index is the assertion's position in the full `spec.Assertions` list (0-based, zero-padded — counts non-bitmap assertions too so the index matches what's printed in CLI failure output). Stable across reruns of the same scenario.

### 4.5 DiffFormat resolution

Per-assertion `BitmapAssertionSpec.DiffFormat` (nullable) > run-wide CLI/MCP flag > default `Files`.

**Why both:** the run-wide flag is the agent-friendly knob (Claude reruns one scenario asking for triptych). Per-assertion override exists for the scenario author who knows a specific assertion is finicky and wants triptych for that one without affecting the rest of the run.

### 4.6 HTML report integration

`ScenarioOutcome.Diffs: IReadOnlyList<DiffSet>` — populated by `ScenarioRunner` from `AssertionResult.Diffs`.

`HtmlReportGenerator.RenderScenarioReport` adds before the existing screenshots block:

```html
<section class="forensics">
  <h2>Failure forensics</h2>
  <div class="diff-grid">
    <figure class="diff-set">
      <h3>assertion-03-bitmap</h3>
      <p class="meta">SSIM 0.7234 &lt; tolerance 0.95</p>
      <div class="triptych">
        <figure><img src="diffs/assertion-03-bitmap/baseline.png"><figcaption>baseline</figcaption></figure>
        <figure><img src="diffs/assertion-03-bitmap/capture.png"><figcaption>capture</figcaption></figure>
        <figure><img src="diffs/assertion-03-bitmap/diff.png"><figcaption>diff</figcaption></figure>
      </div>
    </figure>
    <!-- one .diff-set per failing bitmap assertion -->
  </div>
</section>
```

CSS additions to `assets/styles.css`:
```css
.forensics { background: #fff5f5; border-left: 4px solid #b03030; padding: 1em; margin: 1em 0; }
.forensics h2 { margin-top: 0; color: #b03030; }
.diff-set h3 { font-family: monospace; font-size: 1em; }
.diff-set .meta { color: #666; font-style: italic; }
.triptych { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 0.5em; }
.triptych img { width: 100%; height: auto; border: 1px solid #ddd; }
.triptych figcaption { text-align: center; font-size: 0.85em; color: #666; }
```

Section is rendered only if `s.Diffs.Count > 0`.

### 4.7 MCP integration

`RunScenarioTool` accepts optional `diff_format` arg (string: `"files" | "triptych" | "all"`). Threads through to the diff-format resolution. Result includes diff paths transitively via the `report_dir` (the on-disk forensics dir is reachable from the `report_dir` already returned by T6 of HTML run reports).

No new MCP tool. Claude reads files via the local filesystem; the report dir is already exposed.

### 4.8 update-baselines short-circuit

When `--update-baselines` is set, on SSIM mismatch the capture overwrites the baseline (existing behavior). Skip diff generation entirely — there's no failure to forensics, the baseline is now the capture.

## 5. Data shapes

**Wire format change:** `bitmap` assertion in scenario JSON gains optional `diff_format`:
```json
{
  "type": "bitmap",
  "baseline": "baselines/shop_menu.png",
  "tolerance": 0.95,
  "diff_format": "triptych"
}
```
`diff_format` enum: `"files" | "triptych" | "all"`. Absent → fall back to run-wide flag.

**`summary.json` change:** each `ScenarioOutcome` gains `diffs` array:
```json
"diffs": [
  {
    "baseline": "scenarios/shop/diffs/assertion-03-bitmap/baseline.png",
    "capture": "scenarios/shop/diffs/assertion-03-bitmap/capture.png",
    "diff": "scenarios/shop/diffs/assertion-03-bitmap/diff.png",
    "triptych": null
  }
]
```
Paths are relative to the run-dir root. `triptych` is null unless format requested it.

## 6. Testing

### 6.1 SsimResult — 1 test
- `Score_EqualsAverageOfBlockScores`

### 6.2 DiffImageRenderer — 4 tests
- `IdenticalImages_DiffPngHasNoRedTint` — sample 10 random pixels in diff.png, all should match baseline within ±2 per channel.
- `DifferingImages_DiffPngHasRedRegionsAtExpectedBlocks` — pre-baked fixture with known-bad regions; assert red dominance in those blocks.
- `Triptych_ProducesFourthFile_3xWidth` — assert file exists + width = 3 × baseline width.
- `BilinearSmoothing_NoHardBlockBoundaries` — sample pixels at x=7, x=8, x=9 (block boundary); the redness gradient should be continuous.

### 6.3 BitmapAssertion — 3 tests
- `FailingAssertion_WritesThreeDiffPngs` — fixture with `BitmapInvoker` shim returning a known-bad capture; assert 3 files exist.
- `PassingAssertion_WritesNoDiffPngs` — identical-baseline scenario; diff dir doesn't exist.
- `UpdateBaselinesMode_FailingAssertion_WritesNoDiffPngs` — baseline gets overwritten, no diffs.

### 6.4 HtmlReportGenerator — 2 tests
- `ScenarioWithDiffs_RendersForensicsSection` — assert `<section class="forensics">` present + 3 image refs.
- `ScenarioWithoutDiffs_HasNoForensicsSection` — scenario with all-passed assertions, no forensics block in HTML.

### 6.5 MCP — 1 test
- `RunScenario_DiffFormatArg_ThreadsThrough` — pass `diff_format=triptych`, verify it's surfaced (likely via inspecting the resulting summary.json's diff entries).

### 6.6 Integration placeholder — 1 skipped
- `RunReports_BitmapFailureProducesDiffs` — runs `./scripts/run-samples.sh` against a tampered baseline. Manual smoke per project convention.

**Test count delta:** +11 passed, +1 skipped. Target: **357+46 → 368+47**.

## 7. Risks + open questions

**Disk usage.** Each failing bitmap assertion produces 3 PNGs (~3MB total at 1280×720 with PNG compression). A scenario with multiple bitmap failures could generate ~10MB of diffs. Acceptable; Tier 4 can add cleanup.

**Bilinear smoothing complexity.** The per-block-grid → per-pixel-grid bilinear pass is ~30 LOC. The naive approach (4-neighbour weighted sum per pixel) is correct; ImageSharp's `Resize` mode may be tempting but doing it manually in a single pass is cheaper than constructing intermediate images.

**Composite size for "all" format.** `all` writes baseline + capture + diff + triptych (4 PNGs, ~6MB). User can opt-in only if they want this; default `files` is 3 PNGs.

**Test fixture for differing-image case.** Need pre-baked PNGs at `tests/Runner.Tests/Bitmap/fixtures/` similar to M2-bitmap's noise variant. Reuse those if shape matches; otherwise commit new fixtures.

**Existing `SsimDiff.Compute(byte[], byte[])` callers.** `BitmapAssertion` is the only production caller; the M2-bitmap test `SsimDiff.IdenticalImages_ReturnsOne` tests against the float return. Update tests to read `.Score`. No external API surface to maintain.

## 8. Out of scope (future tiers)

- **Diff for state/draw assertions.** Text-based, already legible.
- **Configurable diff colors / opacity.** Red-tint heatmap is the only option.
- **Diff annotations** (arrows pointing at hot regions, text labels). Pure-image output.
- **Animated diffs** (GIF showing baseline → capture transition). Static PNGs only.
- **Diff retention policy.** Diffs accumulate in `test-results/<run-id>/`; cleanup pairs with the existing Tier 4 capture-cache cleanup item.

## 9. Implementation plan handoff

Single plan. 6 tasks: SsimResult extraction → DiffImageRenderer → BitmapAssertion plumbing → HTML forensics section → CLI/MCP/per-assertion knobs → smoke + docs + roadmap.

Save to `docs/superpowers/plans/2026-04-25-diff-image-on-failure.md`.
