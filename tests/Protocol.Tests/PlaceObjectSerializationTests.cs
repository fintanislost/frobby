using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class PlaceObjectSerializationTests
{
    [Fact]
    public void Request_DeserializesFromSnakeCase()
    {
        var json = "{\"id\":\"(O)340\",\"location\":\"Farm\",\"x\":12,\"y\":9,\"stack\":3,\"quality\":2,\"remove_existing\":true}";
        var req = JsonSerializer.Deserialize<PlaceObjectRequest>(json, ProtocolJson.Options)!;

        Assert.Equal("(O)340", req.Id);
        Assert.Equal("Farm", req.Location);
        Assert.Equal(12, req.X);
        Assert.Equal(9, req.Y);
        Assert.Equal(3, req.Stack);
        Assert.Equal(2, req.Quality);
        Assert.True(req.RemoveExisting);
    }

    [Fact]
    public void Request_OptionalFieldsDefaultToCurrentLocationSingleStackNormalQualityAndNoRemoval()
    {
        var json = "{\"id\":\"(O)771\",\"x\":1,\"y\":2}";
        var req = JsonSerializer.Deserialize<PlaceObjectRequest>(json, ProtocolJson.Options)!;

        Assert.Null(req.Location);
        Assert.Equal(1, req.Stack);
        Assert.Equal(0, req.Quality);
        Assert.False(req.RemoveExisting);
    }

    [Fact]
    public void Result_SerializesToSnakeCase()
    {
        var result = new PlaceObjectResult
        {
            Ok = true,
            Tick = 42,
            Id = "340",
            QualifiedId = "(O)340",
            Name = "Honey",
            Location = "Farm",
            Tile = new TilePoint { X = 12, Y = 9 },
            BigCraftable = false,
            RuntimeType = "Object",
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Equal("{\"id\":\"340\",\"qualified_id\":\"(O)340\",\"name\":\"Honey\",\"location\":\"Farm\",\"tile\":{\"x\":12,\"y\":9},\"big_craftable\":false,\"runtime_type\":\"Object\",\"ok\":true,\"tick\":42}", json);
    }
}
