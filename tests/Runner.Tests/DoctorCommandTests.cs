using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Commands;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

/// <summary>
/// Tests the `doctor` command's environment-readiness checks. Happy-path relies on
/// the dev workstation having a valid SDV install (Flatpak Steam path); forced-failure
/// overrides <c>SDV_INSTALL_PATH</c> to a non-existent directory.
/// </summary>
[Collection("Console")]
public class DoctorCommandTests
{
    [Fact]
    public async Task Run_OnThisMachine_ReturnsZero()
    {
        // The dev workstation has a valid SDV install (per memory sdv_install_path.md).
        // If this test runs on a machine without one, it'll fail loudly — which is the
        // correct signal for a doctor-style command.
        var outW = new StringWriter();
        var priorOut = Console.Out;
        Console.SetOut(outW);
        try
        {
            int exit = await DoctorCommand.RunAsync(ReadOnlyMemory<string>.Empty, CancellationToken.None);
            Assert.Equal(0, exit);
        }
        finally
        {
            Console.SetOut(priorOut);
        }

        var output = outW.ToString();
        Assert.Contains("all checks passed", output);
    }

    [Fact]
    public async Task Run_WithInvalidInstallPath_FailsAndReturnsOne()
    {
        // Override SDV_INSTALL_PATH to a non-existent directory for this test only.
        var prior = Environment.GetEnvironmentVariable("SDV_INSTALL_PATH");
        Environment.SetEnvironmentVariable("SDV_INSTALL_PATH",
            Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}"));

        var outW = new StringWriter();
        var priorOut = Console.Out;
        Console.SetOut(outW);
        try
        {
            int exit = await DoctorCommand.RunAsync(ReadOnlyMemory<string>.Empty, CancellationToken.None);
            Assert.Equal(1, exit);
        }
        finally
        {
            Console.SetOut(priorOut);
            Environment.SetEnvironmentVariable("SDV_INSTALL_PATH", prior);
        }

        Assert.Contains("FAIL", outW.ToString());
    }
}
