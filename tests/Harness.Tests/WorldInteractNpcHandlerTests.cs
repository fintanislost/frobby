using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class WorldInteractNpcHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => WorldInteractNpcHandler.Handle(null));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_EmptyName_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"name\":\"\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => WorldInteractNpcHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("name", ex.Message);
    }

    [Fact(Skip = "Requires live SDV (Game1.currentLocation.characters + NPC.checkAction).")]
    public void Handle_NpcPresentInLocation_InvokesCheckActionAndReturnsTick() { /* integration */ }

    [Fact]
    public void Handle_CheckActionDoesNotOpenMenu_ForTalkableNpc_DrawsDialogue()
    {
        var world = new FakeInteractNpcWorld();
        var p = JsonDocument.Parse("{\"name\":\"Sophia\"}").RootElement;

        WorldInteractNpcHandler.Handle(p, world);

        Assert.Equal(new[] { "prepare:Sophia", "check:Sophia", "draw:Sophia" }, world.Calls);
    }

    [Fact]
    public void Handle_CheckActionOpensRenderableMenu_DoesNotDrawDialogueFallback()
    {
        var world = new FakeInteractNpcWorld
        {
            HasActiveMenuAfterCheckAction = true,
            HasRenderableDialogueMenuAfterCheckAction = true,
        };
        var p = JsonDocument.Parse("{\"name\":\"Sophia\"}").RootElement;

        WorldInteractNpcHandler.Handle(p, world);

        Assert.Equal(new[] { "prepare:Sophia", "check:Sophia" }, world.Calls);
    }

    [Fact]
    public void Handle_CheckActionOpensEmptyDialogueMenu_ForTalkableNpc_DrawsDialogue()
    {
        var world = new FakeInteractNpcWorld { HasActiveMenuAfterCheckAction = true };
        var p = JsonDocument.Parse("{\"name\":\"Sophia\"}").RootElement;

        WorldInteractNpcHandler.Handle(p, world);

        Assert.Equal(new[] { "prepare:Sophia", "check:Sophia", "draw:Sophia" }, world.Calls);
    }

    [Fact]
    public void Handle_CheckActionDoesNotOpenMenu_ForNonTalkableNpc_DoesNotDrawDialogue()
    {
        var world = new FakeInteractNpcWorld { NpcCanTalk = false };
        var p = JsonDocument.Parse("{\"name\":\"Sophia\"}").RootElement;

        WorldInteractNpcHandler.Handle(p, world);

        Assert.Equal(new[] { "check:Sophia" }, world.Calls);
    }

    [Fact]
    public void Handle_TalkableNpc_PreparesDialogueBeforeCheckAction()
    {
        var world = new FakeInteractNpcWorld();
        var p = JsonDocument.Parse("{\"name\":\"Sophia\"}").RootElement;

        WorldInteractNpcHandler.Handle(p, world);

        Assert.True(world.Calls.IndexOf("prepare:Sophia") < world.Calls.IndexOf("check:Sophia"));
    }

    [Fact(Skip = "Requires live SDV (Context.IsWorldReady — verified by smoke test).")]
    public void Handle_AtTitleScreen_ThrowsGameStateInvalid() { /* integration */ }

    [Fact(Skip = "Requires live SDV (NPC not found returns GameStateInvalid -32003).")]
    public void Handle_NpcNotInCurrentLocation_ThrowsGameStateInvalid() { /* integration */ }

    private sealed class FakeInteractNpcWorld : IWorldInteractNpcWorld
    {
        private readonly FakeNpc _npc = new("Sophia");
        public int Tick => 123;
        public bool IsWorldReady => true;
        public string CurrentLocationName => "Custom_BlueMoonVineyard";
        public bool HasActiveMenuAfterCheckAction { get; init; }
        public bool HasRenderableDialogueMenuAfterCheckAction { get; init; }
        public bool NpcCanTalk { get; init; } = true;
        public List<string> Calls { get; } = new();

        public object? FindNpcInCurrentLocation(string name)
            => name == _npc.Name ? _npc : null;

        public void CheckAction(object npc)
        {
            Calls.Add($"check:{((FakeNpc)npc).Name}");
        }

        public void PrepareDialogue(object npc)
        {
            Calls.Add($"prepare:{((FakeNpc)npc).Name}");
        }

        public bool HasActiveMenu => HasActiveMenuAfterCheckAction;

        public bool HasEmptyDialogueMenu => HasActiveMenuAfterCheckAction && !HasRenderableDialogueMenuAfterCheckAction;

        public bool CanTalk(object npc)
            => NpcCanTalk;

        public void DrawDialogue(object npc)
        {
            Calls.Add($"draw:{((FakeNpc)npc).Name}");
        }
    }

    private sealed record FakeNpc(string Name);
}
