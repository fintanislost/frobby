using System.Text.Json;
using System.Threading.Tasks;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class FixtureSaveHandlerTests
{
    [Fact]
    public void Handle_MissingName_ThrowsInvalidParams()
    {
        var req = JsonDocument.Parse("""{}""").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => FixtureSaveHandler.Handle(req));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("name", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_LoadedWorld_AwaitsSaveTaskAndReturnsPath()
    {
        var req = JsonDocument.Parse("""{"name":"roundtrip"}""").RootElement;
        var world = new FakeFixtureSaveWorld();

        var result = await FixtureSaveHandler.HandleAsync(req, world);
        var saved = JsonSerializer.Deserialize<FixtureSaveResult>(result, ProtocolJson.Options)!;

        Assert.True(saved.Ok);
        Assert.Equal(42, saved.Tick);
        Assert.Equal("/tmp/Saves/roundtrip", saved.SavePath);
        Assert.True(world.SaveCompleted);
    }

    [Fact(Skip = "Requires live SDV — integration tested via FixtureBuilderIntegrationTests (T11 smoke).")]
    public void Handle_AtTitleScreen_ThrowsGameStateInvalid() { }

    [Fact(Skip = "Requires live SDV — integration tested via FixtureBuilderIntegrationTests (T11 smoke).")]
    public void Handle_InSave_TriggersSaveGameSave_AndReturnsPath() { }

    private sealed class FakeFixtureSaveWorld : IFixtureSaveWorld
    {
        public bool IsWorldReady => true;
        public bool IsEventUp => false;
        public bool IsMinigameActive => false;
        public bool IsWarping => false;
        public int Tick => 42;
        public string SavePath => "/tmp/Saves/roundtrip";
        public bool SaveCompleted { get; private set; }

        public void MarkFixtureSave() { }

        public async Task SaveAsync()
        {
            await Task.Yield();
            SaveCompleted = true;
        }
    }
}
