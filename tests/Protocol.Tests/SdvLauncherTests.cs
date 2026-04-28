using System;
using System.IO;
using System.Linq;
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

    private static string CreateFakeInstall()
    {
        var root = Path.Combine(Path.GetTempPath(), $"sdv-launcher-install-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "StardewModdingAPI"), string.Empty);
        return root;
    }
}
