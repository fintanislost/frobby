using System;
using System.Diagnostics;
using System.IO;

namespace SdvTestFramework.Protocol;

/// <summary>
/// Launches SMAPI as a subprocess with the test-harness socket env var set. The caller is
/// responsible for killing the returned <see cref="Process"/> (typically via a finally block
/// in the driving command).
/// </summary>
public static class SdvLauncher
{
    /// <summary>
    /// Start a SMAPI subprocess configured for test-mode operation.
    /// </summary>
    /// <param name="socketPath">Path the harness mod will open as its RPC listener; propagated via <c>SDV_TEST_SOCKET</c>.</param>
    /// <param name="installPath">SDV install directory; falls back to <c>SDV_INSTALL_PATH</c> then the Flatpak-Steam default.</param>
    /// <param name="modsPath">Optional <c>--mods-path</c> override forwarded to SMAPI.</param>
    public static Process Launch(string socketPath, string? installPath = null, string? modsPath = null)
    {
        installPath ??= Environment.GetEnvironmentVariable("SDV_INSTALL_PATH")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley");

        var smapi = Path.Combine(installPath, "StardewModdingAPI");
        if (!File.Exists(smapi))
            throw new FileNotFoundException($"SMAPI binary not found at {smapi}");

        var psi = new ProcessStartInfo(smapi)
        {
            UseShellExecute = false,
            WorkingDirectory = installPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment["SDV_TEST_SOCKET"] = socketPath;
        if (!string.IsNullOrEmpty(modsPath))
        {
            psi.ArgumentList.Add("--mods-path");
            psi.ArgumentList.Add(modsPath);
        }

        return Process.Start(psi)
            ?? throw new InvalidOperationException("failed to start SMAPI process");
    }
}
