using System;
using System.Collections.Generic;
using System.IO;

namespace SdvTestFramework.Protocol;

/// <summary>
/// Deploys the harness mod payload to <c>&lt;modsPath&gt;/SdvTestFramework.Harness/</c>
/// so that SMAPI loads our harness on the next launch.
/// <para>
/// Two sources, tried in order:
/// <list type="number">
///   <item>
///     <term>Source-tree cache</term>
///     <description>
///       <c>AppContext.BaseDirectory/harness-payload/</c> — populated by the
///       <c>StageHarnessPayload</c> MSBuild target during <c>dotnet build</c>.
///       Idempotent: skips files already present at the same modification time.
///     </description>
///   </item>
///   <item>
///     <term>Embedded resources</term>
///     <description>
///       Resources embedded under the <c>harness/</c> prefix in any loaded assembly —
///       present when <c>SdvTestFramework.Cli</c> is installed via
///       <c>dotnet tool install</c>. Always overwrites (no timestamps in embedded resources).
///     </description>
///   </item>
/// </list>
/// </para>
/// </summary>
public static class HarnessDeployer
{
    private const string ModFolderName = "SdvTestFramework.Harness";
    private const string HarnessResourcePrefix = "harness/";

    public static void Deploy(string modsPath)
    {
        if (string.IsNullOrEmpty(modsPath))
            throw new ArgumentException("modsPath required", nameof(modsPath));

        var targetDir = Path.Combine(modsPath, ModFolderName);

        // Source 1: source-tree cache (existing dev workflow). Idempotent via mtime.
        var payloadDir = Path.Combine(AppContext.BaseDirectory, "harness-payload");
        if (Directory.Exists(payloadDir) && File.Exists(Path.Combine(payloadDir, "manifest.json")))
        {
            Directory.CreateDirectory(targetDir);
            var payloadFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var src in Directory.EnumerateFiles(payloadDir))
                payloadFiles.Add(Path.GetFileName(src));
            RemoveStalePayloadFiles(targetDir, payloadFiles);
            foreach (var src in Directory.EnumerateFiles(payloadDir))
            {
                var name = Path.GetFileName(src);
                var dst = Path.Combine(targetDir, name);
                if (File.Exists(dst) && File.GetLastWriteTimeUtc(dst) == File.GetLastWriteTimeUtc(src))
                    continue;
                File.Copy(src, dst, overwrite: true);
                File.SetLastWriteTimeUtc(dst, File.GetLastWriteTimeUtc(src));
            }
            return;
        }

        // Source 2: embedded resources, scanned across all loaded assemblies.
        // The harness payload is embedded in SdvTestFramework.Cli (Runner.dll) when
        // installed via 'dotnet tool install'. We scan all assemblies because
        // HarnessDeployer lives in Protocol but the resources are in Cli.
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            string[] names;
            try { names = asm.GetManifestResourceNames(); }
            catch { continue; }

            var harnessResources = Array.FindAll(names,
                n => n.StartsWith(HarnessResourcePrefix, StringComparison.Ordinal));
            if (harnessResources.Length == 0) continue;

            Directory.CreateDirectory(targetDir);
            var payloadFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in harnessResources)
                payloadFiles.Add(name.Substring(HarnessResourcePrefix.Length));
            RemoveStalePayloadFiles(targetDir, payloadFiles);
            foreach (var name in harnessResources)
            {
                using var stream = asm.GetManifestResourceStream(name)
                    ?? throw new InvalidOperationException(
                        $"manifest resource '{name}' returned null stream");
                var fileName = name.Substring(HarnessResourcePrefix.Length);
                var dest = Path.Combine(targetDir, fileName);
                using var fileStream = File.Create(dest);
                stream.CopyTo(fileStream);
            }
            return;
        }

        throw new FileNotFoundException(
            $"No harness payload available. Source-tree cache not found at {payloadDir} " +
            $"and no embedded harness resources found in any loaded assembly. " +
            "Reinstall SdvTestFramework.Cli or rebuild from source.");
    }

    private static void RemoveStalePayloadFiles(string targetDir, ISet<string> payloadFiles)
    {
        foreach (var file in Directory.EnumerateFiles(targetDir))
        {
            if (!payloadFiles.Contains(Path.GetFileName(file)))
                File.Delete(file);
        }
    }
}
