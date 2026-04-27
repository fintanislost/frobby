using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests.Facets;

public class DrawBitmapTests
{
    private sealed class CapturingInvoker : ISdvTestInvoker
    {
        public List<(string Method, string ParamsJson)> Calls { get; } = new();
        public string NextJson { get; set; } = "{}";
        public Task<JsonElement> InvokeAsync(string m, JsonElement? p, CancellationToken ct)
        {
            Calls.Add((m, p?.GetRawText() ?? ""));
            return Task.FromResult(JsonDocument.Parse(NextJson).RootElement.Clone());
        }
    }

    [Fact]
    public async Task Arm_InvokesDrawArm()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker();
        SdvTestSession.InitializeForTests(inv);
        try { await Draw.Arm(); }
        finally { SdvTestSession.ResetForTests(); }
        Assert.Equal("draw.arm", inv.Calls[0].Method);
    }

    [Fact]
    public async Task AssertContains_InvokesDrawAssertContainsWithFilter()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker
        {
            NextJson = "{\"passed\":true,\"matched\":1}",
        };
        SdvTestSession.InitializeForTests(inv);
        try
        {
            await Draw.AssertContains(new DrawFilter { TextureAsset = "LooseSprites/Cursors" }, minCount: 2);
        }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("draw.assert_contains", inv.Calls[0].Method);
        Assert.Contains("\"texture_asset\":\"LooseSprites/Cursors\"", inv.Calls[0].ParamsJson);
        Assert.Contains("\"min_count\":2", inv.Calls[0].ParamsJson);
    }

    [Fact]
    public async Task BitmapCapture_WithRegion_SerializesRegionParam()
    {
        SdvTestSession.ResetForTests();
        var inv = new CapturingInvoker
        {
            NextJson = "{\"path\":\"/tmp/x.png\",\"width\":32,\"height\":32}",
        };
        SdvTestSession.InitializeForTests(inv);
        try
        {
            var result = await Bitmap.Capture(new BitmapRegion(0, 0, 32, 32));
            Assert.Equal("/tmp/x.png", result.Path);
            Assert.Equal(32, result.Width);
        }
        finally { SdvTestSession.ResetForTests(); }

        Assert.Equal("bitmap.capture", inv.Calls[0].Method);
        Assert.Contains("\"region\":", inv.Calls[0].ParamsJson);
        Assert.Contains("\"w\":32", inv.Calls[0].ParamsJson);
    }
}
