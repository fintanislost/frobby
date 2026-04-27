using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class DrawArmRequestSerializationTests
{
    [Fact]
    public void DeserializesBothFields()
    {
        var json = "{\"ticks\":60,\"output_path\":\"/tmp/x.jsonl\"}";
        var req = JsonSerializer.Deserialize<DrawArmRequest>(json, ProtocolJson.Options)!;
        Assert.Equal(60, req.Ticks);
        Assert.Equal("/tmp/x.jsonl", req.OutputPath);
    }

    [Fact]
    public void DefaultsWhenAbsent()
    {
        var req = JsonSerializer.Deserialize<DrawArmRequest>("{}", ProtocolJson.Options)!;
        Assert.Equal(30, req.Ticks);
        Assert.Null(req.OutputPath);
    }
}
