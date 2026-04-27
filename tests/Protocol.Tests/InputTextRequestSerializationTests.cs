using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class InputTextRequestSerializationTests
{
    [Fact]
    public void DeserializesTextAndSubmit()
    {
        var req = JsonSerializer.Deserialize<InputTextRequest>(
            "{\"text\":\"OE\",\"submit\":true}",
            ProtocolJson.Options)!;

        Assert.Equal("OE", req.Text);
        Assert.True(req.Submit);
    }

    [Fact]
    public void SubmitDefaultsToFalse()
    {
        var req = JsonSerializer.Deserialize<InputTextRequest>(
            "{\"text\":\"OE\"}",
            ProtocolJson.Options)!;

        Assert.False(req.Submit);
    }
}
