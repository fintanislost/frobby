using System;
using System.IO;
using System.Text.RegularExpressions;
using SdvTestFramework.Protocol.Reports;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Reports;

public class RunDirectoryTests
{
    [Fact]
    public void Create_ProducesExpectedSubdirs()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"rundir-{Guid.NewGuid():N}");
        try
        {
            var rd = RunDirectory.Create(tmp);
            Assert.True(Directory.Exists(rd.Root));
            Assert.True(Directory.Exists(rd.ScenariosDir));
            Assert.True(Directory.Exists(rd.AssetsDir));
            var scen = rd.ScenarioDir("my_scenario");
            Assert.True(Directory.Exists(scen));
            Assert.True(Directory.Exists(Path.Combine(scen, "screenshots")));
        }
        finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true); }
    }

    [Fact]
    public void RunId_FormatMatchesRegex()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"rundir-{Guid.NewGuid():N}");
        try
        {
            var rd = RunDirectory.Create(tmp);
            // Format: YYYY-MM-DDTHH-mm-ss-<6 hex chars>.
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}-[a-f0-9]{6}$", rd.RunId);
        }
        finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, recursive: true); }
    }
}
