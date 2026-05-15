using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Commands;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

[Collection("Console")]
public class RunSuiteCommandTests
{
    [Fact]
    public async Task RunSuite_DiscoversScenariosInStableOrder_AndPassesCoreFlagsToEachRun()
    {
        var root = Path.Combine(Path.GetTempPath(), $"suite-{Guid.NewGuid():N}");
        var scenarios = Path.Combine(root, "scenarios");
        var reports = Path.Combine(root, "reports");
        var mods = Path.Combine(root, "mods");
        var extra = Path.Combine(root, "extra");
        Directory.CreateDirectory(scenarios);
        Directory.CreateDirectory(mods);
        Directory.CreateDirectory(extra);
        try
        {
            File.WriteAllText(Path.Combine(scenarios, "02-beta.test.json"), """{"name":"beta","steps":[]}""");
            File.WriteAllText(Path.Combine(scenarios, "01-alpha.test.json"), """{"name":"alpha","steps":[]}""");

            var calls = new List<string[]>();
            var original = RunSuiteCommand.RunExecutor;
            RunSuiteCommand.RunExecutor = (args, _) =>
            {
                calls.Add(args.ToArray());
                return Task.FromResult(0);
            };
            try
            {
                var exit = await RunSuiteCommand.RunAsync(
                    new[]
                    {
                        "--fresh-process-per-scenario",
                        "--mods-path", mods,
                        "--extra-mod", extra,
                        "--report-dir", reports,
                        "--tier", "self-hosted-nvidia",
                        "--diff-format", "triptych",
                        "--headless",
                        scenarios,
                    }.AsMemory(),
                    CancellationToken.None);

                Assert.Equal(0, exit);
            }
            finally { RunSuiteCommand.RunExecutor = original; }

            Assert.Equal(2, calls.Count);
            Assert.EndsWith("01-alpha.test.json", calls[0].Last());
            Assert.EndsWith("02-beta.test.json", calls[1].Last());
            Assert.DoesNotContain("--fresh-process-per-scenario", calls.SelectMany(c => c));
            foreach (var call in calls)
            {
                Assert.Contains("--mods-path", call);
                Assert.Contains(mods, call);
                Assert.Contains("--extra-mod", call);
                Assert.Contains(extra, call);
                Assert.Contains("--report-dir", call);
                Assert.Contains(reports, call);
                Assert.Contains("--tier", call);
                Assert.Contains("self-hosted-nvidia", call);
                Assert.Contains("--diff-format", call);
                Assert.Contains("triptych", call);
                Assert.Contains("--headless", call);
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunSuite_ConfigOverlayFlag_IsPassedToChildRun()
    {
        var root = Path.Combine(Path.GetTempPath(), $"suite-overlay-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var scenario = Path.Combine(root, "a.test.json");
            File.WriteAllText(scenario, """{"name":"a","steps":[]}""");
            var calls = new List<string[]>();
            var original = RunSuiteCommand.RunExecutor;
            RunSuiteCommand.RunExecutor = (args, _) =>
            {
                calls.Add(args.ToArray());
                return Task.FromResult(0);
            };

            try
            {
                var exit = await RunSuiteCommand.RunAsync(
                    new[]
                    {
                        "--config-overlay", "/tmp/source.json", "Example.Mod", "config.json",
                        "--profile-id", "profile-a",
                        root,
                    }.AsMemory(),
                    CancellationToken.None);

                Assert.Equal(0, exit);
            }
            finally
            {
                RunSuiteCommand.RunExecutor = original;
            }

            var call = Assert.Single(calls);
            Assert.Contains("--config-overlay", call);
            Assert.Contains("/tmp/source.json", call);
            Assert.Contains("Example.Mod", call);
            Assert.Contains("config.json", call);
            Assert.Contains("--profile-id", call);
            Assert.Contains("profile-a", call);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunSuite_ConfigOverlayFlag_MissingTargetPathBeforeScenarioDirReturnsTwo()
    {
        var root = Path.Combine(Path.GetTempPath(), $"suite-overlay-missing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.test.json"), """{"name":"a","steps":[]}""");
            var calls = 0;
            var original = RunSuiteCommand.RunExecutor;
            RunSuiteCommand.RunExecutor = (_, _) =>
            {
                calls++;
                return Task.FromResult(0);
            };
            var errW = new StringWriter();
            var priorErr = Console.Error;
            Console.SetError(errW);

            try
            {
                var exit = await RunSuiteCommand.RunAsync(
                    new[]
                    {
                        "--config-overlay", "/tmp/source.json", "Example.Mod", root,
                    }.AsMemory(),
                    CancellationToken.None);

                Assert.Equal(2, exit);
            }
            finally
            {
                Console.SetError(priorErr);
                RunSuiteCommand.RunExecutor = original;
            }

            Assert.Equal(0, calls);
            Assert.Contains("--config-overlay", errW.ToString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunSuite_FilterMatchesScenarioNamesBeforeInvokingRuns()
    {
        var root = Path.Combine(Path.GetTempPath(), $"suite-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "01-alpha.test.json"), """{"name":"alpha","steps":[]}""");
            File.WriteAllText(Path.Combine(root, "02-beta.test.json"), """{"name":"beta","steps":[]}""");

            var calls = new List<string[]>();
            var original = RunSuiteCommand.RunExecutor;
            RunSuiteCommand.RunExecutor = (args, _) =>
            {
                calls.Add(args.ToArray());
                return Task.FromResult(0);
            };
            try
            {
                var exit = await RunSuiteCommand.RunAsync(
                    new[] { "--filter", "bet", "--no-report", root }.AsMemory(),
                    CancellationToken.None);

                Assert.Equal(0, exit);
            }
            finally { RunSuiteCommand.RunExecutor = original; }

            Assert.Single(calls);
            Assert.EndsWith("02-beta.test.json", calls[0].Last());
            Assert.DoesNotContain("--filter", calls[0]);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunSuite_ContinuesAfterChildFailure_AndReturnsOne()
    {
        var root = Path.Combine(Path.GetTempPath(), $"suite-fail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "01-alpha.test.json"), """{"name":"alpha","steps":[]}""");
            File.WriteAllText(Path.Combine(root, "02-beta.test.json"), """{"name":"beta","steps":[]}""");

            var calls = 0;
            var original = RunSuiteCommand.RunExecutor;
            RunSuiteCommand.RunExecutor = (args, _) =>
            {
                calls++;
                return Task.FromResult(args.ToArray().Last().Contains("01-alpha", StringComparison.Ordinal)
                    ? 1
                    : 0);
            };

            var outW = new StringWriter();
            var priorOut = Console.Out;
            Console.SetOut(outW);
            try
            {
                var exit = await RunSuiteCommand.RunAsync(
                    new[] { "--no-report", root }.AsMemory(),
                    CancellationToken.None);

                Assert.Equal(1, exit);
            }
            finally
            {
                Console.SetOut(priorOut);
                RunSuiteCommand.RunExecutor = original;
            }

            Assert.Equal(2, calls);
            Assert.Contains("[suite] 1/2 passed", outW.ToString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RunSuite_InvalidScenario_ReturnsTwoBeforeRunningChildren()
    {
        var root = Path.Combine(Path.GetTempPath(), $"suite-invalid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "broken.test.json"), "{not json");

            var calls = 0;
            var original = RunSuiteCommand.RunExecutor;
            RunSuiteCommand.RunExecutor = (_, _) =>
            {
                calls++;
                return Task.FromResult(0);
            };
            try
            {
                var exit = await RunSuiteCommand.RunAsync(
                    new[] { "--no-report", root }.AsMemory(),
                    CancellationToken.None);

                Assert.Equal(2, exit);
            }
            finally { RunSuiteCommand.RunExecutor = original; }

            Assert.Equal(0, calls);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
