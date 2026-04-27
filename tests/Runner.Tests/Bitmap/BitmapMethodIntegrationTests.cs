using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

/// <summary>Integration surface for pixel-exact + dHash + tier preset — verified manually via tampered baseline.</summary>
public class BitmapMethodIntegrationTests
{
    [Fact(Skip = "Requires live SDV — author scenarios with each method; verify failures emit the right diff treatment.")]
    public void AllThreeBitmapMethods_WorkAgainstLiveSDV() { }
}
