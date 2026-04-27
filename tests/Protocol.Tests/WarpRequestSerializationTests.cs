using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class WarpRequestSerializationTests
{
    [Fact]
    public void DeserializesFromSnakeCase()
    {
        var json = "{\"location\":\"SeedShop\",\"x\":4,\"y\":19}";
        var req = JsonSerializer.Deserialize<WarpRequest>(json, ProtocolJson.Options)!;
        Assert.Equal("SeedShop", req.Location);
        Assert.Equal(4, req.X);
        Assert.Equal(19, req.Y);
    }
}
