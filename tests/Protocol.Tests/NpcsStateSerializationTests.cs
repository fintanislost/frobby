using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class NpcsStateSerializationTests
{
    [Fact]
    public void Request_UsesSnakeCaseAndDefaults()
    {
        var req = JsonSerializer.Deserialize<NpcsStateRequest>("{}", ProtocolJson.Options)!;

        Assert.True(req.IncludeOffscreen);
        Assert.Equal(200, req.Limit);
    }

    [Fact]
    public void Request_DeserializesSnakeCase()
    {
        var req = JsonSerializer.Deserialize<NpcsStateRequest>(
            "{\"include_offscreen\":false,\"limit\":25}",
            ProtocolJson.Options)!;

        Assert.False(req.IncludeOffscreen);
        Assert.Equal(25, req.Limit);
    }

    [Fact]
    public void State_SerializesNpcList()
    {
        var state = new NpcsState
        {
            Npcs =
            {
                new NpcState
                {
                    Name = "Sophia",
                    Location = "Custom_SophiaHouse",
                    Tile = new TilePoint { X = 23, Y = 6 },
                    Portrait = "Sophia",
                },
            },
        };

        var json = JsonSerializer.Serialize(state, ProtocolJson.Options);

        Assert.Contains("\"npcs\":[", json);
        Assert.Contains("\"name\":\"Sophia\"", json);
    }
}
