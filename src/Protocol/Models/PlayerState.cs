using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Snapshot of the local farmer. Response shape of <c>state.player</c>.</summary>
public sealed class PlayerState
{
    public string Name { get; set; } = string.Empty;
    public int Money { get; set; }
    public int Stamina { get; set; }
    public int MaxStamina { get; set; }
    public int Health { get; set; }
    public string Location { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
    public List<string> MailReceived { get; set; } = new();
    public List<string> MailForTomorrow { get; set; } = new();
    public List<string> EventsSeen { get; set; } = new();
    public List<int> SecretNotesSeen { get; set; } = new();
    public List<PlayerItemSummary> Items { get; set; } = new();
    public bool Swimming { get; set; }
    public bool BathingClothes { get; set; }
    public bool IsBusy { get; set; }
    public bool CanMove { get; set; }
    public List<PlayerBuffSummary> Buffs { get; set; } = new();
}

/// <summary>Minimal inventory item descriptor for a player snapshot.</summary>
public sealed class PlayerItemSummary
{
    public int Slot { get; set; }
    public string Id { get; set; } = string.Empty;
    public string? ItemId { get; set; }
    public string? QualifiedId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Stack { get; set; }
    public int? Category { get; set; }
    public int? Quality { get; set; }
    public string? RuntimeType { get; set; }
}

/// <summary>Compact active-buff descriptor for a player snapshot.</summary>
public sealed class PlayerBuffSummary
{
    public string? Id { get; set; }
    public string? DisplayName { get; set; }
    public string? Source { get; set; }
    public int? MillisecondsDuration { get; set; }
    public int? TotalMillisecondsDuration { get; set; }
    public PlayerBuffEffects Effects { get; set; } = new();
    public string? RuntimeType { get; set; }
}

/// <summary>Known numeric buff effects. Unknown effects are omitted by the projector.</summary>
public sealed class PlayerBuffEffects
{
    public int FarmingLevel { get; set; }
    public int FishingLevel { get; set; }
    public int MiningLevel { get; set; }
    public int ForagingLevel { get; set; }
    public int LuckLevel { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }
    public int Speed { get; set; }
    public int MagnetRadius { get; set; }
}

public sealed class TilePoint
{
    public int X { get; set; }
    public int Y { get; set; }
}

/// <summary>Request shape for <c>player.set_transient_state</c>.</summary>
public sealed class SetTransientStateRequest
{
    public bool? Swimming { get; set; }
    public bool? BathingClothes { get; set; }
}

/// <summary>Response shape for <c>player.set_transient_state</c>.</summary>
public sealed class SetTransientStateResult : MutatorOk
{
    public bool PreviousSwimming { get; set; }
    public bool PreviousBathingClothes { get; set; }
    public bool Swimming { get; set; }
    public bool BathingClothes { get; set; }
}
