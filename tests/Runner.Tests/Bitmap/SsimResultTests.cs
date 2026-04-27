using SdvTestFramework.Runner.Bitmap;
using Xunit;

namespace SdvTestFramework.Runner.Tests.Bitmap;

public class SsimResultTests
{
    [Fact]
    public void Score_EqualsAverageOfBlockScores()
    {
        // 2×2 block grid = 4 blocks. Avg of 0.8, 0.9, 1.0, 1.0 = 0.925.
        var grid = new float[2, 2] { { 0.8f, 0.9f }, { 1.0f, 1.0f } };
        var result = new SsimResult(0.925f, grid, BlocksX: 2, BlocksY: 2);
        Assert.Equal(0.925f, result.Score, precision: 4);
        Assert.Equal(2, result.BlocksX);
        Assert.Equal(2, result.BlocksY);
        Assert.Equal(0.8f, result.BlockScores[0, 0]);
        Assert.Equal(1.0f, result.BlockScores[1, 1]);
    }
}
