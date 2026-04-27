using System;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Commands;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class FixtureCommandTests
{
    [Fact]
    public async Task Run_NoSubcommand_ReturnsHelpExitCode()
    {
        // No subcommand → print usage, exit 64 (same as Unknown at Program level).
        var code = await FixtureCommand.RunAsync(Array.Empty<string>().AsMemory(), CancellationToken.None);
        Assert.Equal(64, code);
    }

    [Fact]
    public async Task Create_MissingFromFlag_ReturnsTwo()
    {
        var code = await FixtureCommand.RunAsync(new[] { "create", "myfix" }.AsMemory(), CancellationToken.None);
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task Create_MissingNameArg_ReturnsTwo()
    {
        var code = await FixtureCommand.RunAsync(new[] { "create" }.AsMemory(), CancellationToken.None);
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task Create_ScriptFileMissing_ReturnsTwo()
    {
        var code = await FixtureCommand.RunAsync(
            new[] { "create", "myfix", "--from", "/tmp/does-not-exist.fixture.json" }.AsMemory(),
            CancellationToken.None);
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task List_NoFixtures_ReturnsZero()
    {
        // Runs against the repo's tests/fixtures/ — if empty or missing, exit 0 silently.
        var code = await FixtureCommand.RunAsync(new[] { "list" }.AsMemory(), CancellationToken.None);
        Assert.Equal(0, code);
    }

    [Fact]
    public async Task UnknownSubcommand_ReturnsHelpExitCode()
    {
        var code = await FixtureCommand.RunAsync(new[] { "bogus" }.AsMemory(), CancellationToken.None);
        Assert.Equal(64, code);
    }
}
