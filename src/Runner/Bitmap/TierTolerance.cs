using System;
using SdvTestFramework.Protocol.Reports;

namespace SdvTestFramework.Runner.Bitmap;

/// <summary>
/// Resolve the effective tolerance for a bitmap assertion. Per-assertion explicit
/// tolerance always wins; otherwise looks up the per-(tier, method) default from the
/// 3×3 table (generic / ci-ubuntu / self-hosted-nvidia × ssim / pixel-exact / dhash)
/// per spec §4.3.
/// </summary>
public static class TierTolerance
{
    public static double Resolve(string tier, BitmapMethod method, double? perAssertionTolerance)
    {
        if (perAssertionTolerance is { } t)
        {
            // Per-method validation matches spec §4.2.
            switch (method)
            {
                case BitmapMethod.Ssim:
                    if (t <= 0 || t > 1)
                        throw new ArgumentException($"bitmap ssim 'tolerance' must be in (0, 1]; got {t}");
                    break;
                case BitmapMethod.PixelExact:
                    if (t < 0)
                        throw new ArgumentException($"bitmap pixel-exact 'tolerance' must be >= 0; got {t}");
                    break;
                case BitmapMethod.DHash:
                    if (t < 0 || t > 64)
                        throw new ArgumentException($"bitmap dhash 'tolerance' must be in [0, 64]; got {t}");
                    break;
            }
            return t;
        }

        // Tier-derived defaults per method (spec §4.3 table). The CLI flag validates
        // tier names upfront; the unknown-tier branch here is a defense-in-depth guard
        // for callers that bypass the CLI (e.g. per-assertion overrides via JSON).
        return (tier, method) switch
        {
            ("generic",            BitmapMethod.Ssim)        => 0.95,
            ("generic",            BitmapMethod.PixelExact)  => 5,
            ("generic",            BitmapMethod.DHash)       => 5,
            ("ci-ubuntu",          BitmapMethod.Ssim)        => 0.98,
            ("ci-ubuntu",          BitmapMethod.PixelExact)  => 2,
            ("ci-ubuntu",          BitmapMethod.DHash)       => 3,
            ("self-hosted-nvidia", BitmapMethod.Ssim)        => 0.999,
            ("self-hosted-nvidia", BitmapMethod.PixelExact)  => 0,
            ("self-hosted-nvidia", BitmapMethod.DHash)       => 1,
            _ => throw new ArgumentException(
                $"unknown tier '{tier}' (expected: generic | ci-ubuntu | self-hosted-nvidia)"),
        };
    }
}
