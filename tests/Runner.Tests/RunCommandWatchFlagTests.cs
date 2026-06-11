using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Commands;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

[Collection("Console")]
public class RunCommandWatchFlagTests
{
    [Fact]
    public async Task Run_WatchFlagWithUnknownPath_ReturnsPathNotFound()
    {
        // --watch is a bare boolean flag. With a nonexistent path, the existing path-not-found
        // path fires with exit 2 BEFORE SDV launches. The test asserts that --watch was parsed
        // as a flag (not treated as a positional path arg): the error message should mention
        // the real path, not "--watch".
        var err = new StringWriter();
        var prevErr = Console.Error;
        Console.SetError(err);
        try
        {
            var code = await RunCommand.RunAsync(
                new[] { "--watch", "/tmp/does-not-exist-dir-watch-test" }.AsMemory(),
                CancellationToken.None);
            Assert.Equal(2, code);
            // When --watch is NOT parsed as a flag, it becomes a positional path and the
            // error mentions "--watch". When it IS parsed, the error mentions the real path.
            Assert.DoesNotContain("--watch", err.ToString());
            Assert.Contains("does-not-exist-dir-watch-test", err.ToString());
        }
        finally { Console.SetError(prevErr); }
    }
}
