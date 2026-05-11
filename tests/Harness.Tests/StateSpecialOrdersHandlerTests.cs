using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class StateSpecialOrdersHandlerTests
{
    [Fact]
    public void Handle_ProjectsActiveAvailableCompletedAndReturnedDonations()
    {
        var world = new FakeSpecialOrdersWorld();
        world.ActiveItems.Add(new FakeSpecialOrder
        {
            Key = "Andy",
            Name = "For The Farm",
            Description = "Bring supplies.",
            Requester = "Andy",
            OrderType = "StardewValleyExpanded",
            SpecialRule = "",
            Duration = "TwoWeeks",
            DueDate = 42,
            State = "InProgress",
            ReadyForRemoval = false,
            IsTimed = false,
            RuntimeType = "SpecialOrder",
            SelectedRandomElements = { ["Treasure"] = "0" },
            PreselectedItems = { ["FishType"] = "(O)136" },
            Objectives =
            {
                new FakeSpecialOrderObjective
                {
                    Type = "Donate",
                    RuntimeType = "DonateObjective",
                    Description = "Bring wood.",
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
                new FakeSpecialOrderReward
                {
                    Type = "MoneyReward",
                    RuntimeType = "MoneyReward",
                    Amount = 5362,
                    Mail = { "AndyCellar" },
                },
            },
            DonatedItems =
            {
                new FakeSpecialOrderItem
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
        });
        world.AvailableItems.Add(new FakeSpecialOrder { Key = "MarlonFay2", Requester = "MarlonFay" });
        world.CompletedItems.Add("Andy");
        world.AcceptedTypeItems.Add("StardewValleyExpanded");
        world.ReturnedDonationItems.Add(
            new FakeSpecialOrderItem { Id = "(O)390", ItemId = "390", QualifiedId = "(O)390", Name = "Stone", Stack = 1 });

        var result = StateSpecialOrdersHandler.Handle(paramsElement: null, world);
        var state = JsonSerializer.Deserialize<SpecialOrdersState>(result.GetRawText(), ProtocolJson.Options)!;

        Assert.Collection(state.Active, order =>
        {
            Assert.Equal("Andy", order.Key);
            Assert.Equal("For The Farm", order.Name);
            Assert.Equal("Bring supplies.", order.Description);
            Assert.Equal("Andy", order.Requester);
            Assert.Equal("StardewValleyExpanded", order.OrderType);
            Assert.Equal("", order.SpecialRule);
            Assert.Equal("TwoWeeks", order.Duration);
            Assert.Equal(42, order.DueDate);
            Assert.Equal("InProgress", order.State);
            Assert.False(order.ReadyForRemoval);
            Assert.False(order.IsTimed);
            Assert.Equal("SpecialOrder", order.RuntimeType);
            Assert.Collection(order.SelectedRandomElements, item =>
            {
                Assert.Equal("Treasure", item.Key);
                Assert.Equal("0", item.Value);
            });
            Assert.Collection(order.PreselectedItems, item =>
            {
                Assert.Equal("FishType", item.Key);
                Assert.Equal("(O)136", item.Value);
            });
            Assert.Collection(order.Objectives, objective =>
            {
                Assert.Equal(0, objective.Index);
                Assert.Equal("Donate", objective.Type);
                Assert.Equal("DonateObjective", objective.RuntimeType);
                Assert.Equal("Bring wood.", objective.Description);
                Assert.Equal(25, objective.CurrentCount);
                Assert.Equal(500, objective.MaxCount);
                Assert.False(objective.Complete);
                Assert.Equal("AndyChest", objective.DropBox);
                Assert.Equal("Custom_AndyHouse", objective.DropBoxLocation);
                Assert.Equal(12, objective.DropBoxTile!.X);
                Assert.Equal(5, objective.DropBoxTile.Y);
                Assert.Contains("item_wood", objective.AcceptedContextTags);
                Assert.False(objective.Confirmed);
                Assert.Equal(-1, objective.MinimumCapacity);
            });
            Assert.Collection(order.Rewards, reward =>
            {
                Assert.Equal(0, reward.Index);
                Assert.Equal("MoneyReward", reward.Type);
                Assert.Equal("MoneyReward", reward.RuntimeType);
                Assert.Equal(5362, reward.Amount);
                Assert.Contains("AndyCellar", reward.Mail);
            });
            Assert.Collection(order.DonatedItems, item =>
            {
                Assert.Equal("(O)388", item.Id);
                Assert.Equal("388", item.ItemId);
                Assert.Equal("(O)388", item.QualifiedId);
                Assert.Equal("Wood", item.Name);
                Assert.Equal(25, item.Stack);
                Assert.Equal(0, item.Quality);
                Assert.Equal(-15, item.Category);
                Assert.Equal("Object", item.RuntimeType);
            });
        });
        Assert.Collection(state.Available, order => Assert.Equal("MarlonFay2", order.Key));
        Assert.Contains("Andy", state.Completed);
        Assert.Contains("StardewValleyExpanded", state.AcceptedTypes);
        Assert.Collection(state.ReturnedDonations, item => Assert.Equal("(O)390", item.QualifiedId));
    }

    [Fact]
    public void Handle_ToleratesSparseUnknownRuntimeTypes()
    {
        var world = new FakeSpecialOrdersWorld();
        world.ActiveItems.Add(new FakeSpecialOrder
        {
            Key = "UnknownOrder",
            RuntimeType = "ModdedOrder",
            Objectives = { new FakeSpecialOrderObjective { RuntimeType = "CustomObjective" } },
            Rewards = { new FakeSpecialOrderReward { RuntimeType = "CustomReward" } },
        });

        var result = StateSpecialOrdersHandler.Handle(paramsElement: null, world);
        var state = JsonSerializer.Deserialize<SpecialOrdersState>(result.GetRawText(), ProtocolJson.Options)!;

        Assert.Equal("UnknownOrder", state.Active[0].Key);
        Assert.Equal("ModdedOrder", state.Active[0].RuntimeType);
        Assert.Equal("CustomObjective", state.Active[0].Objectives[0].RuntimeType);
        Assert.Equal("CustomReward", state.Active[0].Rewards[0].RuntimeType);
    }

    private sealed class FakeSpecialOrdersWorld : ISpecialOrdersWorld
    {
        public List<ISpecialOrderSource> ActiveItems { get; } = new();
        public List<ISpecialOrderSource> AvailableItems { get; } = new();
        public List<string> CompletedItems { get; } = new();
        public List<string> AcceptedTypeItems { get; } = new();
        public List<ISpecialOrderItemSource> ReturnedDonationItems { get; } = new();

        public IReadOnlyList<ISpecialOrderSource> Active => ActiveItems;
        public IReadOnlyList<ISpecialOrderSource> Available => AvailableItems;
        public IReadOnlyList<string> Completed => CompletedItems;
        public IReadOnlyList<string> AcceptedTypes => AcceptedTypeItems;
        public IReadOnlyList<ISpecialOrderItemSource> ReturnedDonations => ReturnedDonationItems;
    }

    private sealed class FakeSpecialOrder : ISpecialOrderSource
    {
        public string Key { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Requester { get; init; } = string.Empty;
        public string OrderType { get; init; } = string.Empty;
        public string SpecialRule { get; init; } = string.Empty;
        public string Duration { get; init; } = string.Empty;
        public int? DueDate { get; init; }
        public string State { get; init; } = string.Empty;
        public bool? ReadyForRemoval { get; init; }
        public bool? IsTimed { get; init; }
        public string RuntimeType { get; init; } = string.Empty;
        public Dictionary<string, string> SelectedRandomElements { get; } = new();
        public Dictionary<string, string> PreselectedItems { get; } = new();
        public List<ISpecialOrderObjectiveSource> Objectives { get; } = new();
        public List<ISpecialOrderRewardSource> Rewards { get; } = new();
        public List<ISpecialOrderItemSource> DonatedItems { get; } = new();

        IReadOnlyDictionary<string, string> ISpecialOrderSource.SelectedRandomElements => SelectedRandomElements;
        IReadOnlyDictionary<string, string> ISpecialOrderSource.PreselectedItems => PreselectedItems;
        IReadOnlyList<ISpecialOrderObjectiveSource> ISpecialOrderSource.Objectives => Objectives;
        IReadOnlyList<ISpecialOrderRewardSource> ISpecialOrderSource.Rewards => Rewards;
        IReadOnlyList<ISpecialOrderItemSource> ISpecialOrderSource.DonatedItems => DonatedItems;
    }

    private sealed class FakeSpecialOrderObjective : ISpecialOrderObjectiveSource
    {
        public string Type { get; init; } = string.Empty;
        public string RuntimeType { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public int? CurrentCount { get; init; }
        public int? MaxCount { get; init; }
        public bool? Complete { get; init; }
        public string DropBox { get; init; } = string.Empty;
        public string DropBoxLocation { get; init; } = string.Empty;
        public TilePoint? DropBoxTile { get; init; }
        public string TargetName { get; init; } = string.Empty;
        public List<string> AcceptedContextTags { get; } = new();
        public bool? Confirmed { get; init; }
        public int? MinimumCapacity { get; init; }

        IReadOnlyList<string> ISpecialOrderObjectiveSource.AcceptedContextTags => AcceptedContextTags;
    }

    private sealed class FakeSpecialOrderReward : ISpecialOrderRewardSource
    {
        public string Type { get; init; } = string.Empty;
        public string RuntimeType { get; init; } = string.Empty;
        public int? Amount { get; init; }
        public List<string> Mail { get; } = new();

        IReadOnlyList<string> ISpecialOrderRewardSource.Mail => Mail;
    }

    private sealed class FakeSpecialOrderItem : ISpecialOrderItemSource
    {
        public string Id { get; init; } = string.Empty;
        public string ItemId { get; init; } = string.Empty;
        public string QualifiedId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int Stack { get; init; }
        public int? Quality { get; init; }
        public int? Category { get; init; }
        public string RuntimeType { get; init; } = string.Empty;
    }
}
