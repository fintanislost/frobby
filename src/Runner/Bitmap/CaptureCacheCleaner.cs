using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SdvTestFramework.Runner.Bitmap;

/// <summary>
/// Sweep the bitmap-capture cache directory. A file is kept iff BOTH conditions hold:
/// (1) its mtime is within <c>maxAgeDays</c>, AND (2) its containing scenario subdir is
/// among the <c>keepRuns</c> most-recently-modified subdirs of the cache root.
/// Either condition failing → delete.
/// </summary>
public static class CaptureCacheCleaner
{
    /// <summary>
    /// Sweep <paramref name="cacheDir"/>. Returns the count of files deleted (or would-be
    /// deleted in dry-run). Returns 0 if the dir doesn't exist.
    /// </summary>
    public static int CleanCache(string cacheDir, int maxAgeDays, int keepRuns, bool dryRun)
    {
        if (!Directory.Exists(cacheDir)) return 0;

        // Identify the keepRuns most recent scenario subdirs by mtime.
        var subdirs = Directory.EnumerateDirectories(cacheDir).ToList();
        var keepSet = new HashSet<string>(
            subdirs.OrderByDescending(d => Directory.GetLastWriteTimeUtc(d)).Take(keepRuns),
            StringComparer.Ordinal);

        var ageCutoff = DateTime.UtcNow.AddDays(-maxAgeDays);

        int count = 0;
        foreach (var dir in subdirs)
        {
            bool dirIsKept = keepSet.Contains(dir);
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var fi = new FileInfo(f);
                bool tooOld = fi.LastWriteTimeUtc < ageCutoff;
                if (dirIsKept && !tooOld) continue;
                if (!dryRun)
                {
                    try { File.Delete(f); }
                    catch { continue; }
                }
                count++;
            }
        }
        return count;
    }
}
