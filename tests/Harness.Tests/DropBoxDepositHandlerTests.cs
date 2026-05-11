using System;
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class DropBoxDepositHandlerTests
{
    [Fact]
    public void Handle_DepositsMatchingInventoryIntoDonationObjective()
    {
        var world = FakeDropBoxWorld.WithOrderAndInventory();
        var req = ProtocolJson.ToElement(new DropBoxDepositRequest
        {
            OrderKey = "Andy",
            DropBox = "AndyChest",
            QualifiedId = "(O)388",
            Count = 25,
        });

        var result = DropBoxDepositHandler.Handle(req, world);
        var parsed = JsonSerializer.Deserialize<DropBoxDepositResult>(result.GetRawText(), ProtocolJson.Options)!;

        Assert.True(parsed.Ok);
        Assert.Equal("Andy", parsed.OrderKey);
        Assert.Equal("AndyChest", parsed.DropBox);
        Assert.Equal(0, parsed.BeforeCount);
        Assert.Equal(25, parsed.AfterCount);
        Assert.Equal(25, parsed.DepositedCount);
        Assert.Equal(25, world.Order.Objectives[0].CurrentCount);
        Assert.Equal(75, world.Inventory[0].Stack);
        Assert.Collection(world.Order.DonatedItems, item => Assert.Equal(25, item.Stack));
    }

    [Fact]
    public void Handle_RejectsInsufficientInventory()
    {
        var world = FakeDropBoxWorld.WithOrderAndInventory();
        var req = ProtocolJson.ToElement(new DropBoxDepositRequest
        {
            OrderKey = "Andy",
            DropBox = "AndyChest",
            QualifiedId = "(O)388",
            Count = 125,
        });

        var ex = Assert.Throws<JsonRpcException>(() => DropBoxDepositHandler.Handle(req, world));
        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("not enough matching inventory", ex.Message);
    }

    [Fact]
    public void Handle_RejectsWrongDropBox()
    {
        var world = FakeDropBoxWorld.WithOrderAndInventory();
        var req = ProtocolJson.ToElement(new DropBoxDepositRequest
        {
            OrderKey = "Andy",
            DropBox = "OtherBox",
            QualifiedId = "(O)388",
            Count = 1,
        });

        var ex = Assert.Throws<JsonRpcException>(() => DropBoxDepositHandler.Handle(req, world));
        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("no matching donation objective", ex.Message);
    }

    private sealed class FakeDropBoxWorld : IDropBoxDepositWorld
    {
        public FakeDepositOrder Order { get; } = new();
        public List<FakeDepositItem> Inventory { get; } = new();
        public IReadOnlyList<IDropBoxDepositOrder> ActiveOrders => new IDropBoxDepositOrder[] { Order };
        public IReadOnlyList<IDropBoxInventoryItem> PlayerInventory => Inventory;

        public static FakeDropBoxWorld WithOrderAndInventory()
        {
            var world = new FakeDropBoxWorld();
            world.Order.Key = "Andy";
            world.Order.Objectives.Add(new FakeDepositObjective
            {
                Index = 0,
                Type = "Donate",
                DropBox = "AndyChest",
                CurrentCount = 0,
                MaxCount = 500,
                AcceptedContextTags = { "item_wood" },
            });
            world.Inventory.Add(new FakeDepositItem
            {
                QualifiedId = "(O)388",
                ItemId = "388",
                Name = "Wood",
                Stack = 100,
                Category = -15,
                Quality = 0,
                RuntimeType = "Object",
                ContextTags = { "item_wood" },
            });
            return world;
        }
    }

    private sealed class FakeDepositOrder : IDropBoxDepositOrder
    {
        public string Key { get; set; } = string.Empty;
        public List<FakeDepositObjective> Objectives { get; } = new();
        public List<FakeDepositItem> DonatedItems { get; } = new();

        IReadOnlyList<IDropBoxDepositObjective> IDropBoxDepositOrder.Objectives => Objectives;

        public void Deposit(IDropBoxDepositObjective objective, IDropBoxInventoryItem item, int count)
        {
            var fakeObjective = Assert.IsType<FakeDepositObjective>(objective);
            var fakeItem = Assert.IsType<FakeDepositItem>(item);
            var after = fakeObjective.CurrentCount.GetValueOrDefault() + count;
            if (fakeObjective.MaxCount is { } max)
                after = Math.Min(after, max);

            fakeObjective.CurrentCount = after;
            fakeItem.Stack -= count;
            DonatedItems.Add(fakeItem.CloneForStack(count));
        }
    }

    private sealed class FakeDepositObjective : IDropBoxDepositObjective
    {
        public int Index { get; set; }
        public string Type { get; set; } = string.Empty;
        public string DropBox { get; set; } = string.Empty;
        public List<string> AcceptedContextTags { get; } = new();
        public int? CurrentCount { get; set; }
        public int? MaxCount { get; set; }

        IReadOnlyList<string> IDropBoxDepositObjective.AcceptedContextTags => AcceptedContextTags;
    }

    private sealed class FakeDepositItem : IDropBoxInventoryItem
    {
        public string Id => QualifiedId;
        public string ItemId { get; set; } = string.Empty;
        public string QualifiedId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Stack { get; set; }
        public int? Quality { get; set; }
        public int? Category { get; set; }
        public string RuntimeType { get; set; } = string.Empty;
        public List<string> ContextTags { get; } = new();

        IReadOnlyList<string> IDropBoxInventoryItem.ContextTags => ContextTags;

        public SpecialOrderItemSummary ToSummary(int stack)
            => new()
            {
                Id = Id,
                ItemId = ItemId,
                QualifiedId = QualifiedId,
                Name = Name,
                Stack = stack,
                Quality = Quality,
                Category = Category,
                RuntimeType = RuntimeType,
            };

        public FakeDepositItem CloneForStack(int stack)
            => new()
            {
                ItemId = ItemId,
                QualifiedId = QualifiedId,
                Name = Name,
                Stack = stack,
                Quality = Quality,
                Category = Category,
                RuntimeType = RuntimeType,
            };
    }
}
