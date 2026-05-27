using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class FestivalSetGrangeDisplayHandlerTests
{
    [Fact]
    public void Handle_PopulatesRequestedGrangeSlots()
    {
        var world = new FakeGrangeDisplayWorld();
        world.Items["(O)388"] = new FakePlaceableObject
        {
            Id = "388",
            QualifiedId = "(O)388",
            Name = "Wood",
            Stack = 1,
            Quality = 0,
            RuntimeType = "Object",
        };
        world.Items["(O)254"] = new FakePlaceableObject
        {
            Id = "254",
            QualifiedId = "(O)254",
            Name = "Melon",
            Stack = 1,
            Quality = 0,
            RuntimeType = "Object",
        };
        var p = JsonDocument.Parse(
            """
            {
              "items": [
                { "slot": 0, "id": "(O)388", "quality": 2 },
                { "slot": 4, "id": "(O)254", "stack": 1 }
              ]
            }
            """).RootElement;

        var json = FestivalSetGrangeDisplayHandler.Handle(p, world);
        var result = JsonSerializer.Deserialize<SetGrangeDisplayResult>(json, ProtocolJson.Options)!;

        Assert.True(result.Ok);
        Assert.Equal(1234, result.Tick);
        Assert.True(world.Cleared);
        Assert.Equal(new[] { 0, 4 }, world.Display.Keys.OrderBy(k => k));
        Assert.Equal(2, world.Display[0].Quality);
        Assert.Equal("(O)388", result.Items[0].QualifiedId);
        Assert.Equal(4, result.Items[1].Slot);
        Assert.Equal(2, result.FilledSlots);
    }

    [Fact]
    public void Handle_InvalidSlot_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"items\":[{\"slot\":9,\"id\":\"(O)388\"}]}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            FestivalSetGrangeDisplayHandler.Handle(p, new FakeGrangeDisplayWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("slot", ex.Message);
    }

    [Fact]
    public void Handle_UnknownItem_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"items\":[{\"slot\":0,\"id\":\"(O)missing\"}]}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            FestivalSetGrangeDisplayHandler.Handle(p, new FakeGrangeDisplayWorld()));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("(O)missing", ex.Message);
    }

    private sealed class FakeGrangeDisplayWorld : IGrangeDisplayWorld
    {
        public bool IsWorldReady { get; init; } = true;
        public int Tick => 1234;
        public bool Cleared { get; private set; }
        public Dictionary<string, IPlaceableObject?> Items { get; } = new();
        public Dictionary<int, IPlaceableObject> Display { get; } = new();

        public bool ItemExists(string id) => Items.ContainsKey(id);
        public IPlaceableObject? CreateObject(string id) => Items[id];
        public int FilledSlots => Display.Count;

        public void ClearDisplay()
        {
            Cleared = true;
            Display.Clear();
        }

        public void SetDisplayItem(int slot, IPlaceableObject item)
            => Display[slot] = item;
    }

    private sealed class FakePlaceableObject : IPlaceableObject
    {
        public string Id { get; init; } = string.Empty;
        public string QualifiedId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int Stack { get; set; }
        public int Quality { get; set; }
        public bool BigCraftable { get; init; }
        public string RuntimeType { get; init; } = string.Empty;
    }
}
