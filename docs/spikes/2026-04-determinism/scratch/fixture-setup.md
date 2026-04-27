# Fixture: spring_day_1_clean

Minimal SDV save used as the starting state for the M0 determinism experiment. The
experiment loads this save, arms the recorder, and records N ticks of draws.

## Contents

- Female farmer named `Tester`, farm name `m0spike`
- Standard farm
- Spring Day 1 Year 1, morning (06:00)
- **Only** the spike harness mod loaded (isolated `SMAPI_MODS_PATH`)
- RNG seed at scenario start: **42** (pinned via `harness_pin_seed 42`)

## Procedure

One-time, interactive. ~3 minutes if you know SDV's menus.

### 1. Launch SMAPI with only the harness

```bash
cd docs/spikes/2026-04-determinism/scratch
./launch-smapi.sh
```

Verifies SDV is installed + the harness is staged, passes `SMAPI_MODS_PATH` through,
and starts a Xvfb display if you're headless. You should see SMAPI's banner followed
by:

```
[M0 spike harness] loaded. Commands: harness_arm, harness_disarm, harness_snapshot,
                   harness_pin_seed, harness_save.
```

If instead you see a block of red startup errors, the harness failed to load — check
the stack trace; most likely causes are Harmony patch resolution failures (SDV internals
shifted between 1.6.15 and your install) or a missing `SpriteBatch.Draw` overload.

### 2. Create the character

At the SDV main menu:
- **New**
- Name: `Tester`
- Farm name: `m0spike`
- Favorite thing: anything (doesn't matter)
- Gender: **female**
- Skin / hair / etc.: accept defaults
- Pet: skip (doesn't matter for the spike)
- Farm type: **Standard**
- Skip the intro cutscene (ESC repeatedly)

You should land on the farm at 06:00, Spring 1, Year 1.

### 3. Save without sleeping

Switch focus to the terminal window running SMAPI and type:

```
harness_save
```

You should see `[M0 spike harness] Save complete.` in the log.

> **If `harness_save` reports "SaveGame.Save failed: …":** the SDV internal save path
> rejected the call. Fallback: walk to your bed in the farmhouse, press `Q` or right-click
> the bed, advance through the overnight summary, then `harness_save` the Day 2 morning
> state. Rename the fixture to `spring_day_2_clean` and update `run.sh`'s `FIXTURE` default.

### 4. Quit

`ALT+F4` or use SDV's exit menu — either works. You don't need to save again.

### 5. Stage the save into the fixtures directory

```bash
./stage-fixture.sh
```

This finds `Tester_m0spike` in SDV's saves directory (checks the Flatpak and XDG paths),
copies it to `scratch/fixtures/spring_day_1_clean.sav`, and writes a
`spring_day_1_clean.meta.json` alongside it.

### 6. Verify

```bash
ls scratch/fixtures/
```

You should see:
- `spring_day_1_clean.sav`        (~100-200 KB)
- `spring_day_1_clean.meta.json`

### 7. Run the experiment

```bash
./run.sh
```

See REPORT.md §"Next steps to unblock execution" for interpretation of the outcomes.

## Why this state

- Day 1 morning: no mail flags, no NPC schedules actively pathing (06:00 is before most
  schedule points kick in), minimal prior RNG consumption from character creation.
- No tilled soil, watered crops, chopped trees — no prior-session side state.
- The farm is visually minimal — small draw budget, so 120 ticks of capture produce
  single-digit-thousands of events, not tens of thousands.

## Staleness

Regenerate if the SDV version changes. Save format is mostly forward-compatible
within 1.6.x but 1.7 (whenever it lands) will likely require a new fixture. The meta
file records `sdv_version` at creation time — `[tool] doctor` will warn on mismatch
once that command exists.
