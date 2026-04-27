using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class DrawEventSnapshotSerializationTests
{
    [Fact]
    public void Serialize_UsesShortFieldNamesInSnakeCase()
    {
        var snap = new DrawEventSnapshot
        {
            Events = new()
            {
                new DrawEventDto
                {
                    Tick = 5, Call = 1, TexRef = 42, TexW = 16, TexH = 16,
                    Src = new[] { 0, 0, 16, 16 },
                    Dst = new[] { 0, 0, 64, 64 },
                    Col = new[] { 255, 255, 255, 255 },
                    Rot = 0f, Orig = new[] { 0f, 0f },
                    Fx = 0, Z = 0.5f,
                },
            },
            Meta = new SnapshotMeta { Ticks = 10, Events = 1, Dropped = 0 },
        };

        var json = JsonSerializer.Serialize(snap, ProtocolJson.Options);
        Assert.Contains("\"tex_w\":16", json);
        Assert.Contains("\"tex_ref\":42", json);
        Assert.Contains("\"src\":[0,0,16,16]", json);
        Assert.Contains("\"dst\":[0,0,64,64]", json);
        Assert.Contains("\"col\":[255,255,255,255]", json);
        Assert.Contains("\"z\":0.5", json);
        Assert.Contains("\"meta\":{\"ticks\":10,\"events\":1,\"dropped\":0,\"resolved_count\":0}", json);
    }

    [Fact]
    public void Serialize_NullSourceRect_EmitsNull()
    {
        var snap = new DrawEventSnapshot
        {
            Events = new() { new DrawEventDto { Src = null, Dst = new[] { 1, 2, 3, 4 } } },
        };
        var json = JsonSerializer.Serialize(snap, ProtocolJson.Options);
        Assert.Contains("\"src\":null", json);
    }

    [Fact]
    public void Serialize_EmptyEvents_EmitsEmptyArray()
    {
        var snap = new DrawEventSnapshot();
        var json = JsonSerializer.Serialize(snap, ProtocolJson.Options);
        Assert.Contains("\"events\":[]", json);
    }

    [Fact]
    public void Serialize_TextureAsset_EmitsSnakeCaseField()
    {
        var snap = new DrawEventSnapshot
        {
            Events = new()
            {
                new DrawEventDto
                {
                    TextureAsset = "Characters/Abigail",
                    Dst = new[] { 0, 0, 16, 16 },
                },
            },
        };
        var json = JsonSerializer.Serialize(snap, ProtocolJson.Options);
        Assert.Contains("\"texture_asset\":\"Characters/Abigail\"", json);
    }

    [Fact]
    public void Serialize_NullTextureAsset_EmittedAsNull()
    {
        // Mirrors Src: JsonIgnore(Never) ensures the field is always emitted, even when null,
        // so "no Tier 1 resolution" is distinguishable from "field absent in this protocol version".
        var snap = new DrawEventSnapshot
        {
            Events = new() { new DrawEventDto { Dst = new[] { 0, 0, 1, 1 } } },
        };
        var json = JsonSerializer.Serialize(snap, ProtocolJson.Options);
        Assert.Contains("\"texture_asset\":null", json);
    }

    [Fact]
    public void Serialize_SnapshotMeta_IncludesResolvedCount()
    {
        var snap = new DrawEventSnapshot
        {
            Meta = new SnapshotMeta { Ticks = 30, Events = 100, Dropped = 0, ResolvedCount = 87 },
        };
        var json = JsonSerializer.Serialize(snap, ProtocolJson.Options);
        Assert.Contains("\"resolved_count\":87", json);
    }
}
