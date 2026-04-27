using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Commands;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

/// <summary>
/// Tests the args-parsing, discovery, filter, and error paths of the `run` command that don't
/// require a live SDV subprocess. The full end-to-end path (launch SDV, wait for ready, execute
/// scenarios) is covered by the documented manual integration test — automating it needs a
/// running SDV instance and is out of scope for unit tests.
/// </summary>
[Collection("Console")]
public class RunCommandTests
{
    [Fact]
    public async Task Run_NoScenarios_ReturnsZero()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"run-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var outW = new StringWriter();
            var priorOut = Console.Out;
            Console.SetOut(outW);
            int exit;
            try
            {
                exit = await RunCommand.RunAsync(
                    new ReadOnlyMemory<string>(new[] { dir }),
                    CancellationToken.None);
            }
            finally { Console.SetOut(priorOut); }
            Assert.Equal(0, exit);
            Assert.Contains("no scenarios matched", outW.ToString());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Run_PathNotFound_ReturnsTwo()
    {
        var errW = new StringWriter();
        var priorErr = Console.Error;
        Console.SetError(errW);
        int exit;
        try
        {
            exit = await RunCommand.RunAsync(
                new ReadOnlyMemory<string>(new[] { "/tmp/definitely-not-a-real-path-" + Guid.NewGuid().ToString("N") }),
                CancellationToken.None);
        }
        finally { Console.SetError(priorErr); }
        Assert.Equal(2, exit);
        Assert.Contains("path not found", errW.ToString());
    }

    [Fact]
    public async Task Run_FilterExcludesEverything_ReturnsZero()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"run-filter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.test.json"),
                "{\"name\":\"alpha\",\"steps\":[]}");
            File.WriteAllText(Path.Combine(dir, "b.test.json"),
                "{\"name\":\"beta\",\"steps\":[]}");

            var outW = new StringWriter();
            var priorOut = Console.Out;
            Console.SetOut(outW);
            int exit;
            try
            {
                exit = await RunCommand.RunAsync(
                    new ReadOnlyMemory<string>(new[] { "--filter", "omega", dir }),
                    CancellationToken.None);
            }
            finally { Console.SetOut(priorOut); }
            Assert.Equal(0, exit);
            Assert.Contains("no scenarios matched", outW.ToString());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Run_ModsPathFlag_ConsumedNotTreatedAsPath()
    {
        var mods = Path.Combine(Path.GetTempPath(), $"run-mp-{Guid.NewGuid():N}");
        var scenarios = Path.Combine(Path.GetTempPath(), $"run-sc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(mods);
        Directory.CreateDirectory(scenarios);
        try
        {
            var outW = new StringWriter();
            var priorOut = Console.Out;
            Console.SetOut(outW);
            int exit;
            try
            {
                exit = await RunCommand.RunAsync(
                    new ReadOnlyMemory<string>(new[] { "--mods-path", mods, scenarios }),
                    CancellationToken.None);
            }
            finally { Console.SetOut(priorOut); }
            Assert.Equal(0, exit);
            Assert.Contains("no scenarios matched", outW.ToString());
        }
        finally
        {
            Directory.Delete(mods, recursive: true);
            Directory.Delete(scenarios, recursive: true);
        }
    }

    [Fact]
    public async Task Run_ExtraModFlag_DeploysModIntoModsDir()
    {
        var root = Path.Combine(Path.GetTempPath(), $"run-extra-{Guid.NewGuid():N}");
        var mods = Path.Combine(root, "mods");
        var scenarios = Path.Combine(root, "scenarios");
        var extra = Path.Combine(root, "extra");
        Directory.CreateDirectory(mods);
        Directory.CreateDirectory(scenarios);
        Directory.CreateDirectory(extra);
        try
        {
            File.WriteAllText(
                Path.Combine(extra, "manifest.json"),
                "{\"Name\":\"Probe\",\"UniqueID\":\"Example.Probe\",\"EntryDll\":\"Probe.dll\"}");
            File.WriteAllText(Path.Combine(extra, "Probe.dll"), "not real");

            var outW = new StringWriter();
            var priorOut = Console.Out;
            Console.SetOut(outW);
            int exit;
            try
            {
                exit = await RunCommand.RunAsync(
                    new ReadOnlyMemory<string>(new[] { "--mods-path", mods, "--extra-mod", extra, scenarios }),
                    CancellationToken.None);
            }
            finally { Console.SetOut(priorOut); }

            Assert.Equal(0, exit);
            Assert.True(File.Exists(Path.Combine(mods, "Example.Probe", "manifest.json")));
            Assert.True(File.Exists(Path.Combine(mods, "Example.Probe", "Probe.dll")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Run_ExtraModMissingManifest_ReturnsTwo()
    {
        var root = Path.Combine(Path.GetTempPath(), $"run-extra-bad-{Guid.NewGuid():N}");
        var mods = Path.Combine(root, "mods");
        var scenarios = Path.Combine(root, "scenarios");
        var extra = Path.Combine(root, "extra");
        Directory.CreateDirectory(mods);
        Directory.CreateDirectory(scenarios);
        Directory.CreateDirectory(extra);
        try
        {
            var errW = new StringWriter();
            var priorErr = Console.Error;
            Console.SetError(errW);
            int exit;
            try
            {
                exit = await RunCommand.RunAsync(
                    new ReadOnlyMemory<string>(new[] { "--mods-path", mods, "--extra-mod", extra, scenarios }),
                    CancellationToken.None);
            }
            finally { Console.SetError(priorErr); }

            Assert.Equal(2, exit);
            Assert.Contains("[extra-mod]", errW.ToString());
            Assert.Contains("manifest", errW.ToString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Run_InvalidScenarioFile_ReturnsTwo()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"run-bad-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "broken.test.json"), "{not json");

            var errW = new StringWriter();
            var priorErr = Console.Error;
            Console.SetError(errW);
            int exit;
            try
            {
                exit = await RunCommand.RunAsync(
                    new ReadOnlyMemory<string>(new[] { dir }),
                    CancellationToken.None);
            }
            finally { Console.SetError(priorErr); }
            Assert.Equal(2, exit);
            Assert.Contains("load-error", errW.ToString());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task Run_AutoDeploysHarnessIntoModsDir()
    {
        var mods = Path.Combine(Path.GetTempPath(), $"run-deploy-{Guid.NewGuid():N}");
        var scenarios = Path.Combine(Path.GetTempPath(), $"run-deploy-scen-{Guid.NewGuid():N}");
        Directory.CreateDirectory(mods);
        Directory.CreateDirectory(scenarios);
        try
        {
            var outW = new StringWriter();
            var priorOut = Console.Out;
            Console.SetOut(outW);
            try
            {
                // Empty scenarios → early return 0 BUT the deploy should have already happened.
                await RunCommand.RunAsync(
                    new ReadOnlyMemory<string>(new[] { "--mods-path", mods, scenarios }),
                    CancellationToken.None);
            }
            finally { Console.SetOut(priorOut); }

            var target = Path.Combine(mods, "SdvTestFramework.Harness");
            Assert.True(Directory.Exists(target), $"expected deploy target {target}");
            Assert.True(File.Exists(Path.Combine(target, "Harness.dll")));
            Assert.True(File.Exists(Path.Combine(target, "manifest.json")));
        }
        finally
        {
            Directory.Delete(mods, recursive: true);
            Directory.Delete(scenarios, recursive: true);
        }
    }
}
