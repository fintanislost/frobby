using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class MenuStateSerializationTests
{
    [Fact]
    public void Absent_SerializesPresentFalse()
    {
        var json = JsonSerializer.Serialize(new MenuState(), ProtocolJson.Options);
        Assert.Contains("\"present\":false", json);
        Assert.Contains("\"type\":\"\"", json);
    }

    [Fact]
    public void Present_SerializesWithExtra()
    {
        var m = new MenuState
        {
            Type = "ShopMenu",
            Present = true,
            Extra = new() { ["currency"] = "g" },
        };
        var json = JsonSerializer.Serialize(m, ProtocolJson.Options);
        Assert.Contains("\"present\":true", json);
        Assert.Contains("\"currency\":\"g\"", json);
    }
}
