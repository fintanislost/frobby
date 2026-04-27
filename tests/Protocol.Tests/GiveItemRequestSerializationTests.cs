using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class GiveItemRequestSerializationTests
{
    [Fact]
    public void DeserializesFromSnakeCase()
    {
        var json = "{\"id\":\"(O)388\",\"count\":50}";
        var req = JsonSerializer.Deserialize<GiveItemRequest>(json, ProtocolJson.Options)!;
        Assert.Equal("(O)388", req.Id);
        Assert.Equal(50, req.Count);
    }

    [Fact]
    public void Count_DefaultsToOne_WhenAbsent()
    {
        var json = "{\"id\":\"(O)388\"}";
        var req = JsonSerializer.Deserialize<GiveItemRequest>(json, ProtocolJson.Options)!;
        Assert.Equal(1, req.Count);
    }
}
