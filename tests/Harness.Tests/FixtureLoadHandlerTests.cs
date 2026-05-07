using System;
using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class FixtureLoadHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => FixtureLoadHandler.Handle(null));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_EmptyName_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"name\":\"\"}").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => FixtureLoadHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_NonexistentSave_ThrowsFixtureLoadFailed()
    {
        // GUID-suffixed name guarantees the folder does not exist under Constants.SavesPath.
        // Context.IsWorldReady is false in the unit-test environment (no live SMAPI), so
        // the already-in-save guard passes; the new folder-exists guard fires next.
        var name = $"__nonexistent__{Guid.NewGuid():N}";
        var p = JsonDocument.Parse("{\"name\":\"" + name + "\"}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => FixtureLoadHandler.Handle(p));
        Assert.Equal(JsonRpcErrorCode.FixtureLoadFailed, ex.Code);
        Assert.Contains("no save named", ex.Message);
    }

    [Fact]
    public void Handle_ValidName_ClearsActiveMenuBeforeQueueingLoad()
    {
        var p = JsonDocument.Parse("{\"name\":\"fixture_a\"}").RootElement;
        var world = new FakeFixtureLoadWorld(existingSave: true);

        FixtureLoadHandler.Handle(p, world);

        Assert.Equal(new[] { "clear_active_menu", "queue_load:fixture_a" }, world.Calls);
    }

    [Fact(Skip = "Requires live SDV (Context.IsWorldReady + SaveGame.getLoadEnumerator).")]
    public void Handle_AlreadyInSave_ThrowsGameStateInvalid() { }

    [Fact(Skip = "Requires live SDV (SaveGame.getLoadEnumerator + Game1.gameMode set).")]
    public void Handle_ValidName_InitiatesLoadAndReturnsTick() { }

    private sealed class FakeFixtureLoadWorld : IFixtureLoadWorld
    {
        private readonly bool _existingSave;

        public FakeFixtureLoadWorld(bool existingSave)
        {
            _existingSave = existingSave;
        }

        public List<string> Calls { get; } = new();

        public bool IsWorldReady { get; set; }
        public int Tick => 123;

        public bool SaveExists(string name) => _existingSave;
        public string SavePath(string name) => $"/fake/saves/{name}";
        public void ClearActiveMenu() => Calls.Add("clear_active_menu");
        public void QueueLoad(string name) => Calls.Add($"queue_load:{name}");
    }
}
