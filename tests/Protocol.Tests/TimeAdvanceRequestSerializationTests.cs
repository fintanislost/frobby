using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class TimeAdvanceRequestSerializationTests
{
    [Fact]
    public void Request_DeserializesFromSnakeCase()
    {
        var req = JsonSerializer.Deserialize<TimeAdvanceRequest>("{\"minutes\":30}", ProtocolJson.Options)!;
        Assert.Equal(30, req.Minutes);
    }

    [Fact]
    public void Result_SerializesNewTimeOfDayAsSnakeCase()
    {
        var r = new TimeAdvanceResult { Tick = 1, NewTimeOfDay = 630 };
        var json = JsonSerializer.Serialize(r, ProtocolJson.Options);
        Assert.Contains("\"new_time_of_day\":630", json);
        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"tick\":1", json);
    }
}
