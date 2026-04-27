using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Runner.Dsl;

/// <summary>Ambient static DSL for fixture lifecycle RPCs.</summary>
public static class Fixture
{
    /// <summary>Load the named save fixture from <c>tests/fixtures/&lt;name&gt;/</c>.</summary>
    public static async Task Load(string name, CancellationToken ct = default)
    {
        var s = SdvTestSession.Current ?? throw DslPreconditions.NoSession();
        var p = JsonSerializer.SerializeToElement(new FixtureLoadRequest { Name = name }, ProtocolJson.Options);
        await s.InvokeAsync("fixture.load", p, ct);
    }
}
