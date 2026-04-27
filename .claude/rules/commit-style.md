# Commit and PR Conventions

## Commit messages

Conventional Commits format:

```
<type>(<scope>): <subject>

<body>

<footer>
```

Types: `feat`, `fix`, `spike`, `refactor`, `test`, `docs`, `chore`, `ci`.

Scopes match top-level source directories: `harness`, `runner`, `rpc`, `recorder`, `determinism`, `fixtures`, `ci`, `docs`.

Subject line: imperative mood, no period, under 72 chars.

Body: **why** over **what**. Reference the milestone and spike doc if applicable.

Footer: `Refs: M1`, `Closes #42`, `Spike: docs/spikes/2026-05-draw-capture.md`.

## Example

```
feat(recorder): intercept SpriteBatch.Draw via Harmony prefix

Implements the core draw-call capture per spec §4.2. Prefix-only,
observation-safe, appends to a thread-local ring buffer. Texture→asset
path resolution is stubbed pending the content pipeline hook in M2.

Refs: M1
Spike: docs/spikes/2026-05-draw-capture.md
```

## PR conventions

Title matches the lead commit subject. Description includes:

- **What** — one paragraph summary
- **Why** — the milestone/issue this advances
- **Testing** — how to verify manually + which automated tests cover it
- **Risk** — what could break; SMAPI/SDV version compat notes

Draft PRs are expected while in progress. Mark ready for review only after CI is green and the milestone checklist reflects the work.
