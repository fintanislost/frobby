using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class WarpNpcRequestSerializationTests
{
    [Fact]
    public void Request_DeserializesSnakeCase()
    {
        var req = JsonSerializer.Deserialize<WarpNpcRequest>(
            "{\"name\":\"Sophia\",\"location\":\"Custom_BlueMoonVineyard\",\"x\":20,\"y\":32}",
            ProtocolJson.Options)!;

        Assert.Equal("Sophia", req.Name);
        Assert.Equal("Custom_BlueMoonVineyard", req.Location);
        Assert.Equal(20, req.X);
        Assert.Equal(32, req.Y);
    }
}
