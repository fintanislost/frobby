using System.Linq;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

internal static class CombatLabLifecycle
{
    private static readonly ICombatLabCleanupWorld ProductionWorld = new SdvCombatLabCleanupWorld();

    internal static void Clear()
        => Clear(ProductionWorld);

    internal static void Clear(ICombatLabCleanupWorld world)
    {
        CombatLabIdentityRegistry.Clear();
        world.RemoveCombatLabLocation();
    }
}

internal interface ICombatLabCleanupWorld
{
    void RemoveCombatLabLocation();
}

internal sealed class SdvCombatLabCleanupWorld : ICombatLabCleanupWorld
{
    public void RemoveCombatLabLocation()
    {
        if (Game1.gameMode != Game1.playingGameMode || !Game1.hasLoadedGame || Game1.locations is null)
            return;

        var lab = Game1.getLocationFromName(CombatLabResetHandler.LocationName);
        if (lab is null)
            return;

        if (ReferenceEquals(Game1.currentLocation, lab))
        {
            var fallback = Game1.locations.FirstOrDefault(l =>
                l is not null && !ReferenceEquals(l, lab) && l.Name != CombatLabResetHandler.LocationName);
            if (fallback is not null)
                Game1.currentLocation = fallback;
        }

        Game1.locations.Remove(lab);
    }
}
