# SVE Slice 34 Content Asset Nested Collections Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add neutral bounded nested collection item projection to `content.asset`, then use it to assert SVE Martin's runtime movie reaction data contains `response == "reject"`.

**Architecture:** Keep `content.asset` entry-key-first and bounded: selected runtime data entries still expose scalar fields, nested objects, and collection counts, while list/array-like nested collections gain capped `items`. Reuse the existing asset expression evaluator, because it already supports `array contains field 'literal'` once `items` is present.

**Tech Stack:** .NET 10, xUnit, System.Text.Json, Frobby JSON-RPC protocol DTOs, Frobby harness `ContentAssetProjector`, SVE headless scenario runner.

---

## File Structure

- Modify `src/Protocol/Models/ContentAssetRequest.cs`: add `NestedItemsLimit` request field.
- Modify `src/Protocol/Models/ScenarioAssertion.cs`: add `NestedItemsLimit` scenario assertion field.
- Modify `src/Harness/Assets/ContentAssetProjector.cs`: validate `nested_items_limit` and project bounded collection `items`.
- Modify `src/Runner.Mcp/Scenarios/ScenarioAssertionEvaluator.cs`: pass assertion `NestedItemsLimit` into the `content.asset` request.
- Modify `schemas/scenario.schema.json`: accept `nested_items_limit` on assertions.
- Modify `tests/Protocol.Tests/ContentAssetSerializationTests.cs`: lock snake_case protocol serialization.
- Modify `tests/Harness.Tests/ContentAssetProjectorTests.cs`: prove collection item projection and cap behavior.
- Modify `tests/Runner.Tests/ScenarioRunnerContentAssetTests.cs`: prove scenario assertions can match nested item fields and pass request caps.
- Modify `docs/rpc-schema.md`: document `nested_items_limit` and collection `items`.
- Modify `docs/wiki/examples.md`: mention the nested content assertion pattern near movie reaction examples.
- Modify `SVE_FROBBY_CAPABILITY_TODO.md`: add Slice 34 status.
- Modify `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/41-sve-martin-movie-worker-ticket-reject.test.json`: add the stricter reject-response assertion.
- Modify `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`: note scenario 41 validates runtime reject reaction data.

Implementation worktree:

```bash
/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-34-content-asset-nested-collections
```

SVE repository stays on its current feature branch. Do not merge SVE to `master`.

---

## Task 1: Protocol DTO And Schema Surface

**Files:**
- Modify: `tests/Protocol.Tests/ContentAssetSerializationTests.cs`
- Modify: `src/Protocol/Models/ContentAssetRequest.cs`
- Modify: `src/Protocol/Models/ScenarioAssertion.cs`
- Modify: `schemas/scenario.schema.json`
- Modify: `src/Runner.Mcp/Scenarios/ScenarioAssertionEvaluator.cs`

- [ ] **Step 1: Write the failing protocol serialization expectations**

In `tests/Protocol.Tests/ContentAssetSerializationTests.cs`, update `Request_SerializesSnakeCaseFields`:

```csharp
var req = new ContentAssetRequest
{
    Name = "Data/Locations",
    AssetType = "data",
    IncludeKeys = true,
    KeysLimit = 25,
    EntryKeys = new[] { "Custom_TownEast" },
    HashTexture = true,
    NestedItemsLimit = 10,
};
```

Add this assertion to the same test:

```csharp
Assert.Contains("\"nested_items_limit\":10", json);
```

Update `ScenarioAssertion_SerializesContentAssetFields`:

```csharp
var assertion = new ScenarioAssertion
{
    Type = "content.asset",
    Asset = "Maps/Custom_TownEast",
    AssetType = "map",
    Expr = "asset.layers contains name 'Back'",
    IncludeKeys = true,
    KeysLimit = 10,
    EntryKeys = new[] { "Custom_TownEast" },
    HashTexture = false,
    NestedItemsLimit = 10,
};
```

Add this assertion to the same test:

```csharp
Assert.Contains("\"nested_items_limit\":10", json);
```

- [ ] **Step 2: Run protocol tests and verify the compile failure**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter ContentAssetSerialization --no-restore --nologo
```

Expected: FAIL to compile because `ContentAssetRequest.NestedItemsLimit` and
`ScenarioAssertion.NestedItemsLimit` do not exist yet.

- [ ] **Step 3: Add DTO properties**

In `src/Protocol/Models/ContentAssetRequest.cs`, add this property after
`KeysLimit`:

```csharp
public int? NestedItemsLimit { get; set; }
```

In `src/Protocol/Models/ScenarioAssertion.cs`, add this property after
`KeysLimit`:

```csharp
/// <summary>For <c>content.asset</c> data assertions: max nested collection items to include per summarized collection.</summary>
public int? NestedItemsLimit { get; set; }
```

- [ ] **Step 4: Pass assertion cap into content asset requests**

In `src/Runner.Mcp/Scenarios/ScenarioAssertionEvaluator.cs`, update the
`ContentAssetRequest` initializer in `EvaluateContentAssetAssertionAsync`:

```csharp
var request = ProtocolJson.ToElement(new ContentAssetRequest
{
    Name = assertion.Asset,
    AssetType = assertion.AssetType,
    IncludeKeys = assertion.IncludeKeys ?? false,
    KeysLimit = assertion.KeysLimit,
    NestedItemsLimit = assertion.NestedItemsLimit,
    EntryKeys = assertion.EntryKeys,
    HashTexture = assertion.HashTexture ?? false,
});
```

- [ ] **Step 5: Update scenario schema**

In `schemas/scenario.schema.json`, add this property after `keys_limit`:

```json
"nested_items_limit": { "type": "integer", "minimum": 1, "maximum": 100 },
```

- [ ] **Step 6: Run protocol tests and verify they pass**

Run:

```bash
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter ContentAssetSerialization --no-restore --nologo
```

Expected: PASS.

- [ ] **Step 7: Commit Task 1**

```bash
git add src/Protocol/Models/ContentAssetRequest.cs src/Protocol/Models/ScenarioAssertion.cs src/Runner.Mcp/Scenarios/ScenarioAssertionEvaluator.cs schemas/scenario.schema.json tests/Protocol.Tests/ContentAssetSerializationTests.cs
git commit -m "feat: add content asset nested items limit field"
```

---

## Task 2: Harness Projection For Nested Collection Items

**Files:**
- Modify: `tests/Harness.Tests/ContentAssetProjectorTests.cs`
- Modify: `src/Harness/Assets/ContentAssetProjector.cs`

- [ ] **Step 1: Add test fixture classes**

In `tests/Harness.Tests/ContentAssetProjectorTests.cs`, add these private
classes near the existing runtime test classes:

```csharp
private sealed class RuntimeMovieReactionLike
{
    public string NPCName { get; init; } = string.Empty;
    public List<RuntimeMovieReactionEntry> Reactions { get; init; } = new();
}

private sealed class RuntimeMovieReactionEntry
{
    public string Tag { get; init; } = string.Empty;
    public string Response { get; init; } = string.Empty;
    public string ID { get; init; } = string.Empty;
    public List<string> Whitelist { get; init; } = new();
}
```

- [ ] **Step 2: Add failing nested object item projection test**

Add this test to `ContentAssetProjectorTests`:

```csharp
[Fact]
public void Project_DataDictionary_SummarizesNestedCollectionItems()
{
    var loader = new FakeLoader();
    loader.Add("Data/MoviesReactions", new Dictionary<string, object>
    {
        ["Martin"] = new RuntimeMovieReactionLike
        {
            NPCName = "Martin",
            Reactions = new List<RuntimeMovieReactionEntry>
            {
                new()
                {
                    Tag = "*",
                    Response = "reject",
                    ID = "reaction_0",
                    Whitelist = new List<string>(),
                },
            },
        },
    });

    var result = ContentAssetProjector.Project(loader, new ContentAssetRequest
    {
        Name = "Data/MoviesReactions",
        AssetType = "data",
        EntryKeys = new[] { "Martin" },
        NestedItemsLimit = 10,
    });

    Assert.True(result.Exists);
    var entries = Assert.IsType<System.Text.Json.Nodes.JsonObject>(result.Summary["entries"]);
    var reactions = entries["Martin"]!["value"]!["reactions"]!;
    Assert.Equal(1, reactions["count"]!.GetValue<int>());
    Assert.Equal(10, reactions["items_limit"]!.GetValue<int>());
    Assert.False(reactions["items_truncated"]!.GetValue<bool>());
    var items = Assert.IsType<System.Text.Json.Nodes.JsonArray>(reactions["items"]);
    Assert.Single(items);
    Assert.Equal("*", items[0]!["tag"]!.GetValue<string>());
    Assert.Equal("reject", items[0]!["response"]!.GetValue<string>());
    Assert.Equal("reaction_0", items[0]!["i_d"]!.GetValue<string>());
    Assert.Equal(0, items[0]!["whitelist"]!["count"]!.GetValue<int>());
    var whitelistItems = Assert.IsType<System.Text.Json.Nodes.JsonArray>(items[0]!["whitelist"]!["items"]);
    Assert.Empty(whitelistItems);
}
```

The current snake_case converter renders `ID` as `i_d`. Keep that behavior in
this slice; do not introduce a naming refactor here.

- [ ] **Step 3: Add failing scalar item and cap tests**

Replace the existing `Project_DataDictionary_SummarizesNestedCollectionCounts`
body with this stronger assertion:

```csharp
[Fact]
public void Project_DataDictionary_SummarizesNestedCollectionCountsAndScalarItems()
{
    var loader = new FakeLoader();
    loader.Add("Data/Example", new Dictionary<string, object>
    {
        ["ExampleEntry"] = new RuntimeCollectionEntry
        {
            Name = "Example",
            Tags = new List<string> { "alpha", "beta", "gamma" },
        },
    });

    var result = ContentAssetProjector.Project(loader, new ContentAssetRequest
    {
        Name = "Data/Example",
        AssetType = "data",
        EntryKeys = new[] { "ExampleEntry" },
        NestedItemsLimit = 2,
    });

    Assert.True(result.Exists);
    var entries = Assert.IsType<System.Text.Json.Nodes.JsonObject>(result.Summary["entries"]);
    var value = entries["ExampleEntry"]!["value"]!;
    Assert.Equal("Example", value["name"]!.GetValue<string>());
    var tags = value["tags"]!;
    Assert.Equal(3, tags["count"]!.GetValue<int>());
    Assert.Equal(2, tags["items_limit"]!.GetValue<int>());
    Assert.True(tags["items_truncated"]!.GetValue<bool>());
    var items = Assert.IsType<System.Text.Json.Nodes.JsonArray>(tags["items"]);
    Assert.Equal(2, items.Count);
    Assert.Equal("alpha", items[0]!.GetValue<string>());
    Assert.Equal("beta", items[1]!.GetValue<string>());
}
```

Add invalid limit coverage:

```csharp
[Theory]
[InlineData(0)]
[InlineData(101)]
public void Project_RejectsInvalidNestedItemsLimit(int limit)
{
    var ex = Assert.Throws<JsonRpcException>(() =>
        ContentAssetProjector.Project(new FakeLoader(), new ContentAssetRequest
        {
            Name = "Data/Example",
            AssetType = "data",
            NestedItemsLimit = limit,
        }));

    Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    Assert.Contains("nested_items_limit", ex.Message);
}
```

- [ ] **Step 4: Run harness tests and verify failure**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter ContentAssetProjector --no-restore --nologo
```

Expected: FAIL because nested collection summaries do not expose `items`,
`items_limit`, or `items_truncated`, and invalid `nested_items_limit` is not
validated.

- [ ] **Step 5: Add projector constants and validation**

In `src/Harness/Assets/ContentAssetProjector.cs`, add constants near
`MaxObjectDepth`:

```csharp
private const int DefaultNestedItemsLimit = 25;
private const int MaxNestedItemsLimit = 100;
```

In `Validate`, add:

```csharp
if (req.NestedItemsLimit is < 1 or > MaxNestedItemsLimit)
    throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "nested_items_limit must be between 1 and 100");
```

- [ ] **Step 6: Pass the cap into selected entry value summaries**

In `SummarizeDictionary<T>`, resolve the limit and pass it into
`SummarizeValue`:

```csharp
var limit = req.KeysLimit ?? 50;
var nestedItemsLimit = req.NestedItemsLimit ?? DefaultNestedItemsLimit;
```

Update the selected entry assignment:

```csharp
["value"] = SummarizeValue(value, nestedItemsLimit),
```

- [ ] **Step 7: Replace `SummarizeValue` with capped collection support**

Change the signature:

```csharp
private static JsonNode? SummarizeValue(object? value, int nestedItemsLimit, int depth = 0)
```

Use this implementation for the enumerable branch:

```csharp
if (value is IEnumerable enumerable and not string)
{
    var count = 0;
    var items = new JsonArray();
    var includeItems = ShouldSummarizeEnumerableItems(value);
    foreach (var item in enumerable)
    {
        if (includeItems && count < nestedItemsLimit)
            items.Add(SummarizeValue(item, nestedItemsLimit, depth + 1));
        count++;
    }

    var collection = new JsonObject
    {
        ["runtime_type"] = value.GetType().FullName ?? value.GetType().Name,
        ["count"] = count,
    };

    if (includeItems)
    {
        collection["items_limit"] = nestedItemsLimit;
        collection["items_truncated"] = count > nestedItemsLimit;
        collection["items"] = items;
    }

    return collection;
}
```

Update every recursive call in the method:

```csharp
obj[ToSnakeCase(prop.Name)] = SummarizeValue(propValue, nestedItemsLimit);
obj[ToSnakeCase(prop.Name)] = SummarizeValue(propValue, nestedItemsLimit, depth + 1);
obj[ToSnakeCase(field.Name)] = SummarizeValue(fieldValue, nestedItemsLimit);
obj[ToSnakeCase(field.Name)] = SummarizeValue(fieldValue, nestedItemsLimit, depth + 1);
```

For enumerable properties and fields, pass the current object depth instead of
resetting to zero:

```csharp
obj[ToSnakeCase(prop.Name)] = SummarizeValue(propValue, nestedItemsLimit, depth);
obj[ToSnakeCase(field.Name)] = SummarizeValue(fieldValue, nestedItemsLimit, depth);
```

Add this helper near `ShouldSummarizeNestedObject`:

```csharp
private static bool ShouldSummarizeEnumerableItems(object value)
{
    if (value is IDictionary)
        return false;

    var type = value.GetType();
    if (type == typeof(string))
        return false;

    return true;
}
```

- [ ] **Step 8: Ensure top-level `SummarizeValue` calls compile**

After changing the signature, any remaining direct calls must provide the limit.
Use this pattern:

```csharp
SummarizeValue(propValue, nestedItemsLimit, depth + 1)
```

Do not add overloads that silently use the default; callers should choose the
limit explicitly so future projection changes stay bounded.

- [ ] **Step 9: Run harness tests and verify pass**

Run:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter ContentAssetProjector --no-restore --nologo
```

Expected: PASS.

- [ ] **Step 10: Commit Task 2**

```bash
git add src/Harness/Assets/ContentAssetProjector.cs tests/Harness.Tests/ContentAssetProjectorTests.cs
git commit -m "feat: project nested content asset collection items"
```

---

## Task 3: Runner Content Assertion Coverage

**Files:**
- Modify: `tests/Runner.Tests/ScenarioRunnerContentAssetTests.cs`

- [ ] **Step 1: Add a test that requires nested item matching and request pass-through**

Add this test to `ScenarioRunnerContentAssetTests`:

```csharp
[Fact]
public async Task ContentAssetAssertion_EvaluatesNestedCollectionItemExpression()
{
    var (cts, server, client, calls) = await StartFakeHarness(SocketPath(), req =>
    {
        if (req.Method == "content.asset")
        {
            Assert.NotNull(req.Params);
            Assert.True(req.Params.Value.TryGetProperty("nested_items_limit", out var limit));
            Assert.Equal(10, limit.GetInt32());
        }

        return """
        {
          "name": "Data/MoviesReactions",
          "exists": true,
          "kind": "data",
          "runtime_type": "Dictionary\u00602",
          "summary": {
            "entries": {
              "Martin": {
                "exists": true,
                "value": {
                  "npc_name": "Martin",
                  "reactions": {
                    "runtime_type": "System.Collections.Generic.List\u00601",
                    "count": 1,
                    "items_limit": 10,
                    "items_truncated": false,
                    "items": [
                      { "tag": "*", "response": "reject", "i_d": "reaction_0" }
                    ]
                  }
                }
              }
            }
          }
        }
        """;
    });
    using var _ = cts;
    using var __ = client;

    var runner = new ScenarioRunner(client);
    var spec = new ScenarioSpec
    {
        Name = "content_asset_nested_collection_item",
        Assertions = new()
        {
            new ScenarioAssertion
            {
                Type = "content.asset",
                Asset = "Data/MoviesReactions",
                AssetType = "data",
                EntryKeys = new[] { "Martin" },
                NestedItemsLimit = 10,
                Expr = "asset.entries.Martin.value.reactions.items contains response 'reject'",
            },
        },
    };

    var report = await runner.RunAsync(spec, cts.Token);

    Assert.True(report.Passed, string.Join(Environment.NewLine, report.Failures));
    Assert.Equal(1, report.AssertionsPassed);
    Assert.Contains("content.asset", calls);
    cts.Cancel();
    try { await server; } catch (OperationCanceledException) { }
}
```

- [ ] **Step 2: Add a fake harness overload that can inspect requests**

Keep the existing `StartFakeHarness(string socket, string contentAssetJson)` as
a convenience wrapper, but add this overload below it:

```csharp
private static Task<(CancellationTokenSource Cts, Task Server, JsonRpcSession Client, List<string> Calls)> StartFakeHarness(
    string socket,
    Func<JsonRpcRequest, string> contentAssetJsonFactory)
{
    var calls = new List<string>();
    var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var serverTask = Task.Run(async () =>
    {
        await UnixSocketRpc.RunServerAsync(socket, async (session, tok) =>
        {
            session.RequestReceived += async req =>
            {
                calls.Add(req.Method);
                JsonElement r = req.Method switch
                {
                    "scenario.begin" => JsonDocument.Parse("{\"session_id\":\"t\",\"tick\":0}").RootElement,
                    "content.asset" => JsonDocument.Parse(contentAssetJsonFactory(req)).RootElement,
                    "scenario.end" => JsonDocument.Parse(
                        "{\"duration_ms\":10,\"assertions_run\":0,\"assertions_passed\":0}").RootElement,
                    _ => JsonDocument.Parse("{\"ok\":true}").RootElement,
                };
                await session.SendResponseAsync(JsonRpcResponse.Ok(req.Id, r), tok);
            };
            await session.SendNotificationAsync("ready",
                JsonDocument.Parse("{\"version\":\"0\"}").RootElement, tok);
            await session.RunAsync(tok);
        }, cts.Token);
    }, cts.Token);

    return ConnectFakeHarnessAsync(socket, cts, serverTask, calls);
}
```

Refactor the existing helper body so both overloads share this private method:

```csharp
private static async Task<(CancellationTokenSource Cts, Task Server, JsonRpcSession Client, List<string> Calls)> ConnectFakeHarnessAsync(
    string socket,
    CancellationTokenSource cts,
    Task serverTask,
    List<string> calls)
{
    for (var i = 0; i < 40 && !File.Exists(socket); i++)
        await Task.Delay(50, cts.Token);

    var client = await UnixSocketRpc.ConnectAsync(socket, cts.Token);
    _ = client.RunAsync(cts.Token);
    return (cts, serverTask, client, calls);
}
```

Then rewrite the original helper as:

```csharp
private static Task<(CancellationTokenSource Cts, Task Server, JsonRpcSession Client, List<string> Calls)> StartFakeHarness(
    string socket,
    string contentAssetJson)
    => StartFakeHarness(socket, _ => contentAssetJson);
```

- [ ] **Step 3: Run runner content asset tests**

Run:

```bash
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter ContentAsset --no-restore --nologo
```

Expected after Task 1 code: PASS. If it fails because `nested_items_limit` is
not in the fake harness request, fix `ScenarioAssertionEvaluator` pass-through
before continuing.

- [ ] **Step 4: Commit Task 3**

```bash
git add tests/Runner.Tests/ScenarioRunnerContentAssetTests.cs
git commit -m "test: cover nested content asset item assertions"
```

---

## Task 4: Docs, Schema Reference, And Capability Tracking

**Files:**
- Modify: `docs/rpc-schema.md`
- Modify: `docs/wiki/examples.md`
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Update RPC schema docs**

In `docs/rpc-schema.md` under `content.asset`, update the params example:

```json
{
  "name": "Data/Locations",
  "asset_type": "data",
  "include_keys": true,
  "keys_limit": 25,
  "nested_items_limit": 10,
  "entry_keys": ["ExampleTownEast"],
  "hash_texture": false
}
```

Add this bullet after `keys_limit`:

```markdown
- `nested_items_limit` - max items to include for each nested list/array-style
  collection under selected entries. Valid range: 1-100. Default: 25.
```

Replace the current collection summary sentence with:

```markdown
Selected data entries include public scalar fields/properties and bounded nested
runtime data objects, with names converted to snake_case. List/array-style
nested collections include `runtime_type`, `count`, `items_limit`,
`items_truncated`, and a bounded `items` array. Nested dictionaries remain
count-only in this slice. Keyed list-style data assets are projected by stable
entry identity where Stardew exposes one, such as movie reaction NPC names or
concession taste names.
```

Update the `InvalidParams` error bullet to mention `nested_items_limit`:

```markdown
- `InvalidParams -32602` - missing `name`, unsupported `asset_type`, invalid
  `keys_limit`, or invalid `nested_items_limit`.
```

- [ ] **Step 2: Update wiki examples**

In `docs/wiki/examples.md`, in the movie theater/NPC paragraph near scenario
41, append:

```markdown
When a runtime data entry contains nested list data, request a bounded
`nested_items_limit` and assert against `.items`; for example,
`asset.entries.Martin.value.reactions.items contains response 'reject'` proves
the applied live movie reaction data, not just the source content file.
```

- [ ] **Step 3: Add Slice 34 to capability tracking**

In `SVE_FROBBY_CAPABILITY_TODO.md`, add this slice after Slice 33:

```markdown
- [ ] Active: Slice 34, nested content asset collection assertions.
  - SVE pressure: Martin's worker-day movie rejection is represented as a
    conditional nested `Data/MoviesReactions` list item, but the current
    `content.asset` projection exposes only collection counts.
  - Frobby goal: expose bounded `items` for nested list/array-style selected
    entry values so scenarios can assert applied runtime content details without
    raw unbounded asset dumps or mod-specific helpers.
  - Design spec: `docs/superpowers/specs/2026-06-01-sve-slice-34-content-asset-nested-collections-design.md`.
  - Implementation plan: `docs/superpowers/plans/2026-06-01-sve-slice-34-content-asset-nested-collections.md`.
```

- [ ] **Step 4: Commit Task 4**

```bash
git add docs/rpc-schema.md docs/wiki/examples.md SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: document nested content asset collection assertions"
```

---

## Task 5: SVE Scenario 41 Stricter Runtime Data Assertion

**Files:**
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/41-sve-martin-movie-worker-ticket-reject.test.json`
- Modify: `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`

- [ ] **Step 1: Add the stricter assertion to scenario 41**

In `/home/fintan/stardewRepos/StardewValleyExpanded/tests/sdv/41-sve-martin-movie-worker-ticket-reject.test.json`, after the existing
`Martin movie reaction data is populated` assertion, add:

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

Ensure the JSON comma placement remains valid.

- [ ] **Step 2: Update SVE Frobby docs**

In `/home/fintan/stardewRepos/StardewValleyExpanded/docs/FROBBY.md`, extend the
scenario 41 paragraph with:

```markdown
The scenario also asserts the live runtime `Data/MoviesReactions` entry for
Martin contains a bounded nested reaction item with `response == reject`, proving
the applied Content Patcher condition rather than only the source patch file.
```

- [ ] **Step 3: Validate scenario JSON through the repo-local runner list**

Run from the SVE repo:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-34-content-asset-nested-collections ./scripts/sdv-test list
```

Expected: the command lists scenarios and does not reject scenario 41 schema.

- [ ] **Step 4: Commit SVE scenario/docs on the SVE feature branch**

Run in `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
git add tests/sdv/41-sve-martin-movie-worker-ticket-reject.test.json docs/FROBBY.md
git commit -m "test: assert Martin runtime movie reject reaction"
```

Do not merge SVE to `master`.

---

## Task 6: Verification

**Files:** None unless verification reveals a defect.

- [ ] **Step 1: Run focused Frobby tests**

From the Frobby Slice 34 worktree:

```bash
dotnet test tests/Harness.Tests/Harness.Tests.csproj --filter ContentAssetProjector --no-restore --nologo
dotnet test tests/Protocol.Tests/Protocol.Tests.csproj --filter ContentAssetSerialization --no-restore --nologo
dotnet test tests/Runner.Tests/Runner.Tests.csproj --filter ContentAsset --no-restore --nologo
```

Expected: all three commands PASS.

- [ ] **Step 2: Run full Frobby unit suite**

From the Frobby Slice 34 worktree:

```bash
dotnet test sdv-test-framework.slnx --no-restore --nologo
```

Expected: exit code 0.

- [ ] **Step 3: Run focused SVE scenario 41 headlessly**

From `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-34-content-asset-nested-collections ./scripts/sdv-test --headless --scenario tests/sdv/41-sve-martin-movie-worker-ticket-reject.test.json --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-34-final-41
```

Expected: scenario 41 PASS. The final report should include the stricter
`Martin movie reaction data contains a reject response` assertion.

- [ ] **Step 4: Run adjacent SVE movie scenarios headlessly**

From `/home/fintan/stardewRepos/StardewValleyExpanded`:

```bash
FROBBY_ROOT=/home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-34-content-asset-nested-collections ./scripts/sdv-test --headless --scenario tests/sdv/36-sve-movie-theater-npc-click.test.json --scenario tests/sdv/38-sve-movie-ticket-invite-flow.test.json --scenario tests/sdv/39-sve-movie-concession-purchase-flow.test.json --scenario tests/sdv/40-sve-movie-screening-reaction-flow.test.json --scenario tests/sdv/41-sve-martin-movie-worker-ticket-reject.test.json --report-dir /tmp/stardew-valley-expanded-frobby-results-0.1.0/slice-34-final-movie-adjacent
```

Expected: 5/5 scenarios PASS.

- [ ] **Step 5: Inspect git state**

```bash
git -C /home/fintan/stardewRepos/frobby/sdv-test-framework/.worktrees/sve-slice-34-content-asset-nested-collections status --short --branch
git -C /home/fintan/stardewRepos/StardewValleyExpanded status --short --branch
```

Expected:

- Frobby worktree clean on `feature/sve-slice-34-content-asset-nested-collections`.
- SVE clean on its feature branch, not `master`.

---

## Task 7: Mark Slice 34 Complete And Final Commit

**Files:**
- Modify: `SVE_FROBBY_CAPABILITY_TODO.md`

- [ ] **Step 1: Mark Slice 34 done**

After verification passes, change the Slice 34 entry in
`SVE_FROBBY_CAPABILITY_TODO.md` from:

```markdown
- [ ] Active: Slice 34, nested content asset collection assertions.
```

to:

```markdown
- [x] Done: Slice 34, nested content asset collection assertions.
```

Append verification notes:

```markdown
  - Done: `content.asset` selected-entry collection summaries now expose
    bounded `items`, `items_limit`, and `items_truncated` for nested
    list/array-style values while preserving existing `count` assertions.
  - Verified: SVE scenario 41 asserts Martin's runtime
    `Data/MoviesReactions` entry contains `response == reject`; adjacent movie
    scenarios 36, 38, 39, 40, and 41 pass headlessly.
```

- [ ] **Step 2: Commit completion status**

```bash
git add SVE_FROBBY_CAPABILITY_TODO.md
git commit -m "docs: mark nested content asset assertions complete"
```

- [ ] **Step 3: Leave final status for merge decision**

Report:

```text
Frobby Slice 34 branch is ready to merge after passing focused/full Frobby tests and SVE movie-adjacent headless coverage.
SVE branch has the scenario/docs assertion commit and remains unmerged to master.
```

---

## Self-Review

- Spec coverage: protocol field, schema, bounded projection, evaluator
  pass-through, tests, docs, SVE scenario, and verification are all mapped to
  tasks.
- Scope: one generic Frobby capability plus one SVE proof scenario update; no
  raw asset dump, movie-specific helper, or SVE framework special case.
- Type consistency: the plan uses `NestedItemsLimit` in C# and
  `nested_items_limit` on JSON wire/schema/docs.
- Existing behavior: `count` remains available; `items` is additive for
  list/array-style nested collections.
