using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewModdingAPI;
using StardewValley;

namespace SdvTestFramework.Harness.Handlers;

/// <summary>
/// Handler for <c>fixture.save</c>. Starts SDV's <see cref="SaveGame.Save"/> on the game
/// thread (the handler already runs there via GameThreadDispatch), waits for the save to
/// settle, then returns the absolute save path. Preconditions mirror <c>FreezeBeginHandler</c>.
/// </summary>
public static class FixtureSaveHandler
{
    public const string Method = "fixture.save";

    public static JsonElement Handle(JsonElement? paramsElement)
        => HandleAsync(paramsElement).GetAwaiter().GetResult();

    public static Task<JsonElement> HandleAsync(JsonElement? paramsElement)
        => HandleAsync(paramsElement, new SdvFixtureSaveWorld());

    internal static async Task<JsonElement> HandleAsync(JsonElement? paramsElement, IFixtureSaveWorld world)
    {
        var req = RpcParams.Required<FixtureSaveRequest>(paramsElement);
        if (string.IsNullOrEmpty(req.Name))
            throw new JsonRpcException(JsonRpcErrorCode.InvalidParams, "params.name required");

        // Preconditions — same predicate as FreezeBeginHandler (D1.7 widened).
        if (!world.IsWorldReady)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "fixture.save requires a loaded world (no active save)");
        if (world.IsEventUp)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "fixture.save requires !Game1.eventUp (event active)");
        if (world.IsMinigameActive)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "fixture.save requires Game1.currentMinigame == null (minigame active)");
        if (world.IsWarping)
            throw new JsonRpcException(JsonRpcErrorCode.GameStateInvalid,
                "fixture.save requires !Game1.isWarping (mid-warp)");

        // Marker so framework-created saves can be identified later. Harmless flavor field.
        world.MarkFixtureSave();

        var savePath = world.SavePath;
        var tick = world.Tick;
        await world.SaveAsync().ConfigureAwait(false);

        return ProtocolJson.ToElement(new FixtureSaveResult
        {
            Ok = true,
            Tick = tick,
            SavePath = savePath,
        });
    }
}

internal interface IFixtureSaveWorld
{
    bool IsWorldReady { get; }
    bool IsEventUp { get; }
    bool IsMinigameActive { get; }
    bool IsWarping { get; }
    int Tick { get; }
    string SavePath { get; }
    void MarkFixtureSave();
    Task SaveAsync();
}

internal sealed class SdvFixtureSaveWorld : IFixtureSaveWorld
{
    public bool IsWorldReady => Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame;
    public bool IsEventUp => Game1.eventUp;
    public bool IsMinigameActive => Game1.currentMinigame != null;
    public bool IsWarping => Game1.isWarping;
    public int Tick => Game1.ticks;

    public string SavePath => Path.Combine(
        Constants.SavesPath,
        Game1.player.farmName.Value + "_" + Game1.uniqueIDForThisGame);

    public void MarkFixtureSave()
        => Game1.player.favoriteThing.Value = "sdv-test-fixture";

    public async Task SaveAsync()
    {
        var startedUtc = DateTime.UtcNow;
        var result = InvokeSaveGameSave();
        if (result is Task task)
        {
            await task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        }
        else if (result is IEnumerator enumerator)
        {
            DriveSaveEnumerator(enumerator);
        }
        else
        {
            await WaitForSavingToSettleAsync().ConfigureAwait(false);
        }

        await WaitForSaveFilesAsync(SavePath, startedUtc).ConfigureAwait(false);
    }

    private static void DriveSaveEnumerator(IEnumerator enumerator)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (enumerator.MoveNext())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("fixture.save exceeded 30s budget");
        }
    }

    private static async Task WaitForSavingToSettleAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (ReadIsSaving() && DateTime.UtcNow < deadline)
            await Task.Delay(50).ConfigureAwait(false);

        if (ReadIsSaving())
            throw new TimeoutException("fixture.save exceeded 30s budget");
    }

    private static async Task WaitForSaveFilesAsync(string savePath, DateTime startedUtc)
    {
        var saveName = Path.GetFileName(savePath);
        var mainSave = Path.Combine(savePath, saveName);
        var saveGameInfo = Path.Combine(savePath, "SaveGameInfo");
        var earliestAcceptedWrite = startedUtc.AddSeconds(-2);
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            if (WasWrittenSince(mainSave, earliestAcceptedWrite)
                || WasWrittenSince(saveGameInfo, earliestAcceptedWrite))
            {
                return;
            }

            await Task.Delay(100).ConfigureAwait(false);
        }

        throw new TimeoutException("fixture.save did not observe an updated Stardew save file within 30s");
    }

    private static bool WasWrittenSince(string path, DateTime earliestAcceptedWrite)
        => File.Exists(path) && File.GetLastWriteTimeUtc(path) >= earliestAcceptedWrite;

    private static bool ReadIsSaving()
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        var property = typeof(SaveGame).GetProperty("IsSaving", flags);
        if (property?.GetValue(null) is bool propertyValue)
            return propertyValue;

        var field = typeof(SaveGame).GetField("_isSaving", flags)
            ?? typeof(SaveGame).GetField("isSaving", flags);
        return field?.GetValue(null) is bool fieldValue && fieldValue;
    }

    private static object? InvokeSaveGameSave()
    {
        var method = typeof(SaveGame).GetMethod(
            nameof(SaveGame.Save),
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null)
            ?? throw new InvalidOperationException("StardewValley.SaveGame.Save method not found");

        try
        {
            return method.Invoke(null, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }
    }
}
