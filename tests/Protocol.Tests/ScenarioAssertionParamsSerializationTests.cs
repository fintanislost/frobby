using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class ScenarioAssertionParamsSerializationTests
{
    [Fact]
    public void Params_RoundTripsForStateAssertion()
    {
        var assertion = JsonSerializer.Deserialize<ScenarioAssertion>(
            "{\"type\":\"state\",\"expr\":\"state.npc.hearts == 2\",\"params\":{\"name\":\"Sophia\"}}",
            ProtocolJson.Options)!;

        Assert.Equal("state", assertion.Type);
        Assert.Equal("state.npc.hearts == 2", assertion.Expr);
        Assert.NotNull(assertion.Params);
        Assert.Equal("Sophia", assertion.Params.Value.GetProperty("name").GetString());
    }
}
