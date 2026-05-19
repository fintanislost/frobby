using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class PlayerAddSecretNoteSeenHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => PlayerAddSecretNoteSeenHandler.Handle(null));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Handle_InvalidId_ThrowsInvalidParams(int id)
    {
        var p = JsonSerializer.SerializeToElement(new { id });

        var ex = Assert.Throws<JsonRpcException>(() => PlayerAddSecretNoteSeenHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("positive", ex.Message);
    }

    [Fact(Skip = "Requires live SDV (Game1.MasterPlayer.secretNotesSeen read/write).")]
    public void Handle_ValidId_AddsSecretNoteSeen() { /* integration */ }

    [Fact]
    public void Handle_ValidId_AddsMissingNoteToMasterAndSeparateLocal()
    {
        var world = new FakeSecretNoteSeenWorld { LocalPlayerIsMaster = false };
        var p = JsonSerializer.SerializeToElement(new { id = 18 });

        var result = PlayerAddSecretNoteSeenHandler.Handle(p, world);

        Assert.Contains(18, world.MasterSecretNotesSeen);
        Assert.Contains(18, world.LocalSecretNotesSeen);
        Assert.Equal(1, world.MasterAddCount);
        Assert.Equal(1, world.LocalAddCount);
        Assert.Contains("\"tick\":456", result.GetRawText());
    }

    [Fact]
    public void Handle_ExistingId_DoesNotAddDuplicateToMasterOrLocal()
    {
        var world = new FakeSecretNoteSeenWorld { LocalPlayerIsMaster = false };
        world.MasterSecretNotesSeen.Add(18);
        world.LocalSecretNotesSeen.Add(18);
        var p = JsonSerializer.SerializeToElement(new { id = 18 });

        PlayerAddSecretNoteSeenHandler.Handle(p, world);

        Assert.Equal(new[] { 18 }, world.MasterSecretNotesSeen);
        Assert.Equal(new[] { 18 }, world.LocalSecretNotesSeen);
        Assert.Equal(0, world.MasterAddCount);
        Assert.Equal(0, world.LocalAddCount);
    }

    private sealed class FakeSecretNoteSeenWorld : ISecretNoteSeenWorld
    {
        public int Tick => 456;
        public bool LocalPlayerIsMaster { get; set; } = true;
        public List<int> MasterSecretNotesSeen { get; } = new();
        public List<int> LocalSecretNotesSeen { get; } = new();
        public int MasterAddCount { get; private set; }
        public int LocalAddCount { get; private set; }

        public void RequireWorldReady() { }

        public bool MasterHasSecretNoteSeen(int id) => MasterSecretNotesSeen.Contains(id);

        public void AddMasterSecretNoteSeen(int id)
        {
            MasterAddCount++;
            MasterSecretNotesSeen.Add(id);
        }

        public bool LocalHasSecretNoteSeen(int id) => LocalSecretNotesSeen.Contains(id);

        public void AddLocalSecretNoteSeen(int id)
        {
            LocalAddCount++;
            LocalSecretNotesSeen.Add(id);
        }
    }
}
