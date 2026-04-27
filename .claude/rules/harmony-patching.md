# Harmony Patch Safety

Harmony patches are the backbone of the draw-call recorder and determinism controller. They're also the most likely source of silent breakage across SMAPI/SDV versions. Follow these rules.

## Required patch header

Every Harmony patch method gets this comment block:

```csharp
// Patch: SpriteBatch.Draw(Texture2D, Vector2, Rectangle?, Color)
// Type: Prefix (non-modifying, records side effect)
// Why: Capture draw events for assertion queries (spec §4.2)
// Rollback: Remove [HarmonyPatch] attribute; recorder falls back to inert mode
// Tested in: tests/Integration/DrawCallRecorderTests.cs
// Depends on: Harmony 2.x, SMAPI >= 4.0 (for IReflectionHelper access)
```

Missing any of these lines is a review blocker.

## Patch type preferences

In order of preference:

1. **Prefix + return true** (observe only, never modify control flow) — the default for recording
2. **Postfix** — when we need the method's return value (e.g., inspecting what `Game1.getLocationFromName` returned)
3. **Transpiler** — only when prefix/postfix is insufficient. Requires doubled scrutiny; every transpiler needs a version-compat test.
4. **Prefix + return false** (skip original) — forbidden without design discussion. This is where mod conflicts happen.

## Version coupling

- Pin minimum SMAPI version in `manifest.json`
- Every patch targets a specific method signature. If SDV changes that signature in a point release, the patch silently stops applying. **Always** use `MethodInfo != null` assertion at patch registration time and fail loud.
- Add a `doctor` check that runs all patch registrations against the current SDV assembly and reports any that fail to resolve.

## Thread safety

MonoGame and `Game1.*` are **not** thread-safe. Harmony patches run on whatever thread called the original method. For the draw recorder this is always the game thread — safe. For anything touched by SMAPI events, confirm thread via `GameLoop.UpdateTicked` dispatch.

Never queue work from a Harmony prefix to a background thread that then mutates `Game1.*`. Record-only is fine (append to ring buffer); mutation must go through the command loop.

## Testing patches

Integration test pattern:

1. Load SDV with harness mod only (no mod-under-test)
2. Load a golden fixture save
3. Arm the recorder
4. Advance one tick
5. Assert draw-call stream matches a committed baseline

If the baseline diff shows unexpected entries, either the patch is wrong or SDV changed. Investigate before regenerating the baseline.

## When a patch breaks

Don't silently widen the match. Prefer:

1. Keep the old patch, add a new version-gated one
2. Fail loud at registration if neither resolves
3. Document the SDV version range each patch targets in its header comment
