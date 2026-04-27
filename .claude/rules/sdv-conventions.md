# Stardew Valley / SMAPI Conventions

Hard-won context about working with the SDV codebase. Consult before writing anything that touches `Game1.*`.

## Statics everywhere

SDV's codebase treats `Game1` as a god object. `Game1.player`, `Game1.currentLocation`, `Game1.random`, `Game1.currentGameTime` — all statics, all mutable, all touched from dozens of places. This is why determinism is hard and why our architecture treats the harness mod as "the" entry point for mutation during tests.

**Rule:** never cache `Game1.*` references across ticks in framework code. The state can change.

## Tick cadence

Game updates at 60 Hz via `Game1.Update`. SMAPI's `GameLoop.UpdateTicked` fires after each update. Draw happens separately via `Game1.Draw`. Our recorder lives on the draw path; our command loop lives on the update path.

## Save loading is async-ish

`SaveGame.Load` is a coroutine. During loading, `Game1.player`, `Game1.currentLocation`, etc., are in transitional states. Never assert on state until `SaveEvents.Loaded` (or SMAPI's `GameLoop.SaveLoaded`) fires.

## Content pipeline quirks

- Assets load lazily. `Game1.content.Load<Texture2D>("Characters/Abigail")` populates the content cache on first call.
- SMAPI's `IAssetLoader`/`IContentHelper` intercepts loads; our texture→path map hooks there.
- Mods can invalidate cached assets via `helper.GameContent.InvalidateCache`. Our recorder must rebuild its texture map on invalidation events.

## NPC positions and movement

NPCs have three position-ish fields: `Position`, `TilePoint`, and `getTileLocationPoint()`. They don't always agree mid-tick. Always use `getTileLocation()` for assertions and document which coordinate space a manipulator accepts.

## Menus

`Game1.activeClickableMenu` is the current top-level menu or null. Shop menus, dialogue boxes, and crafting menus all go through this. Nested menus (e.g., inventory inside a shop) are tracked via the menu's own state, not `Game1.activeClickableMenu`.

## Farmer references

`Game1.player` in singleplayer = the local farmer. In multiplayer, `Game1.getAllFarmers()` matters. We target singleplayer only for v1 but write farmer lookups defensively — don't assume `Game1.player` is the only farmer.

## Random sources

- `Game1.random` — the main one
- `Game1.recentMultiplayerRandom` — multiplayer-seeded, rarely relevant to us
- Each `GameLocation` has its own RNG in some cases
- Some mini-games (fishing, slots) use local RNG inside the minigame instance

Seed pinning via reflection works for `Game1.random`. Per-location RNG needs case-by-case handling; document in `determinism.md`.

## Version detection

`Game1.version` (string, e.g., `"1.6.15"`) and the SMAPI constants API give us what we need. The `doctor` command consults both and the mod manifest list.

## DON'T

- Don't call `Game1.exitActiveMenu()` from a Harmony prefix. Queue it through the command loop.
- Don't set `Game1.paused = true` without unsetting it — easy to leave the game wedged.
- Don't mutate collections during iteration (e.g., adding objects to a location while it's being drawn). Queue the mutation.
- Don't use `Thread.Sleep` anywhere. Ever. Use SMAPI's tick events or `GameLoop.UpdateTicking`.
