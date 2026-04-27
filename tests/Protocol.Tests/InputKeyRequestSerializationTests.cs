using System.Text.Json;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class InputKeyRequestSerializationTests
{
    [Fact]
    public void DeserializesKey()
    {
        var req = JsonSerializer.Deserialize<InputKeyRequest>("{\"key\":\"Enter\"}", ProtocolJson.Options)!;

        Assert.Equal("Enter", req.Key);
    }
}
