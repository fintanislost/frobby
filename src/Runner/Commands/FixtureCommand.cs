using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;
using SdvTestFramework.Runner.Fixtures;

namespace SdvTestFramework.Runner.Commands;

/// <summary>
/// <c>sdv-test fixture [create|list]</c>. `create` builds a fixture from a `.fixture.json`
/// script; `list` enumerates existing fixtures in <c>tests/fixtures/</c>.
/// </summary>
public static class FixtureCommand
{
    public static async Task<int> RunAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 64;
        }

        return args.Span[0] switch
        {
            "create" => await CreateAsync(args[1..], ct),
            "list" => ListAsync(),
            _ => Unknown(args.Span[0]),
        };
    }

    private static int Unknown(string subcommand)
    {
        Console.Error.WriteLine($"fixture: unknown subcommand '{subcommand}'");
        PrintHelp(Console.Error);
        return 64;
    }

    private static async Task<int> CreateAsync(ReadOnlyMemory<string> args, CancellationToken ct)
    {
        // Parse: <name> --from <script> [--mods-path X] [--force]
        string? name = null;
        string? fromPath = null;
        string? modsPath = null;
        bool force = false;

        for (int i = 0; i < args.Length; i++)
        {
            var a = args.Span[i];
            if (a == "--from" && i + 1 < args.Length) { fromPath = args.Span[++i]; continue; }
            if (a == "--mods-path" && i + 1 < args.Length) { modsPath = args.Span[++i]; continue; }
            if (a == "--force") { force = true; continue; }
            if (a.StartsWith("--")) { Console.Error.WriteLine($"unknown flag: {a}"); return 2; }
            if (name is null) { name = a; continue; }
            Console.Error.WriteLine($"unexpected positional argument: {a}");
            return 2;
        }

        if (string.IsNullOrEmpty(name)) { Console.Error.WriteLine("usage: sdv-test fixture create <name> --from <script>"); return 2; }
        if (string.IsNullOrEmpty(fromPath)) { Console.Error.WriteLine("fixture create: --from <script> is required"); return 2; }
        if (!File.Exists(fromPath)) { Console.Error.WriteLine($"script not found at {fromPath}"); return 2; }

        FixtureSpec spec;
        try { spec = FixtureLoader.Load(fromPath); }
        catch (FixtureLoadException ex) { Console.Error.WriteLine($"[load-error] {ex.Message}"); return 2; }

        if (spec.Name != name)
        {
            Console.Error.WriteLine($"name mismatch: CLI arg '{name}' vs script name '{spec.Name}'");
            return 2;
        }

        var fixturesRoot = Path.Combine(Directory.GetCurrentDirectory(), "tests", "fixtures");
        var targetDir = Path.Combine(fixturesRoot, name);
        if (Directory.Exists(targetDir) && !force)
        {
            Console.Error.WriteLine($"tests/fixtures/{name}/ already exists — pass --force to overwrite");
            return 3;
        }

        // Resolve the base fixture exists (if specified) before launching SDV.
        if (!string.IsNullOrEmpty(spec.Base))
        {
            var basePath = Path.Combine(fixturesRoot, spec.Base, "save");
            if (!Directory.Exists(basePath))
            {
                Console.Error.WriteLine($"base fixture '{spec.Base}' not found — did you forget to build it?");
                return 2;
            }
        }

        // Launch SDV + run build via FixtureBuilder.
        return await RunBuildAsync(spec, fromPath, fixturesRoot, modsPath, ct);
    }

    private static async Task<int> RunBuildAsync(
        FixtureSpec spec, string scriptPath, string fixturesRoot, string? modsPath, CancellationToken ct)
    {
        // Resolve mods path (same logic as RunCommand).
        modsPath ??= Environment.GetEnvironmentVariable("SDV_MODS_PATH");
        if (string.IsNullOrEmpty(modsPath))
        {
            modsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "sdv-test-framework", "mods");
        }
        Directory.CreateDirectory(modsPath);
        HarnessDeployer.Deploy(modsPath);

        // Stage the base fixture into SDV's saves dir BEFORE launching SDV.
        var sdvSavesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "StardewValley", "Saves");
        Directory.CreateDirectory(sdvSavesDir);

        if (!string.IsNullOrEmpty(spec.Base))
            FixtureStager.Stage(spec.Base, fixturesRoot, sdvSavesDir);

        // Launch SDV + connect + build.
        var socket = Path.Combine(Path.GetTempPath(), $"sdv-test-fixture-{Guid.NewGuid():N}.sock");
        using var sdv = SdvLauncher.Launch(socket, installPath: null, modsPath: modsPath);
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(60));
            for (int i = 0; i < 120 && !File.Exists(socket); i++)
                await Task.Delay(500, connectCts.Token);
            if (!File.Exists(socket))
                throw new TimeoutException("SDV never opened the test socket");

            using var session = await UnixSocketRpc.ConnectAsync(socket, connectCts.Token);
            var readyTcs = new TaskCompletionSource<JsonRpcNotification>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            session.NotificationReceived += n => { if (n.Method == "ready") readyTcs.TrySetResult(n); };
            _ = session.RunAsync(ct);
            await readyTcs.Task.WaitAsync(TimeSpan.FromSeconds(60), ct);

            var result = await FixtureBuilder.BuildAsync(spec, session, ct);
            if (!result.Success)
            {
                Console.Error.WriteLine($"[build-error] {result.Error}");
                return 4;
            }

            // Capture the save back into the repo. SaveGame.Save writes to <farmName>_<uniqueID>
            // (not the requested fixture name), so use the actual save_path reported by the
            // handler. CaptureFromPath renames the inner save-data file to match the new
            // fixture name so SDV's loader can find it when this fixture is later used as a base.
            var actualSavePath = !string.IsNullOrEmpty(result.SavePath)
                ? result.SavePath
                : Path.Combine(sdvSavesDir, spec.Name);
            FixtureStager.CaptureFromPath(actualSavePath, spec.Name, fixturesRoot);

            // Write script copy + meta + README.
            var targetDir = Path.Combine(fixturesRoot, spec.Name);
            File.Copy(scriptPath, Path.Combine(targetDir, $"{spec.Name}.fixture.json"), overwrite: true);

            var meta = FixtureMetadata.Generate(
                spec,
                sdvVersion: result.SdvVersion,
                smapiVersion: result.SmapiVersion,
                mods: result.Mods,
                farmerName: result.FarmerName,
                farmerGender: result.FarmerGender,
                createdAtUtc: DateTime.UtcNow);

            var metaOptions = new JsonSerializerOptions(SdvTestFramework.Protocol.Json.ProtocolJson.Options) { WriteIndented = true };
            File.WriteAllText(
                Path.Combine(targetDir, $"{spec.Name}.meta.json"),
                JsonSerializer.Serialize(meta, metaOptions));

            File.WriteAllText(
                Path.Combine(targetDir, $"{spec.Name}.README.md"),
                FixtureReadme.Generate(spec, meta));

            Console.WriteLine($"[ok] fixture '{spec.Name}' created at tests/fixtures/{spec.Name}/");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[fixture create] fatal: {ex.Message}");
            return 4;
        }
        finally
        {
            SdvLauncher.Terminate(sdv);
        }
    }

    private static int ListAsync()
    {
        var fixturesRoot = Path.Combine(Directory.GetCurrentDirectory(), "tests", "fixtures");
        if (!Directory.Exists(fixturesRoot)) return 0;

        foreach (var dir in Directory.GetDirectories(fixturesRoot))
        {
            var name = Path.GetFileName(dir);
            var metaPath = Path.Combine(dir, $"{name}.meta.json");
            if (!File.Exists(metaPath)) continue;

            try
            {
                var meta = JsonSerializer.Deserialize<FixtureMetadata>(
                    File.ReadAllText(metaPath), SdvTestFramework.Protocol.Json.ProtocolJson.Options);
                if (meta is not null)
                    Console.WriteLine($"  {meta.Name} — {meta.Description} (created {meta.CreatedAt})");
            }
            catch { /* malformed meta — skip silently */ }
        }
        return 0;
    }

    private static void PrintHelp(TextWriter? output = null)
    {
        var w = output ?? Console.Out;
        w.WriteLine("sdv-test fixture — create/list test fixtures");
        w.WriteLine();
        w.WriteLine("Subcommands:");
        w.WriteLine("  create <name> --from <script.fixture.json> [--mods-path X] [--force]");
        w.WriteLine("      Build a new fixture by loading a base, running steps, and saving.");
        w.WriteLine("  list");
        w.WriteLine("      Enumerate fixtures in tests/fixtures/.");
    }
}
