using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Commands;

namespace SdvTestFramework.Runner;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        // POSIX signal handlers for non-TTY shutdown paths (backgrounded jobs,
        // `kill` from another shell, systemd stop, etc.). Console.CancelKeyPress only
        // fires on controlling-terminal Ctrl-C, so these catch the bg-job case.
        // PosixSignalRegistration is a no-op on Windows — harmless.
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
        {
            ctx.Cancel = true;   // suppress default abrupt termination
            cts.Cancel();
        });
        using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx =>
        {
            ctx.Cancel = true;
            cts.Cancel();
        });

        return args[0] switch
        {
            "probe" => await ProbeCommand.RunAsync(args.AsMemory()[1..], cts.Token),
            "doctor" => await DoctorCommand.RunAsync(args.AsMemory()[1..], cts.Token),
            "list" => await ListCommand.RunAsync(args.AsMemory()[1..], cts.Token),
            "run" => await RunCommand.RunAsync(args.AsMemory()[1..], cts.Token),
            "run-suite" => await RunSuiteCommand.RunAsync(args.AsMemory()[1..], cts.Token),
            "repo" => await RepoCommand.RunAsync(args.AsMemory()[1..], cts.Token),
            "fixture" => await FixtureCommand.RunAsync(args.AsMemory()[1..], cts.Token),
            "record" => await RecordCommand.RunAsync(args.AsMemory()[1..], cts.Token),
            "mcp" => await McpCommand.RunAsync(args.AsMemory()[1..], cts.Token),
            "build-manifest" => await BuildManifestCommand.RunAsync(args.AsMemory()[1..], cts.Token),
            "baselines" => await BaselinesCommand.RunAsync(args.AsMemory()[1..], cts.Token),
            "cache" => await CacheCommand.RunAsync(args.AsMemory()[1..], cts.Token),
            _ => Unknown(args[0]),
        };
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintHelp(Console.Error);
        return 64;
    }

    private static void PrintHelp(System.IO.TextWriter? output = null)
    {
        var w = output ?? Console.Out;
        w.WriteLine("sdv-test — M1 runner (early)");
        w.WriteLine();
        w.WriteLine("Commands:");
        w.WriteLine("  probe [socket]    Connect to a running harness, print the 'ready' notification,");
        w.WriteLine("                    then invoke state.player and print the result.");
        w.WriteLine("                    If [socket] omitted, uses $SDV_TEST_SOCKET.");
        w.WriteLine("  doctor            Verify local environment (.NET, SDV install, SMAPI, Saves dir).");
        w.WriteLine("  list [path]       Scan <path> (default: cwd) recursively for *.test.json and validate each.");
        w.WriteLine("  run [--filter <p>] [--mods-path <p>] [--extra-mod <path>] [--headless] [--reporter <c|tap|junit>] [--output <path>] [--watch] [--update-baselines] [--tier <generic|ci-ubuntu|self-hosted-nvidia>] [paths...]");
        w.WriteLine("                    Launch SDV, run scenarios, print summary.");
        w.WriteLine("                    --filter: case-insensitive substring on scenario name.");
        w.WriteLine("                    --mods-path: isolated mods dir for the harness to load from.");
        w.WriteLine("                                 Defaults to ~/.cache/sdv-test-framework/mods.");
        w.WriteLine("                    --extra-mod: repeatable built SMAPI mod folder to stage into --mods-path.");
        w.WriteLine("                                 Also reads path-list entries from $SDV_EXTRA_MODS.");
        w.WriteLine("                    --headless: launch through xvfb-run so SDV does not use");
        w.WriteLine("                                the active desktop display or mouse cursor.");
        w.WriteLine("                    --reporter: output format. One of 'console' (default),");
        w.WriteLine("                                'tap' (TAP 13), 'junit' (Jenkins XML).");
        w.WriteLine("                    --output: write reporter output to this path. Defaults to stdout.");
        w.WriteLine("                    --watch: stay resident; rerun scenarios on *.test.json changes.");
        w.WriteLine("                             SDV subprocess reused across reruns. Ctrl-C to exit.");
        w.WriteLine("                    --update-baselines: bitmap assertions with a missing or stale baseline");
        w.WriteLine("                                        write the current capture as the new baseline + pass.");
        w.WriteLine("                    --tier: tolerance preset for bitmap assertions. Maps to per-method");
        w.WriteLine("                            defaults: generic→0.95 SSIM, ci-ubuntu→0.98, self-hosted-nvidia→0.999.");
        w.WriteLine("  run-suite [--fresh-process-per-scenario] [--filter <p>] [--mods-path <p>] [--headless]");
        w.WriteLine("            [--extra-mod <path>] [--report-dir <path>] [paths...]");
        w.WriteLine("                    Run each discovered scenario via a separate 'run' invocation.");
        w.WriteLine("                    This is the preferred flow for mod UI suites that need a fresh");
        w.WriteLine("                    SMAPI process per scenario while sharing one report hub.");
        w.WriteLine("  repo run [--repo-root <path>] [--visible|--headless] [--no-build] [--dry-run]");
        w.WriteLine("           [--baseline] [--mod-set <name>] [--report-dir <path>] [targets...]");
        w.WriteLine("                    Run a repo-local Frobby scaffold from sdv-test.config.json.");
        w.WriteLine("  repo repeat [--count|-n <count>] [repo run options]");
        w.WriteLine("                    Repeat repo-local runs; first run may build, later runs skip build.");
        w.WriteLine("  repo init         Placeholder registered for the scaffold generator task.");
        w.WriteLine("  fixture create <name> --from <script>");
        w.WriteLine("                    Build a reproducible save-state fixture in tests/fixtures/.");
        w.WriteLine("  fixture list      Enumerate existing fixtures.");
        w.WriteLine("  record <name> [--mods-path X] [--output path] [--force]");
        w.WriteLine("                    Launch SDV, capture external RPC calls as scenario steps,");
        w.WriteLine("                    write to tests/samples/<name>.test.json on Ctrl-C.");
        w.WriteLine("                    Filters out state.* reads and scenario.begin/end.");
        w.WriteLine("  mcp               Run the MCP stdio server for Claude Code / MCP clients.");
        w.WriteLine("                    Reads JSON-RPC 2.0 requests from stdin, writes responses to stdout.");
        w.WriteLine("                    Configure via .mcp.json (see docs/mcp-quickstart.md).");
        w.WriteLine("  build-manifest [--output <path>] [--mods-path <path>]");
        w.WriteLine("                    Build a texture-hash manifest for the installed SDV version.");
        w.WriteLine("                    Resolves the 9.2% of textures that Tier 1 (IContentEvents)");
        w.WriteLine("                    misses. Writes ~/.cache/sdv-test-framework/texture-manifests/");
        w.WriteLine("                    <sdv-version>.json by default. Run once per SDV version install.");
        w.WriteLine("  baselines <list|update|show|delete> [args]");
        w.WriteLine("                    Manage bitmap baselines.");
        w.WriteLine("                    list [--scenarios <dir>]: enumerate referenced baselines + presence.");
        w.WriteLine("                    update <path-or-glob> [--tier <n>] [--mods-path <p>]: rerun with --update-baselines.");
        w.WriteLine("                    show <path>: print PNG metadata.");
        w.WriteLine("                    delete <path> [--force]: remove file (prompts unless --force).");
        w.WriteLine("  cache clean [--max-age <days>] [--keep-runs <n>] [--dry-run]");
        w.WriteLine("                    Sweep the bitmap-capture cache directory. A file is kept iff its");
        w.WriteLine("                    mtime is within --max-age (default 7) AND its scenario subdir is among");
        w.WriteLine("                    the --keep-runs most recent (default 5). Override location via");
        w.WriteLine("                    $SDV_CACHE_DIR (default ~/.cache/sdv-test-framework/captures).");
    }
}
