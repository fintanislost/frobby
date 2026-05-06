using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol.Models;
using StardewValley;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class NpcStateProjectorTests
{
    [Theory]
    [InlineData("Portraits/Abigail", "Abigail")]
    [InlineData("Portraits\\Sophia", "Sophia")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizePortraitName_ReturnsBaseName(string? raw, string? expected)
    {
        Assert.Equal(expected, NpcStateProjector.NormalizePortraitName(raw));
    }

    [Fact]
    public void ApplyFriendship_MapsPointsHeartsAndFlags()
    {
        var state = new NpcState { Name = "Sophia" };
        var friendship = new Friendship
        {
            Points = 500,
            TalkedToToday = true,
            GiftsToday = 1,
            GiftsThisWeek = 2,
        };

        NpcStateProjector.ApplyFriendship(state, friendship);

        Assert.Equal(500, state.FriendshipPoints);
        Assert.Equal(2, state.Hearts);
        Assert.True(state.GiftGivenToday);
        Assert.True(state.TalkedToToday);
    }
}
