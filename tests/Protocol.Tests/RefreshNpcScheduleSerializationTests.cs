using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class RefreshNpcScheduleSerializationTests
{
    [Fact]
    public void Request_DeserializesSnakeCase()
    {
        var req = JsonSerializer.Deserialize<RefreshNpcScheduleRequest>(
            "{\"name\":\"Claire\",\"schedule_key\":\"Thu\"}",
            ProtocolJson.Options)!;

        Assert.Equal("Claire", req.Name);
        Assert.Equal("Thu", req.ScheduleKey);
    }

    [Fact]
    public void Result_SerializesSnakeCase()
    {
        var json = JsonSerializer.Serialize(new RefreshNpcScheduleResult
        {
            Ok = true,
            Tick = 42,
            Location = "MovieTheater",
            Tile = new TilePoint { X = 7, Y = 5 },
        }, ProtocolJson.Options);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"tick\":42", json);
        Assert.Contains("\"location\":\"MovieTheater\"", json);
        Assert.Contains("\"tile\":{\"x\":7,\"y\":5}", json);
    }
}
