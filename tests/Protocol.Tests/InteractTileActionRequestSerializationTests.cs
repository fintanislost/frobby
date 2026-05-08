using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class InteractTileActionRequestSerializationTests
{
    [Fact]
    public void Request_DeserializesSnakeCase()
    {
        var req = JsonSerializer.Deserialize<InteractTileActionRequest>(
            "{\"location\":\"Custom_BlueMoonVineyard\",\"x\":56,\"y\":48,\"property\":\"TouchAction\",\"layers\":[\"Back\"],\"just_checking_for_activity\":true}",
            ProtocolJson.Options)!;

        Assert.Equal("Custom_BlueMoonVineyard", req.Location);
        Assert.Equal(56, req.X);
        Assert.Equal(48, req.Y);
        Assert.Equal("TouchAction", req.Property);
        Assert.Equal(new[] { "Back" }, req.Layers);
        Assert.True(req.JustCheckingForActivity);
    }
}
