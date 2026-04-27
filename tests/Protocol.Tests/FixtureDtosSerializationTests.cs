using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class FixtureDtosSerializationTests
{
    [Fact]
    public void FixtureSaveRequest_Serializes_WithSnakeCaseName()
    {
        var req = new FixtureSaveRequest { Name = "spring_day_5_500g" };
        var json = JsonSerializer.Serialize(req, ProtocolJson.Options);
        Assert.Contains("\"name\":\"spring_day_5_500g\"", json);
    }

    [Fact]
    public void FixtureSaveResult_Serializes_WithOkTickSavePath()
    {
        var r = new FixtureSaveResult { Ok = true, Tick = 1234, SavePath = "/tmp/save/x" };
        var json = JsonSerializer.Serialize(r, ProtocolJson.Options);
        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"tick\":1234", json);
        Assert.Contains("\"save_path\":\"/tmp/save/x\"", json);
    }

    [Fact]
    public void ModsState_Serializes_WithArrayOfIds()
    {
        var s = new ModsState { Mods = new[] { "A.B.C", "D.E.F" } };
        var json = JsonSerializer.Serialize(s, ProtocolJson.Options);
        Assert.Contains("\"mods\":[\"A.B.C\",\"D.E.F\"]", json);
    }
}
