using System;
using System.Globalization;
using System.IO;
using HarmonyLib;
using SdvTestFramework.SpikeHarness.Determinism;
using SdvTestFramework.SpikeHarness.Patches;
using SdvTestFramework.SpikeHarness.Recording;
using StardewModdingAPI;
using StardewValley;

namespace SdvTestFramework.SpikeHarness;

/// <summary>
/// M0 determinism-spike entry point. No socket/RPC in the spike — control via SMAPI console
/// commands exclusively. See docs/spikes/2026-04-determinism/REPORT.md.
/// </summary>
public sealed class ModEntry : Mod
{
    public override void Entry(IModHelper helper)
    {
        Recorder.Initialize(this.Monitor);

        var harmony = new Harmony(this.ModManifest.UniqueID);
        SpriteBatchDrawPatches.Apply(harmony, this.Monitor);
        CursorPatches.Apply(harmony, this.Monitor);

        helper.ConsoleCommands.Add("harness_arm",
            "harness_arm <ticks> [outPath] — record the next N ticks of draws to outPath (defaults /tmp/draws-<pid>.jsonl).",
            this.OnArm);
        helper.ConsoleCommands.Add("harness_disarm",
            "harness_disarm — stop recording immediately and flush.",
            (_, _) => Recorder.Disarm());
        helper.ConsoleCommands.Add("harness_snapshot",
            "harness_snapshot [outPath] — record a single tick and flush. Convenience.",
            this.OnSnapshot);
        helper.ConsoleCommands.Add("harness_pin_seed",
            "harness_pin_seed <seed> — pin Game1.random to new Random(seed).",
            this.OnPinSeed);
        helper.ConsoleCommands.Add("harness_save",
            "harness_save — save the current game without sleeping. For fixture creation.",
            this.OnSave);
        helper.ConsoleCommands.Add("harness_load",
            "harness_load <save_name> — load a save by folder name (no interactive menu). Asynchronous — wait for SaveLoaded in the log before arming.",
            this.OnLoad);

        helper.Events.GameLoop.UpdateTicked += Recorder.OnUpdateTicked;

        this.Monitor.Log(
            "M0 spike harness loaded. Commands: harness_arm, harness_disarm, harness_snapshot, harness_pin_seed, harness_save, harness_load.",
            LogLevel.Info);
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

    private void OnSnapshot(string cmd, string[] args)
    {
        var outPath = args.Length >= 1
            ? args[0]
            : Path.Combine("/tmp", $"draws-{Environment.ProcessId}-snap.jsonl");
        try
        {
            Recorder.Arm(1, outPath);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"harness_snapshot failed: {ex.Message}", LogLevel.Error);
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

    private void OnSave(string cmd, string[] args)
    {
        // Preconditions per sdv-conventions.md: don't try to save during transitional states.
        if (!Context.IsWorldReady)
        {
            this.Monitor.Log("Cannot save — world not ready (still on menu or loading).", LogLevel.Error);
            return;
        }
        if (Game1.eventUp || Game1.currentMinigame != null || Game1.isWarping)
        {
            this.Monitor.Log("Cannot save mid-event / mid-warp / in minigame.", LogLevel.Error);
            return;
        }

        try
        {
            // SDV 1.6: SaveGame.Save() drives the save enumerator to completion synchronously.
            // Must run on the game thread — SMAPI console commands do, so this is fine.
            SaveGame.Save();
            this.Monitor.Log("Save complete.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"harness_save failed: {ex}", LogLevel.Error);
        }
    }

    private void OnLoad(string cmd, string[] args)
    {
        if (args.Length < 1)
        {
            this.Monitor.Log("Usage: harness_load <save_name>  (directory name in StardewValley/Saves/)", LogLevel.Error);
            return;
        }
        var saveName = args[0];

        // Must be at the title / menu state, not already in a save.
        if (Context.IsWorldReady)
        {
            this.Monitor.Log("Already in a save — return to title before calling harness_load.", LogLevel.Error);
            return;
        }

        try
        {
            // SDV's load sequence: set Game1.currentLoader to the save's enumerator and flip
            // gameMode to loadingMode (6). The normal update loop drives the enumerator to
            // completion; SMAPI's SaveLoaded event fires when ready.
            Game1.currentLoader = SaveGame.getLoadEnumerator(saveName);
            Game1.gameMode = 6;
            this.Monitor.Log($"Loading save '{saveName}' — watch for SaveLoaded event.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"harness_load failed: {ex}", LogLevel.Error);
        }
    }
}
