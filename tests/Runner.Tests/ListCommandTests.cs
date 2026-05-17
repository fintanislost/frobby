using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Commands;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

/// <summary>
/// Tests the `list` command: recursive scan of a directory for <c>*.test.json</c>
/// files, per-file validation via <see cref="Protocol.Scenarios.ScenarioLoader"/>,
/// and summary-line reporting. Covers the valid/invalid mix, empty-directory,
/// not-a-directory, and nested-walk paths.
/// </summary>
[Collection("Console")]
public class ListCommandTests
{
    [Fact]
    public async Task Run_ValidAndInvalidFiles_CountsBoth()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"list-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "ok.test.json"),
                "{\"name\":\"x\",\"steps\":[]}");
            File.WriteAllText(Path.Combine(dir, "bad.test.json"),
                "{\"oops\":true}");

            var outW = new StringWriter();
            var priorOut = Console.Out;
            Console.SetOut(outW);
            int exit;
            try
            {
                exit = await ListCommand.RunAsync(
                    new ReadOnlyMemory<string>(new[] { dir }),
                    CancellationToken.None);
            }
            finally
            {
                Console.SetOut(priorOut);
            }

            Assert.Equal(1, exit); // because one invalid
            var output = outW.ToString();
            Assert.Contains("[ok]", output);
            Assert.Contains("[invalid]", output);
            Assert.Contains("1 ok, 1 invalid", output);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Run_EmptyDirectory_ReturnsZero()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"list-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var outW = new StringWriter();
            var priorOut = Console.Out;
            Console.SetOut(outW);
            int exit;
            try
            {
                exit = await ListCommand.RunAsync(
                    new ReadOnlyMemory<string>(new[] { dir }),
                    CancellationToken.None);
            }
            finally
            {
                Console.SetOut(priorOut);
            }

            Assert.Equal(0, exit);
            Assert.Contains("0 ok, 0 invalid", outW.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Run_NotADirectory_ReturnsTwo()
    {
        var errW = new StringWriter();
        var priorErr = Console.Error;
        Console.SetError(errW);
        int exit;
        try
        {
            exit = await ListCommand.RunAsync(
                new ReadOnlyMemory<string>(new[] { "/tmp/does-not-exist-" + Guid.NewGuid().ToString("N") }),
                CancellationToken.None);
        }
        finally
        {
            Console.SetError(priorErr);
        }
        Assert.Equal(2, exit);
        Assert.Contains("not a directory", errW.ToString());
    }

    [Fact]
    public async Task Run_RecursiveWalk_FindsNestedFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"list-nested-{Guid.NewGuid():N}");
        var sub = Path.Combine(dir, "sub");
        Directory.CreateDirectory(sub);
        try
        {
            File.WriteAllText(Path.Combine(sub, "nested.test.json"),
                "{\"name\":\"nested\",\"steps\":[]}");

            var outW = new StringWriter();
            var priorOut = Console.Out;
            Console.SetOut(outW);
            int exit;
            try
            {
                exit = await ListCommand.RunAsync(
                    new ReadOnlyMemory<string>(new[] { dir }),
                    CancellationToken.None);
            }
            finally
            {
                Console.SetOut(priorOut);
            }

            Assert.Equal(0, exit);
            Assert.Contains("nested", outW.ToString());
            Assert.Contains("1 ok, 0 invalid", outW.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
