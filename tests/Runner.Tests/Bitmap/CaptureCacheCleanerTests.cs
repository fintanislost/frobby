using System;
using System.IO;
using System.Linq;
using System.Threading;
using SdvTestFramework.Runner.Bitmap;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

public class CaptureCacheCleanerTests
{
    private static void Touch(string path, DateTime mtime)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47 });   // PNG magic
        File.SetLastWriteTimeUtc(path, mtime);
    }

    [Fact]
    public void MaxAgeZero_DeletesAllFiles()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ccc-{Guid.NewGuid():N}");
        try
        {
            Touch(Path.Combine(tmp, "a", "1.png"), DateTime.UtcNow);
            Touch(Path.Combine(tmp, "a", "2.png"), DateTime.UtcNow);
            Touch(Path.Combine(tmp, "b", "1.png"), DateTime.UtcNow);

            int deleted = CaptureCacheCleaner.CleanCache(tmp, maxAgeDays: 0, keepRuns: 0, dryRun: false);
            Assert.Equal(3, deleted);
            Assert.False(File.Exists(Path.Combine(tmp, "a", "1.png")));
            Assert.False(File.Exists(Path.Combine(tmp, "b", "1.png")));
        }
        finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public void KeepRuns_RetainsNMostRecentScenarioDirs()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ccc-{Guid.NewGuid():N}");
        try
        {
            // 3 scenario dirs with different mtimes; keep top 2 by recency.
            var older = DateTime.UtcNow.AddDays(-1);
            var newer = DateTime.UtcNow.AddHours(-1);
            var newest = DateTime.UtcNow;
            Touch(Path.Combine(tmp, "old", "x.png"), older);
            Touch(Path.Combine(tmp, "mid", "x.png"), newer);
            Touch(Path.Combine(tmp, "new", "x.png"), newest);
            // Match the parent dir mtime to the contained file's.
            Directory.SetLastWriteTimeUtc(Path.Combine(tmp, "old"), older);
            Directory.SetLastWriteTimeUtc(Path.Combine(tmp, "mid"), newer);
            Directory.SetLastWriteTimeUtc(Path.Combine(tmp, "new"), newest);

            int deleted = CaptureCacheCleaner.CleanCache(tmp, maxAgeDays: 365, keepRuns: 2, dryRun: false);
            Assert.Equal(1, deleted);   // only "old" got swept
            Assert.False(File.Exists(Path.Combine(tmp, "old", "x.png")));
            Assert.True(File.Exists(Path.Combine(tmp, "mid", "x.png")));
            Assert.True(File.Exists(Path.Combine(tmp, "new", "x.png")));
        }
        finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public void DryRun_ReportsButDoesntDelete()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ccc-{Guid.NewGuid():N}");
        try
        {
            Touch(Path.Combine(tmp, "a", "1.png"), DateTime.UtcNow);
            int wouldDelete = CaptureCacheCleaner.CleanCache(tmp, maxAgeDays: 0, keepRuns: 0, dryRun: true);
            Assert.Equal(1, wouldDelete);
            Assert.True(File.Exists(Path.Combine(tmp, "a", "1.png")), "dry-run must not touch files");
        }
        finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true); }
    }
}
