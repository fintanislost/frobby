using System;
using System.IO;

namespace SdvTestFramework.Repository.Tests;

internal static class RepositoryTestPaths
{
    public static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "sdv-test-framework.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Frobby repository root.");
    }
}
