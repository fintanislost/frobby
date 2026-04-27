using System.Linq;
using System.Threading.Tasks;
using SdvTestFramework.Protocol.Models;
using SdvTestFramework.Runner.Dsl;
using Xunit;

namespace SdvTestFramework.Runner.Dsl.Tests.Worked;

/// <summary>
/// End-to-end DSL example: fixture → warp → freeze → draw snapshot + assertion. Skip-marked
/// by default (requires live SDV + harness + Xvfb); run manually via
/// <c>dotnet test tests/Runner.Dsl.Tests/ --filter Worked</c>.
/// </summary>
[Collection("SDV")]
public class ShopMenuDslSmoke
{
    [Fact(Skip = "Requires live SDV + Xvfb — run manually with --filter Worked.")]
    [Scenario(fixture: "m0spike_436515781")]
    public async Task Warp_DrawsCursorsTexture()
    {
        await Player.Warp("FarmHouse", 8, 10);
        await Player.SetMoney(1000);
        await Draw.Arm();
        await Wait.Ms(500);
        await Freeze.Begin();

        var snap = await Draw.Snapshot();
        Assert.NotEmpty(snap.Events);
        // Cursors renders almost every frame in vanilla SDV; this assertion exercises the
        // full capture + resolve pipeline without coupling to a specific scene.
        Assert.Contains(snap.Events, e => e.TextureAsset == "LooseSprites/Cursors");
    }
}
