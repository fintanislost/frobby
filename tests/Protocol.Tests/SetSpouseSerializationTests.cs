using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class SetSpouseSerializationTests
{
    [Fact]
    public void Request_DeserializesSnakeCase()
    {
        var req = JsonSerializer.Deserialize<SetSpouseRequest>(
            "{\"npc\":\"Claire\",\"points\":2500,\"roommate\":true,\"wedding_year\":1,\"wedding_season\":\"spring\",\"wedding_day\":2}",
            ProtocolJson.Options)!;

        Assert.Equal("Claire", req.Npc);
        Assert.Equal(2500, req.Points);
        Assert.True(req.Roommate);
        Assert.Equal(1, req.WeddingYear);
        Assert.Equal("spring", req.WeddingSeason);
        Assert.Equal(2, req.WeddingDay);
    }

    [Fact]
    public void Result_SerializesSnakeCase()
    {
        var json = JsonSerializer.Serialize(new SetSpouseResult
        {
            Ok = true,
            Tick = 12,
            Spouse = "Claire",
            Points = 2500,
            Status = "married",
        }, ProtocolJson.Options);

        Assert.Contains("\"spouse\":\"Claire\"", json);
        Assert.Contains("\"points\":2500", json);
        Assert.Contains("\"status\":\"married\"", json);
    }
}
