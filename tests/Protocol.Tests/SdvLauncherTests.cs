using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Protocol.Tests;

public class SdvLauncherTests
{
    [Fact]
    public void CreateStartInfo_Headless_WrapsSmapiWithXvfbRun()
    {
        var root = CreateFakeInstall();
        var mods = Path.Combine(Path.GetTempPath(), $"sdv-launcher-mods-{Guid.NewGuid():N}");
        var socket = Path.Combine(Path.GetTempPath(), $"sdv-launcher-{Guid.NewGuid():N}.sock");

        try
        {
            var psi = SdvLauncher.CreateStartInfo(
                socket,
                installPath: root,
                modsPath: mods,
                headless: true);

            Assert.Equal("xvfb-run", psi.FileName);
            Assert.Equal(root, psi.WorkingDirectory);
            Assert.False(psi.UseShellExecute);
            Assert.True(psi.RedirectStandardOutput);
            Assert.True(psi.RedirectStandardError);
            Assert.Equal(socket, psi.Environment["SDV_TEST_SOCKET"]);

            var args = psi.ArgumentList.ToArray();
            Assert.Equal("-a", args[0]);
            Assert.Equal("-s", args[1]);
            Assert.Equal("-screen 0 1280x720x24", args[2]);
            Assert.Equal(Path.Combine(root, "StardewModdingAPI"), args[3]);
            Assert.Contains("--mods-path", args);
            Assert.Contains(mods, args);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CreateStartInfo_Windowed_LaunchesSmapiDirectly()
    {
        var root = CreateFakeInstall();
        var socket = Path.Combine(Path.GetTempPath(), $"sdv-launcher-{Guid.NewGuid():N}.sock");

        try
        {
            var psi = SdvLauncher.CreateStartInfo(
                socket,
                installPath: root,
                modsPath: null,
                headless: false);

            Assert.Equal(Path.Combine(root, "StardewModdingAPI"), psi.FileName);
            Assert.Equal(root, psi.WorkingDirectory);
            Assert.Empty(psi.ArgumentList);
            Assert.Equal(socket, psi.Environment["SDV_TEST_SOCKET"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Terminate_KillsChildProcessTree()
    {
        var psi = new ProcessStartInfo("/bin/sh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("sleep 60 & echo $!; wait");

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("failed to start shell process");
        var childPid = int.Parse(process.StandardOutput.ReadLine()!);

        SdvLauncher.Terminate(process, timeoutMs: 5000);

        Assert.True(process.HasExited);
        Assert.False(IsProcessAlive(childPid));
    }

    private static string CreateFakeInstall()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sdv-launcher-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "StardewModdingAPI"), string.Empty);
        return root;
    }

    private static bool IsProcessAlive(int pid)
    {
        for (var i = 0; i < 20; i++)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.HasExited)
                    return false;
            }
            catch (ArgumentException)
            {
                return false;
            }

            Thread.Sleep(100);
        }

        return true;
    }
}
