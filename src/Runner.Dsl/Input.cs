using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for the <c>input.*</c> RPC surface.</summary>
public static class Input
{
    /// <summary>Send a MonoGame key press to the active menu.</summary>
    public static async Task Key(string key, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new InputKeyRequest { Key = key }, ProtocolJson.Options);
        await s.InvokeAsync("input.key", p, ct);
    }
}
