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
        Assert.Equal(new[] { "button_tut_1", "custom_mail_gate" }, state.MailReceived);
        Assert.Equal(new[] { "60367", "5532011" }, state.EventsSeen);
        Assert.Collection(state.Items,
            item =>
            {
                Assert.Equal(5, item.Slot);
                Assert.Equal("(F)example_terminal", item.Id);
                Assert.Equal("example_terminal", item.ItemId);
                Assert.Equal("(F)example_terminal", item.QualifiedId);
                Assert.Equal("Example Terminal", item.Name);
                Assert.Equal(1, item.Stack);
                Assert.Equal(-9, item.Category);
                Assert.Equal(0, item.Quality);
                Assert.Equal("Furniture", item.RuntimeType);
            });
    }

    [Fact]
    public void StripQualifiedPrefix_HandlesMultiCharacterQualifier()
    {
        Assert.Equal("custom_big_craftable", SdvPlayerStateWorld.StripQualifiedPrefix("(BC)custom_big_craftable"));
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
        public IReadOnlyList<string> MailReceived { get; } = new[] { "button_tut_1", "custom_mail_gate" };
        public IReadOnlyList<string> EventsSeen { get; } = new[] { "60367", "5532011" };
        public IReadOnlyList<IPlayerInventoryItem> Items { get; } = new[]
        {
            new PlayerInventoryItem(5, "(F)example_terminal", "example_terminal", "(F)example_terminal", "Example Terminal", 1, -9, 0, "Furniture"),
        };
    }
}
