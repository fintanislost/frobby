using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class PlayerSelectItemSerializationTests
{
    [Fact]
    public void Request_DeserializesIdAndPreferHotbar()
    {
        var req = JsonSerializer.Deserialize<PlayerSelectItemRequest>(
            "{\"id\":\"(O)287\",\"prefer_hotbar\":false}",
            ProtocolJson.Options)!;

        Assert.Equal("(O)287", req.Id);
        Assert.Null(req.Slot);
        Assert.False(req.PreferHotbar);
    }

    [Fact]
    public void Request_DefaultsPreferHotbarToTrue()
    {
        var req = JsonSerializer.Deserialize<PlayerSelectItemRequest>(
            "{\"slot\":13}",
            ProtocolJson.Options)!;

        Assert.Null(req.Id);
        Assert.Equal(13, req.Slot);
        Assert.True(req.PreferHotbar);
    }

    [Fact]
    public void Result_SerializesSelectedItemSummary()
    {
        var result = new PlayerSelectItemResult
        {
            Ok = true,
            Tick = 42,
            Slot = 1,
            Item = new PlayerItemSummary
            {
                Slot = 1,
                Id = "(O)287",
                ItemId = "287",
                QualifiedId = "(O)287",
                Name = "Bomb",
                Stack = 2,
                Category = -95,
                Quality = 0,
                RuntimeType = "Object",
            },
        };

        var json = JsonSerializer.Serialize(result, ProtocolJson.Options);

        Assert.Contains("\"ok\":true", json);
        Assert.Contains("\"tick\":42", json);
        Assert.Contains("\"slot\":1", json);
        Assert.Contains("\"qualified_id\":\"(O)287\"", json);
        Assert.Contains("\"runtime_type\":\"Object\"", json);
        Assert.DoesNotContain("PreferHotbar", json);
    }
}
