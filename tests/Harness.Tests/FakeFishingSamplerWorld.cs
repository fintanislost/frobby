using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Harness.Tests;

internal sealed class FakeFishingSamplerWorld : FakeFishingWorld, IFishingSamplerWorld
{
    public bool RestoreCalled { get; set; }

    public static new FakeFishingSamplerWorld Sample()
    {
        var world = new FakeFishingSamplerWorld();
        world.Location.World = world;
        return world;
    }

    public IFishingSampleState Snapshot(FishingSampleCatchRequest request) => new FakeSampleState(this);

    public FishingCatchResult SampleCatch(FishingSampleCatchRequest request, TilePoint tile, int attempt)
        => attempt == 1
            ? new FishingCatchResult
            {
                Attempt = attempt,
                ItemId = "2334",
                QualifiedId = "(F)2334",
                DisplayName = "Pyramid Decal",
                Type = "furniture",
                Stack = 1,
                RuntimeType = "Furniture",
                Source = "runtime",
                RawId = "2334",
            }
            : new FishingCatchResult
            {
                Attempt = attempt,
                ItemId = "164",
                QualifiedId = "(O)164",
                DisplayName = "Sandfish",
                Type = "fish",
                Stack = 1,
                RuntimeType = "Object",
                Source = "runtime",
                RawId = "164",
            };

    private sealed class FakeSampleState(FakeFishingSamplerWorld world) : IFishingSampleState
    {
        public void Restore() => world.RestoreCalled = true;
    }
}
