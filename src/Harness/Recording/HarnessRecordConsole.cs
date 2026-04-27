using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SdvTestFramework.Harness.Recording;

/// <summary>
/// Frozen snapshot of captured game state — passed into
/// <see cref="HarnessRecordConsole.BuildAndWrite"/> so the core logic is unit-testable
/// without Game1 wiring. The <see cref="SdvTestFramework.Harness.ModEntry"/> wrapper
/// populates this from <c>Game1.*</c> on the console-command path.
/// </summary>
public sealed record HarnessSnapshot(
    int Seed,
    bool InSave,
    string Season,
    int DayOfMonth,
    int Year,
    string LocationName,
    int Money);

/// <summary>
/// SMAPI console-command handler for <c>harness_record &lt;name&gt;</c>. Captures current
/// game state as a 6-assertion scenario + writes it via <see cref="IFileSink"/>.
/// </summary>
/// <remarks>
/// Split into a pure-function core (<see cref="BuildAndWrite"/>) + a ModEntry-side wrapper
/// that gathers the <see cref="HarnessSnapshot"/> from live <c>Game1</c> state. Keeps the
/// testable path free of SDV types.
/// </remarks>
public static class HarnessRecordConsole
{
    private static readonly Regex NameRegex = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    /// <summary>
    /// Validates the name + emits the scenario JSON via <paramref name="sink"/>. Logs via
    /// <paramref name="log"/>. Never throws; on validation failure, logs and returns early.
    /// </summary>
    public static void BuildAndWrite(
        string name,
        HarnessSnapshot snapshot,
        string outputDir,
        IFileSink sink,
        Action<string> log)
    {
        if (string.IsNullOrEmpty(name) || !NameRegex.IsMatch(name))
        {
            log($"[harness_record] name must match [A-Za-z0-9_-]+ (got: '{name}')");
            return;
        }

        var path = Path.Combine(outputDir, $"{name}.test.json");
        var existedBefore = File.Exists(path);

        try
        {
            var contents = EmitScenarioJson(name, snapshot);
            sink.Write(path, contents);
            log($"[harness_record] wrote {path} (6 assertions){(existedBefore ? " (overwrote existing file)" : "")}");
        }
        catch (Exception ex)
        {
            log($"[harness_record] write failed: {ex.Message}");
        }
    }

    /// <summary>Serialize the snapshot to a scenario-JSON string with 6 state assertions.</summary>
    internal static string EmitScenarioJson(string name, HarnessSnapshot s)
    {
        var assertions = new JsonArray
        {
            new JsonObject { ["type"] = "state", ["expr"] = $"state.time.in_save == {(s.InSave ? "true" : "false")}" },
            new JsonObject { ["type"] = "state", ["expr"] = $"state.time.season == '{s.Season}'" },
            new JsonObject { ["type"] = "state", ["expr"] = $"state.time.day_of_month == {s.DayOfMonth}" },
            new JsonObject { ["type"] = "state", ["expr"] = $"state.time.year == {s.Year}" },
            new JsonObject { ["type"] = "state", ["expr"] = $"state.location.name == '{s.LocationName}'" },
            new JsonObject { ["type"] = "state", ["expr"] = $"state.player.money == {s.Money}" },
        };
        var obj = new JsonObject
        {
            ["name"] = name,
            ["config"] = new JsonObject { ["seed"] = s.Seed },
            ["steps"] = new JsonArray(),
            ["assertions"] = assertions,
        };
        return obj.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            // Keep single quotes as-is so state DSL expressions like "state.time.season == 'spring'"
            // remain readable (and match-able in tests / diffs). UnsafeRelaxedJsonEscaping is safe
            // here because the output is developer-authored scenario files, not HTML context.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }
}
