using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class SpecialOrdersStateSerializationTests
{
    [Fact]
    public void Serialize_SnakeCaseFields()
    {
        var state = new SpecialOrdersState
        {
            Active =
            {
                new SpecialOrderSummary
                {
                    Key = "Andy",
                    Name = "For The Farm",
                    Description = "Bring supplies.",
                    Requester = "Andy",
                    OrderType = "StardewValleyExpanded",
                    SpecialRule = "NONE",
                    Duration = "TwoWeeks",
                    DueDate = 42,
                    State = "InProgress",
                    ReadyForRemoval = false,
                    IsTimed = false,
                    RuntimeType = "SpecialOrder",
                    SelectedRandomElements = { new SpecialOrderKeyValueSummary { Key = "Treasure", Value = "0" } },
                    PreselectedItems = { new SpecialOrderKeyValueSummary { Key = "FishType", Value = "(O)136" } },
                    Objectives =
                    {
                        new SpecialOrderObjectiveSummary
                        {
                            Index = 0,
                            Type = "Donate",
                            RuntimeType = "DonateObjective",
                            Description = "Place wood in the chest.",
                            CurrentCount = 25,
                            MaxCount = 500,
                            Complete = false,
                            DropBox = "AndyChest",
                            DropBoxLocation = "Custom_AndyHouse",
                            DropBoxTile = new TilePoint { X = 12, Y = 5 },
                            AcceptedContextTags = { "item_wood" },
                            Confirmed = false,
                            MinimumCapacity = -1,
                        },
                    },
                    Rewards =
                    {
                        new SpecialOrderRewardSummary
                        {
                            Index = 0,
                            Type = "MoneyReward",
                            RuntimeType = "MoneyReward",
                            Amount = 5362,
                            Mail = { "AndyCellar" },
                        },
                    },
                    DonatedItems =
                    {
                        new SpecialOrderItemSummary
                        {
                            Id = "(O)388",
                            ItemId = "388",
                            QualifiedId = "(O)388",
                            Name = "Wood",
                            Stack = 25,
                            Quality = 0,
                            Category = -15,
                            RuntimeType = "Object",
                        },
                    },
                },
            },
            Available = { new SpecialOrderSummary { Key = "MarlonFay2", Requester = "MarlonFay" } },
            Completed = { "Andy" },
            AcceptedTypes = { "Qi", "StardewValleyExpanded" },
            ReturnedDonations =
            {
                new SpecialOrderItemSummary { Id = "(O)388", ItemId = "388", QualifiedId = "(O)388", Name = "Wood", Stack = 1 },
            },
        };

        var json = JsonSerializer.Serialize(state, ProtocolJson.Options);

        Assert.Contains("\"active\"", json);
        Assert.Contains("\"available\"", json);
        Assert.Contains("\"completed\":[\"Andy\"]", json);
        Assert.Contains("\"accepted_types\":[\"Qi\",\"StardewValleyExpanded\"]", json);
        Assert.Contains("\"order_type\":\"StardewValleyExpanded\"", json);
        Assert.Contains("\"ready_for_removal\":false", json);
        Assert.Contains("\"selected_random_elements\":[{\"key\":\"Treasure\",\"value\":\"0\"}]", json);
        Assert.Contains("\"drop_box\":\"AndyChest\"", json);
        Assert.Contains("\"drop_box_tile\":{\"x\":12,\"y\":5}", json);
        Assert.Contains("\"accepted_context_tags\":[\"item_wood\"]", json);
        Assert.Contains("\"donated_items\":[{\"id\":\"(O)388\"", json);
        Assert.Contains("\"returned_donations\":[{\"id\":\"(O)388\"", json);
    }
}
