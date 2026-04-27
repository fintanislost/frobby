# Milestone 0 — Determinism Spike

**Goal:** Prove the foundation works before building anything else.

**Duration:** 1 week target. If it slips past 2, reassess.

**Exit criteria:** Two consecutive runs of an identical scripted scenario produce byte-identical draw-call streams.

## Why this comes first

The entire framework rests on two unproven assumptions:

1. We can Harmony-patch `SpriteBatch.Draw` and capture every call cleanly
2. We can pin enough nondeterminism to get reproducible output

If either is false, the architecture needs rethinking. Don't build the RPC layer, runner CLI, or scenario format until this spike passes.

## Deliverables

### D0.1 — Minimal harness mod

- SMAPI mod project at `src/Harness/`
- Harmony patches on all `SpriteBatch.Draw` overloads
- Writes draw events to `/tmp/draws-<tick>.jsonl` when armed
- Arm/disarm via a dev-only SMAPI console command (`harness_arm`, `harness_snapshot`)
- No socket, no RPC yet — just prove capture works

### D0.2 — Determinism experiment script

- `docs/spikes/2026-XX-determinism/` directory
- Shell script that launches SDV twice with the same fixture save and seed
- Diffs the two `/tmp/draws-*.jsonl` outputs
- Reports first divergence, if any

### D0.3 — Spike report

- `docs/spikes/2026-XX-determinism/REPORT.md`
- Documents: what was pinned, what wasn't, frame-time overhead measured, edge cases found
- Recommendation: proceed to M1, revise spec, or escalate a blocker

## Non-goals

- No texture → path resolution yet (log textures by reference ID)
- No scenario format, no RPC, no runner CLI
- No bitmap fallback
- Not productionizing the harness — this is throwaway learning code

## Known risks

- **Texture identity across reloads** — the `Texture2D` reference is per-session. The spike may need to ignore texture identity and compare on (width, height, source rect) instead. That's fine for the spike; plan for proper resolution in M2.
- **FNA vs XNA differences** — SDV 1.6 uses MonoGame which wraps both. The draw overloads might differ subtly. Enumerate at runtime, don't hard-code.
- **Mod load order** — the spike runs with zero other mods. Document this. M1 handles the conflict question.

## Acceptance

```
$ ./docs/spikes/2026-XX-determinism/run.sh
[run 1/2] Launching SDV, fixture=spring_day_1_clean, seed=42...
[run 1/2] Captured 84,231 draw events in 120 ticks. Hash: ab12...
[run 2/2] Launching SDV, fixture=spring_day_1_clean, seed=42...
[run 2/2] Captured 84,231 draw events in 120 ticks. Hash: ab12...
✓ Deterministic: identical streams
```

If this works, M0 passes and M1 can begin.

If it doesn't, the REPORT.md documents the divergence, and the next step is either (a) tighten determinism controls or (b) accept a well-characterized source of nondeterminism and filter it from captures.
