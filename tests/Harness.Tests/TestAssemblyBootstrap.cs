using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SdvTestFramework.Harness.Tests;

/// <summary>
/// Harness.Tests references Harness, which references MonoGame / StardewValley / SMAPI DLLs
/// that only exist on disk inside the SDV install directory. ModBuildConfig marks those
/// references <c>Private=false</c> (right for a SMAPI mod, wrong for a standalone test runner).
/// Rather than copying binaries we redirect at runtime.
/// </summary>
internal static class TestAssemblyBootstrap
{
    [ModuleInitializer]
    internal static void Init()
    {
        var gameDir = Environment.GetEnvironmentVariable("SDV_INSTALL_PATH")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Stardew Valley");

        if (!Directory.Exists(gameDir))
        {
            // Let the first test fail loudly with the original AssemblyLoadException.
            // We'd rather a clear stack trace than a silent hook that does nothing.
            return;
        }

        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
        {
            var name = new AssemblyName(args.Name).Name;
            if (name is null) return null;
            string[] candidates =
            {
                Path.Combine(gameDir, name + ".dll"),
                Path.Combine(gameDir, "smapi-internal", name + ".dll"),
            };
            foreach (var path in candidates)
            {
                if (File.Exists(path))
                    return Assembly.LoadFrom(path);
            }
            return null;
        };
    }
}
