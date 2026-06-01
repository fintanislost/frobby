using System.Text.Json;
using System.Text.Json.Nodes;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class ContentAssetSerializationTests
{
    [Fact]
    public void Request_SerializesSnakeCaseFields()
    {
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

        var json = JsonSerializer.Serialize(req, ProtocolJson.Options);

        Assert.Contains("\"name\":\"Data/Locations\"", json);
        Assert.Contains("\"asset_type\":\"data\"", json);
        Assert.Contains("\"include_keys\":true", json);
        Assert.Contains("\"keys_limit\":25", json);
        Assert.Contains("\"nested_items_limit\":10", json);
        Assert.Contains("\"entry_keys\":[\"Custom_TownEast\"]", json);
        Assert.Contains("\"hash_texture\":true", json);
    }

    [Fact]
    public void Result_PreservesExactSummaryKeys()
    {
        var summary = new JsonObject
        {
            ["entries"] = new JsonObject
            {
                ["Custom_TownEast"] = new JsonObject
                {
                    ["exists"] = true,
                },
            },
        };
        var result = new ContentAssetResult
        {
            Name = "Data/Locations",
            Exists = true,
            Kind = "data",
            RuntimeType = "System.Collections.Generic.Dictionary`2",
            Summary = summary,
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Contains("\"runtime_type\":\"System.Collections.Generic.Dictionary", json);
        Assert.Contains("\"Custom_TownEast\"", json);
        Assert.DoesNotContain("custom_town_east", json);
    }

    [Fact]
    public void ScenarioAssertion_SerializesContentAssetFields()
    {
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

        var json = JsonSerializer.Serialize(assertion, ProtocolJson.Options);

        Assert.Contains("\"type\":\"content.asset\"", json);
        Assert.Contains("\"asset\":\"Maps/Custom_TownEast\"", json);
        Assert.Contains("\"asset_type\":\"map\"", json);
        Assert.Contains("\"include_keys\":true", json);
        Assert.Contains("\"keys_limit\":10", json);
        Assert.Contains("\"nested_items_limit\":10", json);
        Assert.Contains("\"entry_keys\":[\"Custom_TownEast\"]", json);
        Assert.Contains("\"hash_texture\":false", json);
    }
}
