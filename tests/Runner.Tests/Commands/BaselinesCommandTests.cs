using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Commands;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Commands;

/// <summary>
/// Covers the four <c>sdv-test baselines</c> subcommands: <c>list</c> (presence check),
/// <c>update</c> (dispatches to the swappable <see cref="BaselinesCommand.RunExecutor"/>
/// seam without launching SDV), <c>show</c> (PNG metadata via ImageSharp's no-decode
/// identify), and <c>delete --force</c> (filesystem removal).
/// </summary>
[Collection("Console")]
public class BaselinesCommandTests
{
    private static void WriteScenario(string path, string baselineRelPath)
    {
        File.WriteAllText(path,
            "{\"name\":\"s\",\"config\":{\"seed\":42},\"steps\":[],\"assertions\":[" +
            "{\"type\":\"bitmap\",\"baseline\":\"" + baselineRelPath + "\",\"tolerance\":0.95}" +
            "]}");
    }

    private static void WriteSolidPng(string path, byte r, byte g, byte b)
    {
        using var img = new Image<Rgba32>(8, 8);
        for (int y = 0; y < 8; y++)
        for (int x = 0; x < 8; x++)
            img[x, y] = new Rgba32(r, g, b, 255);
        img.SaveAsPng(path);
    }

    [Fact]
    public async Task List_EnumeratesReferencedBaselines_MarksMissingPresent()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"bl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            // Two scenarios; one's baseline exists, the other's doesn't.
            WriteScenario(Path.Combine(tmp, "a.test.json"), "baselines/a.png");
            WriteScenario(Path.Combine(tmp, "b.test.json"), "baselines/b.png");
            Directory.CreateDirectory(Path.Combine(tmp, "baselines"));
            WriteSolidPng(Path.Combine(tmp, "baselines/a.png"), 0, 0, 0);
            // baselines/b.png deliberately not created → should show MISSING

            var sw = new StringWriter();
            var origOut = Console.Out;
            Console.SetOut(sw);
            try
            {
                var rc = await BaselinesCommand.RunAsync(
                    new ReadOnlyMemory<string>(new[] { "list", "--scenarios", tmp }),
                    CancellationToken.None);
                Assert.Equal(0, rc);
            }
            finally { Console.SetOut(origOut); }

            var output = sw.ToString();
            Assert.Contains("a.png", output);
            Assert.Contains("PRESENT", output);
            Assert.Contains("b.png", output);
            Assert.Contains("MISSING", output);
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task Update_DispatchesToRunCommandWithUpdateMode()
    {
        // Test seam: BaselinesCommand exposes a static delegate for the run-executor.
        // Default points to RunCommand.RunFromOptions (production); tests substitute a probe.
        bool dispatched = false;
        bool updateBaselinesSeen = false;
        Func<RunCommandOptions, CancellationToken, Task<int>> origExecutor = BaselinesCommand.RunExecutor;
        BaselinesCommand.RunExecutor = (opts, ct) =>
        {
            dispatched = true;
            updateBaselinesSeen = opts.UpdateBaselines;
            return Task.FromResult(0);
        };
        try
        {
            var rc = await BaselinesCommand.RunAsync(
                new ReadOnlyMemory<string>(new[] { "update", "tests/samples/" }),
                CancellationToken.None);
            Assert.Equal(0, rc);
            Assert.True(dispatched);
            Assert.True(updateBaselinesSeen);
        }
        finally { BaselinesCommand.RunExecutor = origExecutor; }
    }

    [Fact]
    public async Task Show_PrintsPngMetadata()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"bl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var p = Path.Combine(tmp, "x.png");
            WriteSolidPng(p, 0, 0, 0);

            var sw = new StringWriter();
            var origOut = Console.Out;
            Console.SetOut(sw);
            try
            {
                var rc = await BaselinesCommand.RunAsync(
                    new ReadOnlyMemory<string>(new[] { "show", p }),
                    CancellationToken.None);
                Assert.Equal(0, rc);
            }
            finally { Console.SetOut(origOut); }

            var output = sw.ToString();
            Assert.Contains("8", output);          // dimensions 8x8
            Assert.Contains("bytes", output);      // file size present
        }
        finally { Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public async Task Delete_WithForce_RemovesFile()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"bl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmp);
        try
        {
            var p = Path.Combine(tmp, "doomed.png");
            WriteSolidPng(p, 0, 0, 0);
            Assert.True(File.Exists(p));

            var rc = await BaselinesCommand.RunAsync(
                new ReadOnlyMemory<string>(new[] { "delete", p, "--force" }),
                CancellationToken.None);
            Assert.Equal(0, rc);
            Assert.False(File.Exists(p));
        }
        finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true); }
    }
}
