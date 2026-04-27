using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class PlaceFurnitureRequestSerializationTests
{
    [Fact]
    public void DeserializesFromSnakeCase()
    {
        var json = "{\"id\":\"(F)stonks_starberg_terminal_v1\",\"location\":\"FarmHouse\",\"x\":8,\"y\":9,\"remove_existing\":true}";
        var req = JsonSerializer.Deserialize<PlaceFurnitureRequest>(json, ProtocolJson.Options)!;

        Assert.Equal("(F)stonks_starberg_terminal_v1", req.Id);
        Assert.Equal("FarmHouse", req.Location);
        Assert.Equal(8, req.X);
        Assert.Equal(9, req.Y);
        Assert.True(req.RemoveExisting);
    }

    [Fact]
    public void OptionalFields_DefaultToCurrentLocationAndNoRemoval()
    {
        var json = "{\"id\":\"(F)1308\",\"x\":1,\"y\":2}";
        var req = JsonSerializer.Deserialize<PlaceFurnitureRequest>(json, ProtocolJson.Options)!;

        Assert.Null(req.Location);
        Assert.False(req.RemoveExisting);
    }
}
