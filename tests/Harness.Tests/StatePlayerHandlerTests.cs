using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class StatePlayerHandlerTests
{
    [Fact]
    public void Handle_IncludesInventoryItemSummaries()
    {
        var result = StatePlayerHandler.Handle(null, new FakePlayerStateWorld());
        var state = JsonSerializer.Deserialize<PlayerState>(result, ProtocolJson.Options)!;

        Assert.Equal("Tester", state.Name);
        Assert.Equal(30000, state.Money);
        Assert.Collection(state.Items,
            item =>
            {
                Assert.Equal(5, item.Slot);
                Assert.Equal("(F)stonks_starberg_terminal_v1", item.Id);
                Assert.Equal("Starberg Terminal - Model 4201", item.Name);
                Assert.Equal(1, item.Stack);
            });
    }

    private sealed class FakePlayerStateWorld : IPlayerStateWorld
    {
        public string Name => "Tester";
        public int Money => 30000;
        public int Stamina => 270;
        public int MaxStamina => 270;
        public int Health => 100;
        public string Location => "FarmHouse";
        public TilePoint Tile => new() { X = 8, Y = 10 };
        public IReadOnlyList<IPlayerInventoryItem> Items { get; } = new[]
        {
            new PlayerInventoryItem(5, "(F)stonks_starberg_terminal_v1", "Starberg Terminal - Model 4201", 1),
        };
    }
}
