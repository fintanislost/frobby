using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class SetMoneyRequestSerializationTests
{
    [Fact]
    public void DeserializesFromSnakeCase()
    {
        var json = "{\"amount\":5000}";
        var req = JsonSerializer.Deserialize<SetMoneyRequest>(json, ProtocolJson.Options)!;
        Assert.Equal(5000, req.Amount);
    }

    [Fact]
    public void Result_IncludesPreviousAndTickAndOk()
    {
        var r = new SetMoneyResult { Previous = 1000, Tick = 42 };
        var json = JsonSerializer.Serialize(r, ProtocolJson.Options);
        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"tick\":42", json);
        Assert.Contains("\"previous\":1000", json);
    }
}
