# SVE Slice 34 Content Asset Nested Collections Design

## Overview

Slice 34 fills the neutral Frobby capability gap found while hardening SVE
scenario 41. `content.asset` can currently prove that a selected data entry
exists and that nested collections have a nonzero `count`, but it cannot inspect
bounded items inside those collections. That leaves tests unable to assert the
runtime `Data/MoviesReactions` entry for Martin actually contains a
`Response: reject` item after Content Patcher conditions apply.

The fix should remain generic. This is not a movie-specific or SVE-specific
helper. It should let any mod test inspect a small, bounded sample of nested
runtime list/array items inside selected content data entries.

## Current State

Implemented before this slice:

- `content.asset` loads live runtime assets through Stardew's game-content
  pipeline after Content Patcher/config/game-state conditions have applied.
- Keyed data dictionaries and keyed list-style assets can be summarized with
  `entry_keys`.
- Selected entry values expose public scalar fields/properties and bounded
  nested objects with snake_case names.
- Collection values currently expose only `runtime_type` and `count`.
- Scenario expressions already support `asset.<path> contains <field> '<value>'`
  when `<path>` resolves to an array of objects.

The observed gap:

- SVE scenario 41 can assert
  `asset.entries.Martin.value.reactions.count != 0`.
- It cannot yet assert
  `asset.entries.Martin.value.reactions.items contains response 'reject'`
  because `reactions.items` does not exist.

## Goals

1. Extend `content.asset` so nested list/array summaries include bounded
   `items` in addition to existing `runtime_type` and `count`.
2. Preserve compatibility for existing scenarios that use collection `count`.
3. Add an opt-in request/assertion cap named `nested_items_limit`, with a safe
   default when omitted.
4. Keep payloads bounded and deterministic enough for reports and MCP use.
5. Update SVE scenario 41 to assert Martin's runtime movie reaction contains a
   reject response.
6. Update docs, schema, and TODO tracking so future mod authors can discover the
   new assertion pattern.

## Non-Goals

- Do not expose raw, unbounded asset JSON.
- Do not add an SVE- or movie-specific `movie.reactions` helper.
- Do not expand top-level dictionaries into all entries unless they are already
  requested through `entry_keys`.
- Do not change the existing `asset.entries.<key>.value.<collection>.count`
  behavior.
- Do not merge the SVE feature branch to `master`.

## Proposed Approach

Enhance `ContentAssetProjector.SummarizeValue` so when it sees a non-string
`IEnumerable` that is not a dictionary, it returns:

```json
{
  "runtime_type": "System.Collections.Generic.List`1[...]",
  "count": 3,
  "items_limit": 2,
  "items_truncated": true,
  "items": [
    { "tag": "*", "response": "reject", "id": "reaction_0" },
    { "tag": "love", "response": "like", "id": "reaction_1" }
  ]
}
```

The projector should summarize at most `nested_items_limit` items per collection.
Default `nested_items_limit` is `25`; valid request range is `1..100`.
`items_truncated` is true when the runtime collection has more elements than the
included item count.

Item summaries should reuse the existing scalar/object summarization logic:

- scalar items become scalar JSON values;
- object items expose public scalar fields/properties in snake_case;
- nested objects remain depth-limited by the existing `MaxObjectDepth`;
- nested child collections also get bounded `items`.

The runner assertion evaluator does not need a new expression language. The
existing array `contains field 'literal'` expression can operate on the new
`items` array.

## Components

### Protocol Models

Modify:

- `src/Protocol/Models/ContentAssetRequest.cs`
- `src/Protocol/Models/ScenarioAssertion.cs`

Add nullable `NestedItemsLimit` properties. Scenario assertions pass the value
through to the `content.asset` RPC request.

### Harness Projection

Modify:

- `src/Harness/Assets/ContentAssetProjector.cs`

Add request validation for `nested_items_limit`, pass the resolved limit through
value summarization, and expose bounded `items` for list/array-style nested
collections.

### Runner Assertion Path

Modify:

- `src/Runner.Mcp/Scenarios/ScenarioAssertionEvaluator.cs`

Pass `NestedItemsLimit` from `ScenarioAssertion` into `ContentAssetRequest`.
No evaluator expression changes are expected.

### Tests

Modify:

- `tests/Harness.Tests/ContentAssetProjectorTests.cs`
- `tests/Protocol.Tests/ContentAssetSerializationTests.cs`
- `tests/Runner.Tests/ScenarioRunnerContentAssetTests.cs`

Add red/green coverage for:

- nested collection item projection;
- limit/truncation metadata;
- invalid `nested_items_limit`;
- protocol snake_case serialization;
- scenario expression success against `reactions.items contains response
  'reject'`.

### Schema And Docs

Modify:

- `schemas/scenario.schema.json`
- `docs/rpc-schema.md`
- `docs/wiki/examples.md`
- `SVE_FROBBY_CAPABILITY_TODO.md`

Document `nested_items_limit` and the new nested collection summary shape.
Record Slice 34 as active while implementing and done after verification.

### SVE Scenario

Modify:

- `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/41-sve-martin-movie-worker-ticket-reject.test.json`
- `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

Add an assertion that Martin's live runtime movie reaction data includes a
reject response:

```json
{
  "label": "Martin movie reaction data contains a reject response",
  "type": "content.asset",
  "asset": "Data/MoviesReactions",
  "asset_type": "data",
  "entry_keys": ["Martin"],
  "nested_items_limit": 10,
  "expr": "asset.entries.Martin.value.reactions.items contains response 'reject'",
  "message": "Martin should have a runtime reject movie reaction while working."
}
```

## Data Flow

1. A scenario assertion requests `content.asset` for a runtime data asset and
   supplies one or more `entry_keys`.
2. The runner passes `nested_items_limit` into the `content.asset` request.
3. The harness loads the live asset through Stardew's content pipeline.
4. The harness summarizes only selected entries.
5. Nested list/array values expose count, limit metadata, and bounded items.
6. The runner evaluates existing `contains field` expressions against the
   projected `items` array.
7. SVE scenario 41 validates Martin's runtime reject response without parsing
   source content files.

## Error Handling

- Missing `name`, unsupported `asset_type`, invalid `keys_limit`, and invalid
  `nested_items_limit` return `InvalidParams`.
- `nested_items_limit` outside `1..100` fails before asset loading.
- Collection projection should tolerate item property getters that throw by
  skipping those properties, matching existing object projection behavior.
- Dictionaries remain count-only at nested collection sites for this slice so
  their key/value shape is not accidentally misrepresented as a list.
- If an assertion references `.items` for an empty collection, it should resolve
  to an empty array and fail with the existing "expected path to contain" detail.

## Testing

Frobby unit tests:

- `ContentAssetProjectorTests` for nested item projection, scalar list item
  projection, limit/truncation metadata, and invalid limits.
- `ContentAssetSerializationTests` for `nested_items_limit` on both request and
  scenario assertion DTOs.
- `ScenarioRunnerContentAssetTests` for the existing expression evaluator
  matching `response == reject` in a fake content asset response.

Frobby verification:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter ContentAssetProjector --no-restore --nologo
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter ContentAssetSerialization --no-restore --nologo
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter ContentAsset --no-restore --nologo
dotnet test sdv-test-framework.slnx --no-restore --nologo
```

SVE verification:

- Run scenario 41 headlessly with the Slice 34 Frobby worktree.
- Run adjacent movie scenarios 36, 38, 39, 40, and 41 headlessly.
- Do not merge SVE to `master`; keep SVE on its feature branch unless the user
  explicitly says otherwise.

## Acceptance Criteria

- Existing `content.asset` collection `count` assertions still pass.
- `asset.entries.<key>.value.<collection>.items contains response 'reject'`
  works for runtime Content Patcher data.
- `nested_items_limit` is serialized, schema-valid, documented, and enforced.
- Frobby focused and full unit tests pass.
- SVE scenario 41 passes with the stricter reject-response content assertion.
- Adjacent SVE movie scenarios still pass headlessly.
