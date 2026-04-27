namespace SdvTestFramework.Runner.Bitmap;

/// <summary>
/// SSIM computation result. <see cref="Score"/> is the mean of <see cref="BlockScores"/>.
/// The grid is row-major: <c>BlockScores[by, bx]</c> where 0 ≤ by &lt; <see cref="BlocksY"/>,
/// 0 ≤ bx &lt; <see cref="BlocksX"/>. Block size is fixed at 8×8 in <see cref="SsimDiff"/>.
/// </summary>
public readonly record struct SsimResult(
    float Score,
    float[,] BlockScores,
    int BlocksX,
    int BlocksY);
