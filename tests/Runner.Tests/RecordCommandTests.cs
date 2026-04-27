using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Commands;
using Xunit;

namespace SdvTestFramework.Runner.Tests;

public class RecordCommandTests
{
    [Fact]
    public async Task MissingName_ReturnsTwo()
    {
        var code = await RecordCommand.RunAsync(Array.Empty<string>().AsMemory(), CancellationToken.None);
        Assert.Equal(2, code);
    }

    [Fact]
    public async Task ExistingOutputWithoutForce_ReturnsThree()
    {
        // Pre-create a target file; RecordCommand should refuse without --force.
        var outputPath = Path.Combine(Path.GetTempPath(), $"rec-collide-{System.Guid.NewGuid():N}.test.json");
        File.WriteAllText(outputPath, "{\"name\":\"old\"}");
        try
        {
            var code = await RecordCommand.RunAsync(
                new[] { "my_trace", "--output", outputPath }.AsMemory(),
                CancellationToken.None);
            Assert.Equal(3, code);
        }
        finally { if (File.Exists(outputPath)) File.Delete(outputPath); }
    }
}
