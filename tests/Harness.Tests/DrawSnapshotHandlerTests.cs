using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Recording;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

[Collection("Recorder")]
public class DrawSnapshotHandlerTests
{
    [Fact]
    public void Handle_EmptyBuffer_ReturnsEmptyEvents()
    {
        // No IMonitor needed — Recorder.SnapshotEvents doesn't log.
        Recorder.Initialize(null, capacity: 16);
        // Discard any residual state from other tests.
        Recorder.Disarm();

        var result = DrawSnapshotHandler.Handle(null);
        var text = result.GetRawText();
        Assert.Contains("\"events\":[]", text);
        Assert.Contains("\"dropped\":0", text);
    }

    [Fact]
    public void ToDto_MapsAllFields()
    {
        var e = new DrawEvent
        {
            Tick = 7, CallIndex = 2, TextureRefId = 99,
            TextureWidth = 32, TextureHeight = 16,
            SourceRect = new Rectangle(1, 2, 3, 4),
            DestRect = new Rectangle(10, 20, 30, 40),
            Color = new Color(100, 150, 200, 250),
            Rotation = 1.5f,
            Origin = new Vector2(8, 4),
            Effects = SpriteEffects.FlipHorizontally,
            LayerDepth = 0.75f,
        };
        var dto = DrawSnapshotHandler.ToDto(in e);
        Assert.Equal(7, dto.Tick);
        Assert.Equal(2, dto.Call);
        Assert.Equal(99, dto.TexRef);
        Assert.Equal(new[] { 1, 2, 3, 4 }, dto.Src);
        Assert.Equal(new[] { 10, 20, 30, 40 }, dto.Dst);
        Assert.Equal(new[] { 100, 150, 200, 250 }, dto.Col);
        Assert.Equal(1.5f, dto.Rot);
        Assert.Equal(new[] { 8f, 4f }, dto.Orig);
        Assert.Equal((int)SpriteEffects.FlipHorizontally, dto.Fx);
        Assert.Equal(0.75f, dto.Z);
    }

    [Fact]
    public void ToDto_NullSourceRect_PreservesNull()
    {
        var e = new DrawEvent { SourceRect = null, DestRect = new Rectangle(0, 0, 1, 1), Color = Color.White };
        var dto = DrawSnapshotHandler.ToDto(in e);
        Assert.Null(dto.Src);
    }

    // Note: no true red-phase test for T5's positive path — Texture2D can't be constructed
    // without a GraphicsDevice, and ToDto uses the Texture2D-typed TryResolve overload so
    // the object-keyed shim API can't stand in. The two null-path tests below are regression
    // guards (they passed before T5 because TextureAsset defaulted to null; they continue to
    // pass after T5 because both paths resolve to null). The positive case is covered by the
    // T8 smoke test and the skip-marked ContentLoadPatches integration test.

    [Fact]
    public void ToDto_NullTexture_ResolvesToNull()
    {
        // Event has no Texture reference (e.g. constructed in a test).
        // Even with a populated registry, there's nothing to look up → TextureAsset is null.
        var registry = new SdvTestFramework.Harness.Assets.TextureAssetRegistry();
        var prior = SdvTestFramework.Harness.Assets.TextureAssetRegistry.Shared;
        SdvTestFramework.Harness.Assets.TextureAssetRegistry.Shared = registry;
        try
        {
            var e = new DrawEvent { Texture = null, DestRect = new Rectangle(0, 0, 1, 1), Color = Color.White };
            var dto = DrawSnapshotHandler.ToDto(in e);
            Assert.Null(dto.TextureAsset);
        }
        finally
        {
            SdvTestFramework.Harness.Assets.TextureAssetRegistry.Shared = prior;
        }
    }

    [Fact]
    public void ToDto_NoSharedRegistry_ResolvesToNull()
    {
        // Defensive: when Shared is null (early startup, or harness in test mode without
        // ModEntry.Entry running), ToDto should not NRE — just return null.
        var prior = SdvTestFramework.Harness.Assets.TextureAssetRegistry.Shared;
        SdvTestFramework.Harness.Assets.TextureAssetRegistry.Shared = null;
        try
        {
            var e = new DrawEvent { Texture = null, DestRect = new Rectangle(0, 0, 1, 1), Color = Color.White };
            var dto = DrawSnapshotHandler.ToDto(in e);
            Assert.Null(dto.TextureAsset);
        }
        finally
        {
            SdvTestFramework.Harness.Assets.TextureAssetRegistry.Shared = prior;
        }
    }
}
