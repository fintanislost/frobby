using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class FixtureLoadRequestSerializationTests
{
    [Fact]
    public void DeserializesFromSnakeCase()
    {
        var req = JsonSerializer.Deserialize<FixtureLoadRequest>("{\"name\":\"spring_day_1_clean\"}", ProtocolJson.Options)!;
        Assert.Equal("spring_day_1_clean", req.Name);
    }
}
