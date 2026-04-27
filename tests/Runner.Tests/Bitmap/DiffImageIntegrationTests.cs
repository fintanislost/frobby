using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

/// <summary>Integration surface for diff-image-on-failure — verified manually via tampered baseline.</summary>
public class DiffImageIntegrationTests
{
    [Fact(Skip = "Requires live SDV — tamper a baseline + run-samples.sh; verify forensics PNGs in test-results/<run-id>/scenarios/.../diffs/.")]
    public void DiffPngs_GeneratedOnBitmapFailure() { }
}
