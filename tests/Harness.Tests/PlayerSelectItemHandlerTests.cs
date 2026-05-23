using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class PlayerSelectItemHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() =>
            PlayerSelectItemHandler.Handle(null, new FakeSelectionWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("params required", ex.Message);
    }

    [Fact]
    public void Handle_IdAndSlotTogether_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)287\",\"slot\":1}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            PlayerSelectItemHandler.Handle(p, new FakeSelectionWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("exactly one", ex.Message);
    }

    [Fact]
    public void Handle_NotWorldReady_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"slot\":1}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            PlayerSelectItemHandler.Handle(p, new FakeSelectionWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_SlotOutOfRange_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"slot\":99}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            PlayerSelectItemHandler.Handle(p, new FakeSelectionWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("out of range", ex.Message);
    }

    [Fact]
    public void Handle_EmptySlot_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"slot\":2}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            PlayerSelectItemHandler.Handle(p, new FakeSelectionWorld()));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("empty", ex.Message);
    }

    [Fact]
    public void Handle_SelectsBySlot()
    {
        var world = new FakeSelectionWorld();
        var p = JsonDocument.Parse("{\"slot\":13}").RootElement;

        var json = PlayerSelectItemHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<PlayerSelectItemResult>(json, ProtocolJson.Options)!;

        Assert.Equal(13, world.SelectedSlot);
        Assert.Equal(13, result.Slot);
        Assert.Equal("(O)287", result.Item.QualifiedId);
        Assert.Equal("Bomb", result.Item.Name);
        Assert.Equal(1234, result.Tick);
    }

    [Fact]
    public void Handle_SelectsByQualifiedIdAndPrefersHotbar()
    {
        var world = new FakeSelectionWorld();
        var p = JsonDocument.Parse("{\"id\":\"(O)287\"}").RootElement;

        var json = PlayerSelectItemHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<PlayerSelectItemResult>(json, ProtocolJson.Options)!;

        Assert.Equal(1, world.SelectedSlot);
        Assert.Equal(1, result.Slot);
        Assert.Equal(2, result.Item.Stack);
    }

    [Fact]
    public void Handle_SelectsByRawIdWhenQualifiedIdNotProvided()
    {
        var world = new FakeSelectionWorld();
        var p = JsonDocument.Parse("{\"id\":\"287\"}").RootElement;

        var result = PlayerSelectItemHandler.Handle(p, world);

        Assert.Equal(1, JsonSerializer.Deserialize<PlayerSelectItemResult>(result, ProtocolJson.Options)!.Slot);
        Assert.Equal(1, world.SelectedSlot);
    }

    [Fact]
    public void Handle_PreferHotbarFalsePreservesSourceOrder()
    {
        var world = new FakeSelectionWorld();
        var p = JsonDocument.Parse("{\"id\":\"(O)287\",\"prefer_hotbar\":false}").RootElement;

        var result = PlayerSelectItemHandler.Handle(p, world);

        Assert.Equal(13, JsonSerializer.Deserialize<PlayerSelectItemResult>(result, ProtocolJson.Options)!.Slot);
        Assert.Equal(13, world.SelectedSlot);
    }

    [Fact]
    public void Handle_MissingId_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"id\":\"(O)74\"}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            PlayerSelectItemHandler.Handle(p, new FakeSelectionWorld()));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("inventory item not found", ex.Message);
    }

    private sealed class FakeSelectionWorld : IPlayerInventorySelectionWorld
    {
        public bool IsWorldReady { get; set; } = true;
        public int Tick { get; set; } = 1234;
        public int InventoryCount => 36;
        public int? SelectedSlot { get; private set; }

        public IReadOnlyList<ISelectableInventoryItem> Items { get; } = new ISelectableInventoryItem[]
        {
            new SelectableInventoryItem(13, "(O)287", "287", "Bomb", 1, -95, 0, "Object"),
            new SelectableInventoryItem(1, "(O)287", "287", "Bomb", 2, -95, 0, "Object"),
        };

        public void SelectSlot(int slot) => SelectedSlot = slot;
    }
}
