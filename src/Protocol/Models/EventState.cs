using System.Collections.Generic;

namespace SdvTestFramework.Protocol.Models;

/// <summary>Snapshot of the active Stardew event/cutscene. Response shape of <c>state.event</c>.</summary>
public sealed class EventState
{
    public bool Active { get; set; }
    public bool EventUp { get; set; }
    public string Location { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public bool IsFestival { get; set; }
    public bool IsSkippable { get; set; }
    public bool PlayerControlLocked { get; set; }
    public List<EventActorState> Actors { get; set; } = new();
    public List<MenuChoiceState> Choices { get; set; } = new();
    public EventDialogueState? Dialogue { get; set; }
    public EventViewportState? Viewport { get; set; }
}

public sealed class EventActorState
{
    public string Name { get; set; } = string.Empty;
    public TilePoint Tile { get; set; } = new();
    public PixelPoint Pixel { get; set; } = new();
    public int FacingDirection { get; set; }
    public int CurrentFrame { get; set; }
    public string DialogueKey { get; set; } = string.Empty;
    public string DialogueText { get; set; } = string.Empty;
    public int DialogueCount { get; set; }
}

public sealed class EventDialogueState
{
    public string MenuType { get; set; } = string.Empty;
    public string Speaker { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public List<MenuChoiceState> Choices { get; set; } = new();
}

public sealed class EventViewportState
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class PixelPoint
{
    public int X { get; set; }
    public int Y { get; set; }
}
