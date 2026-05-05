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
    public void ModsState_Serializes_WithUniqueIdsAndLoadedModMetadata()
    {
        var s = new ModsState
        {
            UniqueIds = new[] { "A.B.C", "D.E.F" },
            Mods =
            [
                new LoadedModSummary
                {
                    UniqueId = "A.B.C",
                    Name = "Alpha",
                    Version = "1.2.3",
                    IsContentPack = false,
                },
                new LoadedModSummary
                {
                    UniqueId = "D.E.F",
                    Name = "Delta",
                    Version = "2.0.0",
                    IsContentPack = true,
                    ContentPackFor = "Pathoschild.ContentPatcher",
                },
            ],
        };
        var json = JsonSerializer.Serialize(s, ProtocolJson.Options);
        Assert.Contains("\"unique_ids\":[\"A.B.C\",\"D.E.F\"]", json);
        Assert.Contains("\"unique_id\":\"A.B.C\"", json);
        Assert.Contains("\"is_content_pack\":true", json);
        Assert.Contains("\"content_pack_for\":\"Pathoschild.ContentPatcher\"", json);
    }
}
