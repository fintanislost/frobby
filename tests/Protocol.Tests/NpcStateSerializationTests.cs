using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class NpcStateSerializationTests
{
    [Fact]
    public void Serialize_SnakeCase()
    {
        var npc = new NpcState
        {
            Name = "Abigail",
            Location = "Town",
            Tile = new TilePoint { X = 4, Y = 23 },
            FriendshipPoints = 500,
            Hearts = 2,
            GiftGivenToday = false,
            Portrait = "Abigail",
        };
        var json = JsonSerializer.Serialize(npc, ProtocolJson.Options);
        Assert.Contains("\"friendship_points\":500", json);
        Assert.Contains("\"gift_given_today\":false", json);
    }

    [Fact]
    public void Serialize_IncludesOptionalScheduleFieldsWhenPopulated()
    {
        var npc = new NpcState
        {
            Name = "Sophia",
            DisplayName = "Sophia",
            Location = "Custom_BlueMoonVineyard",
            Tile = new TilePoint { X = 20, Y = 32 },
            FriendshipPoints = 500,
            Hearts = 2,
            GiftGivenToday = false,
            TalkedToToday = true,
            Portrait = "Sophia",
            CurrentScheduleKey = "Mon",
            CurrentScheduleTime = 900,
            CurrentScheduleLocation = "Custom_BlueMoonVineyard",
            CurrentScheduleTile = new TilePoint { X = 20, Y = 32 },
            CurrentScheduleDirection = 0,
            CurrentScheduleAnimation = "Sophia_Farm2",
            IsVillager = true,
            CanSocialize = true,
        };

        var json = JsonSerializer.Serialize(npc, ProtocolJson.Options);

        Assert.Contains("\"display_name\":\"Sophia\"", json);
        Assert.Contains("\"talked_to_today\":true", json);
        Assert.Contains("\"current_schedule_key\":\"Mon\"", json);
        Assert.Contains("\"current_schedule_time\":900", json);
        Assert.Contains("\"current_schedule_location\":\"Custom_BlueMoonVineyard\"", json);
        Assert.Contains("\"current_schedule_tile\":{\"x\":20,\"y\":32}", json);
        Assert.Contains("\"current_schedule_direction\":0", json);
        Assert.Contains("\"current_schedule_animation\":\"Sophia_Farm2\"", json);
        Assert.Contains("\"is_villager\":true", json);
        Assert.Contains("\"can_socialize\":true", json);
    }
}
