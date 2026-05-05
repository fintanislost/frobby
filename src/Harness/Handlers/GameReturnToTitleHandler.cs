using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>game.return_to_title</c>. Leaves the currently loaded save so a runner can
/// immediately issue <c>fixture.load</c> again. This is intentionally game-generic; callers
/// compose it with <c>fixture.save</c> when they need a persistence round trip.
/// </summary>
public static class GameReturnToTitleHandler
{
    public const string Method = "game.return_to_title";

    public static JsonElement Handle(JsonElement? paramsElement)
        => Handle(paramsElement, new SdvReturnToTitleWorld());

    internal static JsonElement Handle(JsonElement? paramsElement, IReturnToTitleWorld world)
    {
        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "game.return_to_title requires a loaded world");
        if (world.IsEventUp)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "game.return_to_title requires !Game1.eventUp (event active)");
        if (world.IsMinigameActive)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "game.return_to_title requires Game1.currentMinigame == null (minigame active)");
        if (world.IsWarping)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "game.return_to_title requires !Game1.isWarping (mid-warp)");

        world.ReturnToTitle();
        return ProtocolJson.ToElement(new MutatorOk { Tick = world.Tick });
    }
}

internal interface IReturnToTitleWorld
{
    bool IsWorldReady { get; }
    bool IsEventUp { get; }
    bool IsMinigameActive { get; }
    bool IsWarping { get; }
    int Tick { get; }
    void ReturnToTitle();
}

internal sealed class SdvReturnToTitleWorld : IReturnToTitleWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public bool IsEventUp => Game1.eventUp;
    public bool IsMinigameActive => Game1.currentMinigame != null;
    public bool IsWarping => Game1.isWarping;
    public int Tick => Game1.ticks;

    public void ReturnToTitle()
    {
        Game1.exitActiveMenu();
        var method = FindReturnToTitleMethod()
            ?? throw new InvalidOperationException("StardewValley.Game1 return-to-title method not found");
        var args = method.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
        method.Invoke(null, args);
    }

    private static MethodInfo? FindReturnToTitleMethod()
    {
        var methods = typeof(Game1).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        return methods.FirstOrDefault(IsReturnToTitleMethod);
    }

    private static bool IsReturnToTitleMethod(MethodInfo method)
    {
        if (method.Name is not ("ExitToTitle" or "exitToTitle"))
            return false;

        return method.GetParameters().All(p => p.HasDefaultValue || p.IsOptional);
    }
}
