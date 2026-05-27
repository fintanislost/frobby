using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class PlayerSetSpouseHandlerTests
{
    [Fact]
    public void Handle_MissingParams_ThrowsInvalidParams()
    {
        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetSpouseHandler.Handle(null, new FakePlayerSetSpouseWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Handle_BlankNpc_ThrowsInvalidParams(string npc)
    {
        var p = JsonSerializer.SerializeToElement(new { npc });

        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetSpouseHandler.Handle(p, new FakePlayerSetSpouseWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("npc", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2501)]
    public void Handle_OutOfRangePoints_ThrowsInvalidParams(int points)
    {
        var p = JsonSerializer.SerializeToElement(new { npc = "Claire", points });

        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetSpouseHandler.Handle(p, new FakePlayerSetSpouseWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("points", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    public void Handle_OutOfRangeWeddingDay_ThrowsInvalidParams(int weddingDay)
    {
        var p = JsonSerializer.SerializeToElement(new
        {
            npc = "Claire",
            wedding_year = 1,
            wedding_season = "spring",
            wedding_day = weddingDay,
        });

        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetSpouseHandler.Handle(p, new FakePlayerSetSpouseWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("wedding_day", ex.Message);
    }

    [Fact]
    public void Handle_PartialWeddingDate_ThrowsInvalidParams()
    {
        var p = JsonSerializer.SerializeToElement(new { npc = "Claire", wedding_year = 1 });

        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetSpouseHandler.Handle(p, new FakePlayerSetSpouseWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("wedding", ex.Message);
    }

    [Fact]
    public void Handle_UnreadyWorld_ThrowsGameStateInvalid()
    {
        var p = JsonSerializer.SerializeToElement(new { npc = "Claire" });
        var world = new FakePlayerSetSpouseWorld { IsWorldReady = false };

        var ex = Assert.Throws<JsonRpcException>(() => PlayerSetSpouseHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_ValidRequest_SetsSpouseRelationship()
    {
        var p = JsonSerializer.SerializeToElement(new
        {
            npc = " Claire ",
            points = 2400,
            roommate = true,
            wedding_year = 1,
            wedding_season = "spring",
            wedding_day = 2,
        });
        var world = new FakePlayerSetSpouseWorld { Tick = 99 };

        var result = PlayerSetSpouseHandler.Handle(p, world);

        Assert.Equal("Claire", world.Npc);
        Assert.Equal(2400, world.Points);
        Assert.True(world.Roommate);
        Assert.Equal(new WeddingDateSpec(1, "spring", 2), world.WeddingDate);
        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal(99, result.GetProperty("tick").GetInt32());
        Assert.Equal("Claire", result.GetProperty("spouse").GetString());
        Assert.Equal(2400, result.GetProperty("points").GetInt32());
        Assert.Equal("married", result.GetProperty("status").GetString());
    }

    [Fact]
    public void Handle_ValidRequest_DefaultsPointsAndWeddingDate()
    {
        var p = JsonSerializer.SerializeToElement(new { npc = "Claire" });
        var world = new FakePlayerSetSpouseWorld
        {
            CurrentWeddingDate = new WeddingDateSpec(2, "summer", 3),
        };

        PlayerSetSpouseHandler.Handle(p, world);

        Assert.Equal(2500, world.Points);
        Assert.False(world.Roommate);
        Assert.Equal(new WeddingDateSpec(2, "summer", 3), world.WeddingDate);
    }

    private sealed class FakePlayerSetSpouseWorld : IPlayerSetSpouseWorld
    {
        public bool IsWorldReady { get; set; } = true;
        public int Tick { get; set; } = 1;
        public WeddingDateSpec CurrentWeddingDate { get; set; } = new(1, "spring", 1);
        public string? Npc { get; private set; }
        public int Points { get; private set; }
        public bool Roommate { get; private set; }
        public WeddingDateSpec? WeddingDate { get; private set; }

        public PlayerSpouseState SetSpouse(string npc, int points, bool roommate, WeddingDateSpec weddingDate)
        {
            Npc = npc;
            Points = points;
            Roommate = roommate;
            WeddingDate = weddingDate;
            return new PlayerSpouseState(npc, points, "married");
        }
    }
}
