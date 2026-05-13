using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using SdvTestFramework.Harness.Determinism;
using SdvTestFramework.Harness.Capture;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Patches;
using SdvTestFramework.Harness.Recording;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace SdvTestFramework.Harness;

/// <summary>
/// SMAPI entry point for the test framework harness. Console-command surface is minimal;
/// real test-control comes via the RPC server when <c>SDV_TEST_SOCKET</c> is set.
/// </summary>
public sealed class ModEntry : Mod
{
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly GameThreadDispatch _gameThread = new();
    private RpcDispatcher _rpc = null!;
    private Recording.ActionTraceRecorder? _actionRecorder;

    public override void Entry(IModHelper helper)
    {
        Recorder.Initialize(this.Monitor);
        _actionRecorder = new Recording.ActionTraceRecorder(
            new Recording.FileSink(),
            msg => this.Monitor.Log(msg, LogLevel.Info));
        _rpc = new RpcDispatcher(_gameThread);
        // Lambda wrap: JsonElement → JsonElement? widening isn't a method-group conversion.
        _rpc.Register(StatePlayerHandler.Method, p => StatePlayerHandler.Handle(p));
        _rpc.Register(StateTimeHandler.Method, p => StateTimeHandler.Handle(p));
        _rpc.Register(StateLocationHandler.Method, p => StateLocationHandler.Handle(p));
        _rpc.Register(StateLocationsHandler.Method, p => StateLocationsHandler.Handle(p));
        _rpc.Register(StateMapTileHandler.Method, p => StateMapTileHandler.Handle(p));
        _rpc.Register(StateTileActionsHandler.Method, p => StateTileActionsHandler.Handle(p));
        _rpc.Register(StateNpcHandler.Method, p => StateNpcHandler.Handle(p));
        _rpc.Register(StateNpcsHandler.Method, p => StateNpcsHandler.Handle(p));
        _rpc.Register(StateMenuHandler.Method, p => StateMenuHandler.Handle(p));
        _rpc.Register(StateShopHandler.Method, p => StateShopHandler.Handle(p));
        _rpc.Register(StateSpecialOrdersHandler.Method, p => StateSpecialOrdersHandler.Handle(p));
        _rpc.Register(StateFishingContextHandler.Method, p => StateFishingContextHandler.Handle(p));
        _rpc.Register(StateFishingTableHandler.Method, p => StateFishingTableHandler.Handle(p));
        _rpc.Register(FishingSampleCatchHandler.Method, p => FishingSampleCatchHandler.Handle(p));
        _rpc.Register(StateVisualEffectsHandler.Method, p => StateVisualEffectsHandler.Handle(p));
        _rpc.Register(StateEventHandler.Method, p => StateEventHandler.Handle(p));
        _rpc.Register(EventStartHandler.Method, p => EventStartHandler.Handle(p));
        _rpc.Register(EventSkipHandler.Method, p => EventSkipHandler.Handle(p));
        _rpc.Register(PlayerWarpHandler.Method, p => PlayerWarpHandler.Handle(p));
        _rpc.Register(PlayerGiveItemHandler.Method, p => PlayerGiveItemHandler.Handle(p));
        _rpc.Register(PlayerSetMoneyHandler.Method, p => PlayerSetMoneyHandler.Handle(p));
        _rpc.Register(PlayerAddMailHandler.Method, p => PlayerAddMailHandler.Handle(p));
        _rpc.Register(PlayerAddEventSeenHandler.Method, p => PlayerAddEventSeenHandler.Handle(p));
        _rpc.Register(PlayerSetFriendshipHandler.Method, p => PlayerSetFriendshipHandler.Handle(p));
        _rpc.Register(PlayerSetTransientStateHandler.Method, p => PlayerSetTransientStateHandler.Handle(p));
        _rpc.Register(TimeAdvanceHandler.Method, p => TimeAdvanceHandler.Handle(p));
        _rpc.Register(TimeSetHandler.Method, p => TimeSetHandler.Handle(p));
        SdvTimeNextDayWorld.EventSink = new SmapiTimeNextDayEventSink(helper);
        _rpc.Register(TimeNextDayHandler.Method, p => TimeNextDayHandler.Handle(p));
        _rpc.Register(ShopOpenHandler.Method, p => ShopOpenHandler.Handle(p));
        _rpc.Register(ShopPurchaseHandler.Method, p => ShopPurchaseHandler.Handle(p));
        _rpc.Register(WorldSetWeatherHandler.Method, p => WorldSetWeatherHandler.Handle(p));
        _rpc.Register(WorldWarpNpcHandler.Method, p => WorldWarpNpcHandler.Handle(p));
        _rpc.Register(WorldInteractNpcHandler.Method, p => WorldInteractNpcHandler.Handle(p));
        _rpc.Register(WorldPlaceFurnitureHandler.Method, p => WorldPlaceFurnitureHandler.Handle(p));
        _rpc.Register(WorldPlaceObjectHandler.Method, p => WorldPlaceObjectHandler.Handle(p));
        _rpc.Register(WorldPlaceInventoryFurnitureHandler.Method, p => WorldPlaceInventoryFurnitureHandler.Handle(p));
        _rpc.Register(WorldInteractTileHandler.Method, p => WorldInteractTileHandler.Handle(p));
        _rpc.Register(WorldInteractTileActionHandler.Method, p => WorldInteractTileActionHandler.Handle(p));
        _rpc.Register(DropBoxDepositHandler.Method, p => DropBoxDepositHandler.Handle(p));
        _rpc.Register(CombatAttackHandler.Method, p => CombatAttackHandler.Handle(p));
        _rpc.Register(InputKeyHandler.Method, p => InputKeyHandler.Handle(p));
        _rpc.Register(InputTextHandler.Method, p => InputTextHandler.Handle(p));
        _rpc.Register(InputClickHandler.Method, p => InputClickHandler.Handle(p));
        _rpc.Register(InputClickTextHandler.Method, p => InputClickTextHandler.Handle(p));
        _rpc.Register(InputClickMenuButtonHandler.Method, p => InputClickMenuButtonHandler.Handle(p));
        _rpc.Register(InputClickMenuAdvanceHandler.Method, p => InputClickMenuAdvanceHandler.Handle(p));
        _rpc.Register(InputClickMenuChoiceHandler.Method, p => InputClickMenuChoiceHandler.Handle(p));
        _rpc.Register(InputHoverHandler.Method, p => InputHoverHandler.Handle(p));
        _rpc.Register(InputHoverTextHandler.Method, p => InputHoverTextHandler.Handle(p));
        _rpc.Register(DrawArmHandler.Method, p => DrawArmHandler.Handle(p));
        _rpc.Register(DrawDisarmHandler.Method, p => DrawDisarmHandler.Handle(p));
        _rpc.Register(DrawSnapshotHandler.Method, p => DrawSnapshotHandler.Handle(p));
        _rpc.Register(DrawFindHandler.Method, p => DrawFindHandler.Handle(p));
        _rpc.Register(DrawAssertContainsHandler.Method, p => DrawAssertContainsHandler.Handle(p));
        _rpc.Register(DrawAssertNotContainsHandler.Method, p => DrawAssertNotContainsHandler.Handle(p));
        _rpc.Register(DrawTextSnapshotHandler.Method, p => DrawTextSnapshotHandler.Handle(p));
        _rpc.Register(DrawTextFindHandler.Method, p => DrawTextFindHandler.Handle(p));
        _rpc.Register(DrawAssertTextContainsHandler.Method, p => DrawAssertTextContainsHandler.Handle(p));
        _rpc.Register(DrawAssertTextNotContainsHandler.Method, p => DrawAssertTextNotContainsHandler.Handle(p));
        ScenarioBeginHandler.Monitor = this.Monitor;
        _rpc.Register(ScenarioBeginHandler.Method, p => ScenarioBeginHandler.Handle(p));
        _rpc.Register(ScenarioEndHandler.Method, p => ScenarioEndHandler.Handle(p));
        StateModsHandler.Registry = helper.ModRegistry;
        _rpc.Register(StateModsHandler.Method, p => StateModsHandler.Handle(p));
        ContentAssetHandler.Loader = new Assets.SmapiContentAssetLoader(helper.GameContent);
        _rpc.Register(ContentAssetHandler.Method, p => ContentAssetHandler.Handle(p));
        _rpc.Register(FixtureLoadHandler.Method, p => FixtureLoadHandler.Handle(p));
        _rpc.RegisterAsync(
            FixtureSaveHandler.Method,
            async p => (JsonElement?)await FixtureSaveHandler.HandleAsync(p).ConfigureAwait(false));
        _rpc.Register(GameReturnToTitleHandler.Method, p => GameReturnToTitleHandler.Handle(p));
        FreezeBeginHandler.Monitor = this.Monitor;
        _rpc.Register(FreezeBeginHandler.Method, p => FreezeBeginHandler.Handle(p));
        _rpc.Register(FreezeEndHandler.Method, p => FreezeEndHandler.Handle(p));
        _rpc.Register(FreezeStatusHandler.Method, p => FreezeStatusHandler.Handle(p));
        _rpc.Register(BitmapCaptureHandler.Method, p => BitmapCaptureHandler.Handle(p));
        var renderCapture = new RenderSynchronizedCaptureService();
        BitmapCaptureNextFrameHandler.CaptureService = renderCapture;
        BitmapCaptureNextFrameHandler.CaptureNow = BitmapCaptureWriter.CaptureCurrent;
        _rpc.RegisterAsync(
            BitmapCaptureNextFrameHandler.Method,
            p => BitmapCaptureNextFrameHandler.HandleAsync(p, _shutdownCts.Token));
        _rpc.Register(DiagnosticBuildManifestHandler.Method, p => DiagnosticBuildManifestHandler.Handle(p));
        ScenarioEndHandler.Monitor = this.Monitor;

        var harmony = new Harmony(this.ModManifest.UniqueID);
        Assets.TextureAssetRegistry.Shared = new Assets.TextureAssetRegistry();
        Assets.ContentLoadPatches.Apply(helper, this.Monitor, Assets.TextureAssetRegistry.Shared);

        // Tier 2 texture-hash manifest. Generated by 'sdv-test build-manifest'; absent → empty
        // manifest, Tier 2 no-ops, Tier 3 still populates hash+size.
        var manifestDir = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            ".cache", "sdv-test-framework", "texture-manifests");
        var manifestPath = System.IO.Path.Combine(manifestDir, $"{StardewValley.Game1.version}.json");
        var manifest = Assets.TextureHashManifest.Load(manifestPath);
        Handlers.DrawSnapshotHandler.Manifest = manifest;
        if (manifest.Count == 0)
        {
            this.Monitor.Log(
                $"Texture-manifest for SDV {StardewValley.Game1.version} not found — Tier 2 resolution disabled. " +
                "Run 'sdv-test build-manifest' to generate.",
                LogLevel.Info);
        }
        else
        {
            this.Monitor.Log(
                $"Loaded texture-manifest for SDV {StardewValley.Game1.version} ({manifest.Count} textures).",
                LogLevel.Info);
        }
        SpriteBatchDrawPatches.Apply(harmony, this.Monitor);
        SpriteBatchDrawStringPatches.Apply(harmony, this.Monitor);
        CursorPatches.Apply(harmony, this.Monitor);
        Determinism.TimeFreezePatch.Apply(harmony, this.Monitor);
        Determinism.DeterminismController.UseProductionHooks();

        helper.ConsoleCommands.Add("harness_arm",
            "harness_arm <ticks> [outPath] — record the next N ticks of draws to outPath (defaults /tmp/draws-<pid>.jsonl).",
            this.OnArm);
        helper.ConsoleCommands.Add("harness_disarm",
            "harness_disarm — stop recording immediately and flush.",
            (_, _) => Recorder.Disarm());
        helper.ConsoleCommands.Add("harness_pin_seed",
            "harness_pin_seed <seed> — pin Game1.random to new Random(seed).",
            this.OnPinSeed);
        helper.ConsoleCommands.Add("harness_load",
            "harness_load <save_name> — load a save by folder name (no interactive menu).",
            this.OnLoad);
        helper.ConsoleCommands.Add("harness_record",
            "harness_record <name> — capture current state as a scenario (6 assertions) to ~/.cache/sdv-test-framework/records/<name>.test.json. Name must match [A-Za-z0-9_-]+.",
            this.OnRecord);
        helper.ConsoleCommands.Add("harness_record_actions",
            "harness_record_actions <name> — start recording gameplay actions to ~/.cache/sdv-test-framework/records/actions/<name>.test.json. Stop with harness_record_stop. Name must match [A-Za-z0-9_-]+.",
            this.OnRecordActions);
        helper.ConsoleCommands.Add("harness_record_stop",
            "harness_record_stop — finalize the active action-trace recording session.",
            this.OnRecordActionsStop);

        helper.Events.GameLoop.UpdateTicked += Recorder.OnUpdateTicked;
        helper.Events.Player.Warped += this.OnPlayerWarped;
        helper.Events.Display.MenuChanged += this.OnMenuChanged;
        helper.Events.Display.Rendered += (_, _) => renderCapture.OnRendered(Game1.activeClickableMenu is not null);
        helper.Events.Display.RenderedActiveMenu += (_, _) => renderCapture.OnRenderedActiveMenu();
        helper.Events.GameLoop.UpdateTicked += (_, _) => renderCapture.OnUpdateTicked();
        helper.Events.GameLoop.TimeChanged += this.OnTimeChanged;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTickedDrain;
        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;

        this.Monitor.Log(
            "Harness loaded. Console commands: harness_arm, harness_disarm, harness_pin_seed, harness_load, harness_record, harness_record_actions, harness_record_stop. RPC methods: state.player, state.time, state.location, state.locations, state.map_tile, state.tile_actions, state.npc, state.npcs, state.menu, state.shop, state.special_orders, state.fishing_context, state.fishing_table, state.visual_effects, state.event, state.mods. Fishing: state.fishing_context, state.fishing_table, fishing.sample_catch. Content: content.asset. Manipulators: player.warp, player.give_item, player.set_money, player.add_mail, player.add_event_seen, player.set_friendship, player.set_transient_state, event.start, event.skip, time.advance, time.set, time.next_day, shop.open, shop.purchase, world.set_weather, world.warp_npc, world.interact_npc, world.place_furniture, world.place_object, world.place_inventory_furniture, world.interact_tile, world.interact_tile_action, input.key, input.text, input.click, input.click_text, input.click_menu_button, input.click_menu_advance, input.click_menu_choice, input.hover, input.hover_text. Combat: combat.attack. Draw: draw.arm, draw.disarm, draw.snapshot, draw.find, draw.assert_contains, draw.assert_not_contains, draw.text_snapshot, draw.text_find, draw.assert_text_contains, draw.assert_text_not_contains. Lifecycle: scenario.begin, scenario.end, fixture.load, fixture.save, game.return_to_title. Determinism: freeze.begin, freeze.end, freeze.status. Bitmap: bitmap.capture, bitmap.capture_next_frame. Diagnostic: diagnostic.build_texture_manifest.",
            LogLevel.Info);
    }

    private void OnUpdateTickedDrain(object? sender, UpdateTickedEventArgs e)
    {
        // Drain pending RPC actions onto the game thread. Each action is already try/caught
        // at the dispatcher level, so one bad action can't cascade.
        _gameThread.Drain();
    }

    private void OnGameLaunched(object? sender, StardewModdingAPI.Events.GameLaunchedEventArgs e)
    {
        // Fire after SMAPI has finished its own startup; otherwise a fast-connecting runner
        // would race against mod initialization.
        var socket = Environment.GetEnvironmentVariable("SDV_TEST_SOCKET");
        if (string.IsNullOrEmpty(socket))
        {
            this.Monitor.Log(
                "SDV_TEST_SOCKET not set — harness is idle (console commands only).",
                LogLevel.Trace);
            return;
        }

        _ = Task.Run(() => RunRpcServerAsync(socket, _shutdownCts.Token));
    }

    private async Task RunRpcServerAsync(string socketPath, CancellationToken ct)
    {
        this.Monitor.Log($"Starting RPC server at {socketPath}", LogLevel.Info);
        try
        {
            await UnixSocketRpc.RunServerAsync(socketPath, this.OnClientConnected, ct);
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            this.Monitor.Log($"RPC server crashed: {ex}", LogLevel.Error);
        }
    }

    private async Task OnClientConnected(JsonRpcSession session, CancellationToken ct)
    {
        this.Monitor.Log("RPC client connected; sending ready notification.", LogLevel.Info);

        var version = this.ModManifest.Version.ToString();
        var sdv = Game1.version;
        var smapi = Constants.ApiVersion.ToString();
        var ready = JsonDocument.Parse(
            "{\"version\":\"" + version + "\",\"sdv\":\"" + sdv + "\",\"smapi\":\"" + smapi + "\"}").RootElement;

        // Dispatch every incoming request through the RPC dispatcher, which marshals the
        // handler onto the game thread via GameThreadDispatch.
        session.RequestReceived += req => _ = HandleRequestAsync(session, req, ct);

        await session.SendNotificationAsync("ready", ready, ct);
        await session.RunAsync(ct);
        this.Monitor.Log("RPC client disconnected.", LogLevel.Info);
    }

    private async Task HandleRequestAsync(JsonRpcSession session, JsonRpcRequest req, CancellationToken ct)
    {
        try
        {
            var resp = await _rpc.DispatchAsync(req, ct);
            await session.SendResponseAsync(resp, ct);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"dispatch for {req.Method} threw: {ex}", LogLevel.Error);
            try
            {
                await session.SendResponseAsync(
                    JsonRpcResponse.Fail(req.Id,
                        new JsonRpcError(JsonRpcErrorCode.InternalError, ex.Message)),
                    ct);
            }
            catch { /* peer already gone; swallow */ }
        }
    }

    private void OnArm(string cmd, string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
        {
            this.Monitor.Log("Usage: harness_arm <ticks> [outPath]", LogLevel.Error);
            return;
        }
        var outPath = args.Length >= 2
            ? args[1]
            : Path.Combine("/tmp", $"draws-{Environment.ProcessId}.jsonl");
        try
        {
            Recorder.Arm(ticks, outPath);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"harness_arm failed: {ex.Message}", LogLevel.Error);
        }
    }

    private void OnPinSeed(string cmd, string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
        {
            this.Monitor.Log("Usage: harness_pin_seed <seed>", LogLevel.Error);
            return;
        }
        try
        {
            SeedPinner.Pin(seed, this.Monitor);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"harness_pin_seed failed: {ex.Message}", LogLevel.Error);
        }
    }

    private void OnLoad(string cmd, string[] args)
    {
        if (args.Length < 1)
        {
            this.Monitor.Log("Usage: harness_load <save_name>", LogLevel.Error);
            return;
        }
        var saveName = args[0];

        if (Context.IsWorldReady)
        {
            this.Monitor.Log("Already in a save — return to title before calling harness_load.", LogLevel.Error);
            return;
        }

        try
        {
            Game1.currentLoader = SaveGame.getLoadEnumerator(saveName);
            Game1.gameMode = 6;
            this.Monitor.Log($"Loading save '{saveName}' — watch for SaveLoaded event.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"harness_load failed: {ex}", LogLevel.Error);
        }
    }

    private void OnRecord(string cmd, string[] args)
    {
        if (args.Length < 1)
        {
            this.Monitor.Log("Usage: harness_record <name>", LogLevel.Error);
            return;
        }
        var name = args[0];

        // Capture state from Game1. Console commands run on the game thread, so direct reads are safe.
        var snapshot = new HarnessSnapshot(
            Seed: Scenarios.ScenarioState.Current.Seed,
            InSave: Game1.gameMode == Game1.playingGameMode && Game1.hasLoadedGame,
            Season: Game1.Date.Season.ToString().ToLowerInvariant(),
            DayOfMonth: Game1.Date.DayOfMonth,
            Year: Game1.Date.Year,
            LocationName: Game1.currentLocation?.Name ?? string.Empty,
            Money: Game1.player?.Money ?? 0);

        var outputDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "sdv-test-framework", "records");

        HarnessRecordConsole.BuildAndWrite(
            name: name,
            snapshot: snapshot,
            outputDir: outputDir,
            sink: new FileSink(),
            log: msg => this.Monitor.Log(msg, LogLevel.Info));
    }

    private void OnRecordActions(string cmd, string[] args)
    {
        if (args.Length < 1)
        {
            this.Monitor.Log("Usage: harness_record_actions <name>", LogLevel.Error);
            return;
        }
        var name = args[0];
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z0-9_-]+$"))
        {
            this.Monitor.Log("[harness_record_actions] name must match [A-Za-z0-9_-]+", LogLevel.Error);
            return;
        }
        var outDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", "sdv-test-framework", "records", "actions");
        Directory.CreateDirectory(outDir);
        _actionRecorder!.Start(name, outDir);
    }

    private void OnRecordActionsStop(string cmd, string[] args) => _actionRecorder?.Stop();

    private void OnPlayerWarped(object? sender, StardewModdingAPI.Events.WarpedEventArgs e)
    {
        if (_actionRecorder is null || !_actionRecorder.IsRecording) return;
        _actionRecorder.Record(new Recording.RecordedAction(
            DateTime.UtcNow,
            Recording.ActionKind.Warp,
            Location: e.NewLocation?.Name ?? string.Empty,
            X: Game1.player?.TilePoint.X ?? 0,
            Y: Game1.player?.TilePoint.Y ?? 0));
    }

    private void OnMenuChanged(object? sender, StardewModdingAPI.Events.MenuChangedEventArgs e)
    {
        if (_actionRecorder is null || !_actionRecorder.IsRecording) return;
        if (e.NewMenu is null) return;

        // Heuristic: if a DialogueBox or ShopMenu just opened, find the nearest NPC in the
        // player's location and emit world.interact_npc(name). Spatial proximity is a
        // best-effort guess; user can edit the trace if it's wrong.
        var menuType = e.NewMenu.GetType().Name;
        if (menuType is not ("DialogueBox" or "ShopMenu")) return;

        var loc = Game1.currentLocation;
        if (loc?.characters is null || Game1.player is null) return;

        StardewValley.NPC? closest = null;
        int closestDist = int.MaxValue;
        foreach (var c in loc.characters)
        {
            if (c is null) continue;
            int dx = Math.Abs(c.TilePoint.X - Game1.player.TilePoint.X);
            int dy = Math.Abs(c.TilePoint.Y - Game1.player.TilePoint.Y);
            if (dx + dy < closestDist) { closestDist = dx + dy; closest = c; }
        }
        if (closest is null) return;

        _actionRecorder.Record(new Recording.RecordedAction(
            DateTime.UtcNow,
            Recording.ActionKind.NpcInteract,
            NpcName: closest.Name));
    }

    private void OnTimeChanged(object? sender, StardewModdingAPI.Events.TimeChangedEventArgs e)
    {
        if (_actionRecorder is null || !_actionRecorder.IsRecording) return;
        // SDV time is HHMM. NewTime - OldTime in HHMM doesn't give minute deltas directly;
        // convert each to minutes-since-midnight and subtract.
        int oldMinutes = (e.OldTime / 100) * 60 + (e.OldTime % 100);
        int newMinutes = (e.NewTime / 100) * 60 + (e.NewTime % 100);
        var delta = newMinutes - oldMinutes;
        if (delta <= 0) return;
        _actionRecorder.Record(new Recording.RecordedAction(
            DateTime.UtcNow,
            Recording.ActionKind.TimeAdvance,
            MinutesElapsed: delta));
    }
}
