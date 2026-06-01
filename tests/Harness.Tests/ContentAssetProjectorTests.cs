using System;
using System.Collections;
using System.Collections.Generic;
using SdvTestFramework.Harness.Assets;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Models;
using StardewValley.GameData;
using StardewValley.GameData.Locations;
using StardewValley.GameData.Movies;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class ContentAssetProjectorTests
{
    private sealed class RuntimeLocationEntry
    {
        public string DisplayName { get; init; } = string.Empty;
        public RuntimeCreateOnLoadData? CreateOnLoad { get; init; }
    }

    private sealed class RuntimeCreateOnLoadData
    {
        public bool AlwaysActive { get; init; }
        public string MapPath { get; init; } = string.Empty;
    }

    private sealed class RuntimeCollectionEntry
    {
        public string Name { get; init; } = string.Empty;
        public List<string> Tags { get; init; } = new();
    }

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

    private sealed class RuntimeDictionaryEntry
    {
        public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    }

    private sealed class RuntimeNestedCollectionEntry
    {
        public List<object> Children { get; init; } = new();
    }

    private sealed class RuntimeReadOnlyDictionaryOnly : IReadOnlyDictionary<string, string>
    {
        private readonly Dictionary<string, string> _inner;

        public RuntimeReadOnlyDictionaryOnly(Dictionary<string, string> inner)
            => _inner = inner;

        public string this[string key] => _inner[key];
        public IEnumerable<string> Keys => _inner.Keys;
        public IEnumerable<string> Values => _inner.Values;
        public int Count => _inner.Count;
        public bool ContainsKey(string key) => _inner.ContainsKey(key);
        public bool TryGetValue(string key, out string value) => _inner.TryGetValue(key, out value!);
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _inner.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class FakeLoader : IContentAssetLoader
    {
        private readonly Dictionary<(Type Type, string Name), object> _assets = new();

        public void Add<T>(string name, T asset) where T : notnull
            => _assets[(typeof(T), name)] = asset;

        public bool TryLoad<T>(string name, out T? asset) where T : notnull
        {
            if (_assets.TryGetValue((typeof(T), name), out var value))
            {
                asset = (T)value;
                return true;
            }
            asset = default;
            return false;
        }
    }

    [Fact]
    public void Project_RequiresName()
    {
        var ex = Assert.Throws<JsonRpcException>(() =>
            ContentAssetProjector.Project(new FakeLoader(), new ContentAssetRequest()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void Project_RejectsExcessiveKeyLimit()
    {
        var ex = Assert.Throws<JsonRpcException>(() =>
            ContentAssetProjector.Project(new FakeLoader(), new ContentAssetRequest
            {
                Name = "Data/Locations",
                AssetType = "data",
                KeysLimit = 501,
            }));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("keys_limit", ex.Message);
    }

    [Fact]
    public void Project_DataDictionary_SummarizesKeysAndSelectedEntries()
    {
        var loader = new FakeLoader();
        loader.Add("Data/Locations", new Dictionary<string, string>
        {
            ["Custom_TownEast"] = "Town East payload",
            ["Custom_GrandpasShed"] = "Shed payload",
        });

        var result = ContentAssetProjector.Project(loader, new ContentAssetRequest
        {
            Name = "Data/Locations",
            AssetType = "data",
            IncludeKeys = true,
            KeysLimit = 10,
            EntryKeys = new[] { "Custom_TownEast", "Missing_Key" },
        });

        Assert.True(result.Exists);
        Assert.Equal("data", result.Kind);
        Assert.Equal(2, result.Summary["count"]!.GetValue<int>());
        var keys = Assert.IsType<System.Text.Json.Nodes.JsonArray>(result.Summary["keys"]);
        Assert.Contains(keys, node => node?.GetValue<string>() == "Custom_TownEast");
        var entries = Assert.IsType<System.Text.Json.Nodes.JsonObject>(result.Summary["entries"]);
        Assert.True(entries["Custom_TownEast"]!["exists"]!.GetValue<bool>());
        Assert.Equal("Town East payload", entries["Custom_TownEast"]!["value"]!.GetValue<string>());
        Assert.False(entries["Missing_Key"]!["exists"]!.GetValue<bool>());
    }

    [Fact]
    public void Project_DataDictionary_SummarizesLocationDataScalarProperties()
    {
        var loader = new FakeLoader();
        loader.Add("Data/Locations", new Dictionary<string, LocationData>
        {
            ["Custom_TownEast"] = new() { DisplayName = "Town East", CanPlantHere = false },
        });

        var result = ContentAssetProjector.Project(loader, new ContentAssetRequest
        {
            Name = "Data/Locations",
            AssetType = "data",
            EntryKeys = new[] { "Custom_TownEast" },
        });

        Assert.True(result.Exists);
        var entries = Assert.IsType<System.Text.Json.Nodes.JsonObject>(result.Summary["entries"]);
        var entry = entries["Custom_TownEast"]!;
        Assert.True(entry["exists"]!.GetValue<bool>());
        Assert.Equal("Town East", entry["value"]!["display_name"]!.GetValue<string>());
        Assert.False(entry["value"]!["can_plant_here"]!.GetValue<bool>());
    }

    [Fact]
    public void Project_DataDictionary_SummarizesAdditionalFarmKeys()
    {
        var loader = new FakeLoader();
        loader.Add("Data/AdditionalFarms", new Dictionary<string, ModFarmType>
        {
            ["Example.Mod/ExampleFarm"] = new(),
        });

        var result = ContentAssetProjector.Project(loader, new ContentAssetRequest
        {
            Name = "Data/AdditionalFarms",
            AssetType = "data",
            IncludeKeys = true,
            KeysLimit = 10,
            EntryKeys = new[] { "Example.Mod/ExampleFarm" },
        });

        Assert.True(result.Exists);
        Assert.Equal("data", result.Kind);
        var keys = Assert.IsType<System.Text.Json.Nodes.JsonArray>(result.Summary["keys"]);
        Assert.Contains(keys, node => node?.GetValue<string>() == "Example.Mod/ExampleFarm");
        var entries = Assert.IsType<System.Text.Json.Nodes.JsonObject>(result.Summary["entries"]);
        var entry = entries["Example.Mod/ExampleFarm"]!;
        Assert.True(entry["exists"]!.GetValue<bool>());
        Assert.NotNull(entry["value"]);
    }

    [Fact]
    public void Project_DataList_SummarizesMovieTheaterDataKeys()
    {
        var loader = new FakeLoader();
        loader.Add("Data/MoviesReactions", new List<MovieCharacterReaction>
        {
            new() { NPCName = "Sophia" },
        });
        loader.Add("Data/ConcessionTastes", new List<ConcessionTaste>
        {
            new() { Name = "Sophia" },
        });

        var reactions = ContentAssetProjector.Project(loader, new ContentAssetRequest
        {
            Name = "Data/MoviesReactions",
            AssetType = "data",
            IncludeKeys = true,
            EntryKeys = new[] { "Sophia" },
        });
        var concessions = ContentAssetProjector.Project(loader, new ContentAssetRequest
        {
            Name = "Data/ConcessionTastes",
            AssetType = "data",
            IncludeKeys = true,
            EntryKeys = new[] { "Sophia" },
        });

        Assert.True(reactions.Exists);
        Assert.Equal("data", reactions.Kind);
        var reactionKeys = Assert.IsType<System.Text.Json.Nodes.JsonArray>(reactions.Summary["keys"]);
        Assert.Contains(reactionKeys, node => node?.GetValue<string>() == "Sophia");
        Assert.True(reactions.Summary["entries"]!["Sophia"]!["exists"]!.GetValue<bool>());
        Assert.True(concessions.Exists);
        Assert.Equal("data", concessions.Kind);
        var concessionKeys = Assert.IsType<System.Text.Json.Nodes.JsonArray>(concessions.Summary["keys"]);
        Assert.Contains(concessionKeys, node => node?.GetValue<string>() == "Sophia");
        Assert.True(concessions.Summary["entries"]!["Sophia"]!["exists"]!.GetValue<bool>());
    }

    [Fact]
    public void Project_DataDictionary_SummarizesNestedDataObjects()
    {
        var loader = new FakeLoader();
        loader.Add("Data/Locations", new Dictionary<string, object>
        {
            ["Custom_EnchantedGrove"] = new RuntimeLocationEntry
            {
                DisplayName = "Enchanted Grove",
                CreateOnLoad = new RuntimeCreateOnLoadData
                {
                    AlwaysActive = false,
                    MapPath = "Maps\\Custom_EnchantedGrove",
                },
            },
        });

        var result = ContentAssetProjector.Project(loader, new ContentAssetRequest
        {
            Name = "Data/Locations",
            AssetType = "data",
            EntryKeys = new[] { "Custom_EnchantedGrove" },
        });

        Assert.True(result.Exists);
        var entries = Assert.IsType<System.Text.Json.Nodes.JsonObject>(result.Summary["entries"]);
        var value = entries["Custom_EnchantedGrove"]!["value"]!;
        Assert.Equal("Enchanted Grove", value["display_name"]!.GetValue<string>());
        Assert.False(value["create_on_load"]!["always_active"]!.GetValue<bool>());
        Assert.Equal("Maps\\Custom_EnchantedGrove", value["create_on_load"]!["map_path"]!.GetValue<string>());
    }

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

    [Fact]
    public void Project_DataDictionary_SummarizesNestedGenericDictionaryCountsOnly()
    {
        var loader = new FakeLoader();
        loader.Add("Data/Example", new Dictionary<string, object>
        {
            ["ExampleEntry"] = new RuntimeDictionaryEntry
            {
                Metadata = new RuntimeReadOnlyDictionaryOnly(new Dictionary<string, string>
                {
                    ["alpha"] = "one",
                    ["beta"] = "two",
                }),
            },
        });

        var result = ContentAssetProjector.Project(loader, new ContentAssetRequest
        {
            Name = "Data/Example",
            AssetType = "data",
            EntryKeys = new[] { "ExampleEntry" },
            NestedItemsLimit = 10,
        });

        var entries = Assert.IsType<System.Text.Json.Nodes.JsonObject>(result.Summary["entries"]);
        var metadata = entries["ExampleEntry"]!["value"]!["metadata"]!;
        Assert.Equal(2, metadata["count"]!.GetValue<int>());
        Assert.Null(metadata["items"]);
    }

    [Fact]
    public void Project_DataDictionary_OmitsNestedCollectionItemsAtMaxDepth()
    {
        var deepest = new RuntimeNestedCollectionEntry
        {
            Children = new List<object> { "leaf" },
        };
        var level3 = new RuntimeNestedCollectionEntry { Children = new List<object> { deepest } };
        var level2 = new RuntimeNestedCollectionEntry { Children = new List<object> { level3 } };
        var level1 = new RuntimeNestedCollectionEntry { Children = new List<object> { level2 } };

        var loader = new FakeLoader();
        loader.Add("Data/Example", new Dictionary<string, object>
        {
            ["ExampleEntry"] = level1,
        });

        var result = ContentAssetProjector.Project(loader, new ContentAssetRequest
        {
            Name = "Data/Example",
            AssetType = "data",
            EntryKeys = new[] { "ExampleEntry" },
            NestedItemsLimit = 10,
        });

        var entries = Assert.IsType<System.Text.Json.Nodes.JsonObject>(result.Summary["entries"]);
        var level1Children = entries["ExampleEntry"]!["value"]!["children"]!;
        Assert.NotNull(level1Children["items"]);

        var level2Children = level1Children["items"]![0]!["children"]!;
        Assert.NotNull(level2Children["items"]);

        var level3Children = level2Children["items"]![0]!["children"]!;
        Assert.NotNull(level3Children["items"]);

        var deepestChildren = level3Children["items"]![0]!["children"]!;
        Assert.Equal(1, deepestChildren["count"]!.GetValue<int>());
        Assert.Null(deepestChildren["items"]);
    }

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

    [Fact]
    public void Project_MissingAsset_ReturnsMissingResult()
    {
        var result = ContentAssetProjector.Project(new FakeLoader(), new ContentAssetRequest
        {
            Name = "Maps/Missing",
            AssetType = "map",
        });

        Assert.False(result.Exists);
        Assert.Equal("missing", result.Kind);
        Assert.Equal("Maps/Missing", result.Name);
    }
}
