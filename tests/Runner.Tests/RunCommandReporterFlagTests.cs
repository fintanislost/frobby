using System;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Commands;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class RunCommandReporterFlagTests
{
    [Fact]
    public async Task Run_UnknownReporter_ReturnsTwo()
    {
        // Invalid reporter name → argument error → exit 2 before any scenario loading.
        var code = await RunCommand.RunAsync(
            new[] { "--reporter", "bogus", "/tmp/does-not-exist-dir" }.AsMemory(),
            CancellationToken.None);
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task Run_ReporterFlagAfterPathArgs_StillParsed()
    {
        // Flag can come anywhere in argv. Passing a nonexistent path forces exit 2 at
        // scenario-load time; the test just asserts no argument-parse crash.
        var code = await RunCommand.RunAsync(
            new[] { "/tmp/does-not-exist-dir", "--reporter", "tap" }.AsMemory(),
            CancellationToken.None);
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task Run_OutputPathUnwritable_ReturnsThree()
    {
        // Point at a path whose parent directory doesn't exist: StreamWriter throws DirectoryNotFound.
        // We also need to dodge the scenario-loading check, so point at a valid empty dir.
        // The output-path validation runs eagerly BEFORE scenario discovery.
        var emptyDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"empty-{System.Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(emptyDir);
        try
        {
            var code = await RunCommand.RunAsync(
                new[] { "--reporter", "tap", "--output", "/no/such/dir/out.tap", emptyDir }.AsMemory(),
                CancellationToken.None);
            Assert.Equal(3, code);
        }
        finally { System.IO.Directory.Delete(emptyDir, recursive: true); }
    }
}
