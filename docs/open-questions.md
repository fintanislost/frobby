# Open Questions

Unknowns that need investigation but aren't blocking current work. Resolve via spike when they become blocking.

## Active

### Per-location RNG pinning

Some `GameLocation` subclasses have internal RNG fields. Do we need to pin each, or does pinning `Game1.random` cover everything that matters for scenarios?

- **Blocking:** M1 only if scenarios hit location-specific randomization
- **Spike candidate:** yes, if/when the determinism test flakes on a location-specific scenario

### Texture hash collision rate

Tier 2 texture resolution hashes texture bytes. How often do different-looking textures produce identical hashes in real SDV content? Probably ~never, but worth measuring once.

- **Blocking:** no, Tier 1 covers almost everything
- **Spike candidate:** after M1, low priority

### Multiplayer support

The spec excludes multiplayer. Is there a path to include it post-M3, or is it fundamentally incompatible with the FREEZE model?

- **Blocking:** no, explicitly out of scope
- **Spike candidate:** only if there's community demand

### Mod conflict reporting granularity

When a scenario fails because another mod patches the same method, what information do we surface? Full patch list? Diff of expected vs actual draw stream?

- **Blocking:** M2 polish
- **Spike candidate:** during M2 if the naive approach is unclear

### .NET target for the harness mod (spec vs. reality)

`docs/spec.md §1` says ".NET 8 for harness mod and CLI runner." Actual SMAPI 4.5.2.0 runs on `net6.0` (per its `runtimeconfig.json`), and SDV 1.6.15 itself is `net6.0`. A SMAPI mod loads into SMAPI's own process, so **the mod must target `net6.0`**. The runner CLI is a separate process and can target net8/net10 independently.

**Update (M1 foundation):** settled. `src/Harness/` and `src/Protocol/` target `net6.0` (protocol sits in-process with harness). `src/Runner/` targets `net10.0` (available on the dev workstation; spec's "net8+" floor satisfied). The spec itself should be updated to reflect this split before M2.

- **Action:** Update `docs/spec.md §1` and `docs/developer-setup.md` to say "net6.0 for the harness mod + shared protocol; net10.0 (or ≥ net8.0) for the runner CLI."
- **Blocking:** no.

### SMAPI version pin

`docs/fixtures.md` and `docs/ci-integration.md` pin SMAPI 4.1.10. Install on the dev workstation is 4.5.2.0. `manifest.json` sets `MinimumApiVersion: 4.1.10` (conservative lower bound). If CI locks to 4.1.10 but developers run on 4.5.x, we could miss a version-drift break locally and get bitten in CI.

- **Action:** Decide at M1 start whether to bump the pin to 4.5.x or keep 4.1.10 and add a CI job that tests on the newest SMAPI too.
- **Blocking:** no.

### Parallax background scroll determinism (from M0 spike)

M0's determinism experiment achieved 94.93% byte-level equality across two runs. The residual 5% is 100% `dst`-field divergence concentrated at negative-y coordinates — the `Game1.background` parallax cloud/sky layers whose horizontal scroll tracks `Game1.currentGameTime.TotalGameTime`. `eventUp=true` doesn't suppress these.

- **Action:** Implement one of three fixes in M1's FREEZE-phase code:
  1. Prefix-patch `Background.update` to zero-out scroll state while armed (minimal side effects).
  2. Set `Game1.background = null` during capture (suppresses parallax entirely; removes from any screenshot tests).
  3. Prefix-patch `Game1.Update` to short-circuit when armed, pairing with our own draw trigger (heaviest; stops all animation timers).
- **Blocking:** M1 determinism-controller work. Scenarios that assert on background draws are blocked on this; scenarios that don't can be authored now.

## Resolved

_(move items here with resolution notes when answered)_
