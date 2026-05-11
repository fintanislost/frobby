using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SdvTestFramework.Protocol.Models;

public class FishingContextRequest
{
    public string? Location { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public string? Season { get; set; }
    public int? TimeOfDay { get; set; }
    public string? Weather { get; set; }
    public double? Luck { get; set; }
    public bool IncludeTileLayers { get; set; } = true;
}

public sealed class FishingTableRequest : FishingContextRequest
{
    public bool IncludeRaw { get; set; }
    public int Limit { get; set; } = 100;
}

public sealed class FishingSampleCatchRequest : FishingContextRequest
{
    public int Attempts { get; set; } = 1;
    public int? Seed { get; set; }
    public int? PlayerFishingLevel { get; set; }
    public string? RodId { get; set; }
    public string? BaitId { get; set; }
    public string? TackleId { get; set; }
    public bool RestoreState { get; set; } = true;
}

public sealed class FishingContextState
{
    public string Location { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public string LocationType { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
    public string Season { get; set; } = string.Empty;
    public int TimeOfDay { get; set; }
    public string Weather { get; set; } = string.Empty;
    public double? DailyLuck { get; set; }
    public bool IsWater { get; set; }
    public bool IsFishable { get; set; }
    public string BlockedReason { get; set; } = string.Empty;
    public string FishAreaId { get; set; } = string.Empty;
    public string MapFish { get; set; } = string.Empty;
    public bool HasNoFishing { get; set; }
    public List<FishingTileLayerProperties> TileProperties { get; set; } = new();
    public List<FishingAreaSummary> LocationFishAreas { get; set; } = new();
}

public sealed class FishingTileLayerProperties
{
    public string Layer { get; set; } = string.Empty;

    [JsonConverter(typeof(VerbatimStringDictionaryJsonConverter))]
    public Dictionary<string, string> Properties { get; set; } = new();
}

public sealed class FishingAreaSummary
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public RectangleSummary? Position { get; set; }
    public List<string> CrabPotFishTypes { get; set; } = new();
}

public sealed class RectangleSummary
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class FishingTableState
{
    public FishingContextState Context { get; set; } = new();
    public List<FishingCatchCandidate> Candidates { get; set; } = new();
    public List<string> RawSources { get; set; } = new();
}

public sealed class FishingCatchCandidate
{
    public string Id { get; set; } = string.Empty;
    public string ItemId { get; set; } = string.Empty;
    public string QualifiedId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string FishAreaId { get; set; } = string.Empty;
    public double? Chance { get; set; }
    public string Condition { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public string TimeRange { get; set; } = string.Empty;
    public string Weather { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Raw { get; set; } = string.Empty;
}

public sealed class FishingSampleCatchResult
{
    public FishingContextState Context { get; set; } = new();
    public int Attempts { get; set; }
    public bool StateRestored { get; set; }
    public List<FishingCatchResult> Results { get; set; } = new();
}

public sealed class FishingCatchResult
{
    public int Attempt { get; set; }
    public string ItemId { get; set; } = string.Empty;
    public string QualifiedId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Stack { get; set; }
    public int? Quality { get; set; }
    public int? Category { get; set; }
    public string RuntimeType { get; set; } = string.Empty;
    public bool IsNull { get; set; }
    public string Source { get; set; } = string.Empty;
    public string RawId { get; set; } = string.Empty;
}
