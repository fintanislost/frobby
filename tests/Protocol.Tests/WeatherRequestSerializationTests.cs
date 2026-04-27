using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class WeatherRequestSerializationTests
{
    [Fact]
    public void DeserializesFromSnakeCase()
    {
        var req = JsonSerializer.Deserialize<WeatherRequest>("{\"type\":\"rain\"}", ProtocolJson.Options)!;
        Assert.Equal("rain", req.Type);
    }
}
