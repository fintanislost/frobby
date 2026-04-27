using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class InteractTileRequestSerializationTests
{
    [Fact]
    public void DeserializesFromSnakeCase()
    {
        var json = "{\"x\":8,\"y\":9,\"just_checking_for_activity\":true}";
        var req = JsonSerializer.Deserialize<InteractTileRequest>(json, ProtocolJson.Options)!;

        Assert.Equal(8, req.X);
        Assert.Equal(9, req.Y);
        Assert.True(req.JustCheckingForActivity);
    }
}
