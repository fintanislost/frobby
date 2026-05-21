using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class CombatAttackHandlerTests
{
    [Fact]
    public void Handle_MissingDirectionAndTarget_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => CombatAttackHandler.Handle(p, new FakeCombatWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("direction or target tile", ex.Message);
    }

    [Fact]
    public void Handle_PartialTargetTile_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"x\":20}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => CombatAttackHandler.Handle(p, new FakeCombatWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("both x and y", ex.Message);
    }

    [Fact]
    public void Handle_UnknownDirection_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"direction\":\"northish\"}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => CombatAttackHandler.Handle(p, new FakeCombatWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("unknown direction", ex.Message);
    }

    [Fact]
    public void Handle_NotWorldReady_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"direction\":\"up\"}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            CombatAttackHandler.Handle(p, new FakeCombatWorld { IsWorldReady = false }));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_TargetTileAbovePlayerFacesUpSelectsRequestedWeaponAndAttacksOnce()
    {
        var world = new FakeCombatWorld { TileX = 20, TileY = 145 };
        var p = JsonDocument.Parse("{\"x\":20,\"y\":144,\"qualified_item_id\":\"(W)4\"}").RootElement;

        var result = CombatAttackHandler.Handle(p, world);
        var json = result.GetRawText();

        Assert.Equal("up", world.FacedDirection);
        Assert.Equal(1, world.AttackCount);
        Assert.Equal("(W)4", world.SelectedQualifiedItemId);
        Assert.Contains("\"direction\":\"up\"", json);
        Assert.Contains("\"selected_item_qualified_id\":\"(W)4\"", json);
    }

    [Fact]
    public void Handle_TargetTileOverlappingPlayerUsesCurrentFacingAndAttacksOnce()
    {
        var world = new FakeCombatWorld { TileX = 20, TileY = 145, FacingDirection = 0 };
        var p = JsonDocument.Parse("{\"x\":20,\"y\":145,\"qualified_item_id\":\"(W)4\"}").RootElement;

        var result = CombatAttackHandler.Handle(p, world);
        var json = result.GetRawText();

        Assert.Equal("up", world.FacedDirection);
        Assert.Equal(1, world.AttackCount);
        Assert.Equal("(W)4", world.SelectedQualifiedItemId);
        Assert.Contains("\"direction\":\"up\"", json);
    }

    [Fact]
    public void Handle_RepeatGreaterThanOne_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"direction\":\"left\",\"repeat\":3}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => CombatAttackHandler.Handle(p, new FakeCombatWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("repeat is runner-only", ex.Message);
    }

    [Fact]
    public void Handle_DelayTicksGreaterThanZero_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"direction\":\"left\",\"delay_ticks\":1}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => CombatAttackHandler.Handle(p, new FakeCombatWorld()));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("delay_ticks is runner-only", ex.Message);
    }

    private sealed class FakeCombatWorld : ICombatAttackWorld
    {
        public bool IsWorldReady { get; set; } = true;
        public int Tick { get; set; } = 456;
        public int TileX { get; set; } = 20;
        public int TileY { get; set; } = 145;
        public int FacingDirection { get; set; } = 2;
        public string? SelectedQualifiedItemId { get; private set; }
        public string? FacedDirection { get; private set; }
        public int AttackCount { get; private set; }

        public CombatAttackSelectedItem SelectWeapon(string? qualifiedItemId)
        {
            SelectedQualifiedItemId = qualifiedItemId ?? "(W)4";
            return new CombatAttackSelectedItem("4", SelectedQualifiedItemId, "Galaxy Sword", "MeleeWeapon");
        }

        public void FaceDirection(string direction) => FacedDirection = direction;

        public void AttackOnce() => AttackCount++;
    }
}
