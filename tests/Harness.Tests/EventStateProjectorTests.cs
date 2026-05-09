using System.Collections.Generic;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Handlers;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class EventStateProjectorTests
{
    private sealed class FakeEvent
    {
        public string id = "520702";
        public bool skippable = true;
        public bool isFestival = false;
        public List<object> actors = new()
        {
            new FakeActor("Krobus", 16, 23, 1024, 1472, 3, 0),
        };
    }

    private sealed class FakeActor
    {
        public FakeActor(string name, int tileX, int tileY, int pixelX, int pixelY, int facing, int frame)
        {
            Name = name;
            TilePoint = new Point(tileX, tileY);
            Position = new Vector2(pixelX, pixelY);
            FacingDirection = facing;
            Sprite = new FakeSprite { CurrentFrame = frame };
        }

        public string Name { get; }
        public Point TilePoint { get; }
        public Vector2 Position { get; }
        public int FacingDirection { get; }
        public FakeSprite Sprite { get; }
    }

    private sealed class FakeSprite
    {
        public int CurrentFrame { get; set; }
    }

    [Fact]
    public void ToState_Inactive_ReturnsEmptyState()
    {
        var state = EventStateProjector.ToState(new EventProjectionSource
        {
            EventUp = false,
            LocationName = "",
            Viewport = new Rectangle(0, 0, 1280, 720),
        });

        Assert.False(state.Active);
        Assert.False(state.EventUp);
        Assert.Equal("", state.Location);
        Assert.Equal("", state.Id);
        Assert.Empty(state.Actors);
        Assert.Null(state.Dialogue);
        Assert.Null(state.Viewport);
    }

    [Fact]
    public void ToState_ActiveEvent_ProjectsIdActorsFlagsAndViewport()
    {
        var state = EventStateProjector.ToState(new EventProjectionSource
        {
            CurrentEvent = new FakeEvent(),
            EventUp = true,
            LocationName = "BusStop",
            Viewport = new Rectangle(896, 1472, 1280, 720),
        });

        Assert.True(state.Active);
        Assert.True(state.EventUp);
        Assert.Equal("BusStop", state.Location);
        Assert.Equal("520702", state.Id);
        Assert.False(state.IsFestival);
        Assert.True(state.IsSkippable);
        Assert.True(state.PlayerControlLocked);
        Assert.Equal(896, state.Viewport?.X);
        Assert.Equal(1280, state.Viewport?.Width);

        var actor = Assert.Single(state.Actors);
        Assert.Equal("Krobus", actor.Name);
        Assert.Equal(16, actor.Tile.X);
        Assert.Equal(23, actor.Tile.Y);
        Assert.Equal(1024, actor.Pixel.X);
        Assert.Equal(1472, actor.Pixel.Y);
        Assert.Equal(3, actor.FacingDirection);
        Assert.Equal(0, actor.CurrentFrame);
    }

    [Fact]
    public void ToState_ActiveEvent_ProjectsDialogueChoices()
    {
        var state = EventStateProjector.ToState(new EventProjectionSource
        {
            CurrentEvent = new FakeEvent(),
            EventUp = true,
            LocationName = "Town",
            Viewport = new Rectangle(0, 0, 1280, 720),
            ActiveMenu = new FakeQuestionMenu(),
        });

        Assert.NotNull(state.Dialogue);
        Assert.Equal("What should I do?", state.Dialogue!.Text);
        Assert.Equal(2, state.Choices.Count);
        Assert.Equal("pet", state.Choices[0].Key);
        Assert.Equal("Pet Dusty", state.Choices[0].Text);
    }

    [Fact]
    public void ToState_ActiveEvent_ProjectsChoicesWhenDialogueTextIsBlank()
    {
        var state = EventStateProjector.ToState(new EventProjectionSource
        {
            CurrentEvent = new FakeEvent(),
            EventUp = true,
            LocationName = "Town",
            Viewport = new Rectangle(0, 0, 1280, 720),
            ActiveMenu = new FakeBlankQuestionMenu(),
        });

        Assert.NotNull(state.Dialogue);
        Assert.Equal("", state.Dialogue!.Text);
        Assert.Equal(2, state.Choices.Count);
        Assert.Equal("0", state.Choices[0].Key);
        Assert.Equal("Pet Dusty", state.Choices[0].Text);
    }

    private sealed class FakeQuestionMenu
    {
        public string question = "What should I do?";
        public FakeResponse[] responses =
        {
            new("pet", "Pet Dusty"),
            new("leave", "Don't pet Dusty"),
        };
    }

    private sealed class FakeBlankQuestionMenu
    {
        public FakeResponse[] responses =
        {
            new("0", "Pet Dusty"),
            new("1", "Don't pet Dusty"),
        };
    }

    private sealed class FakeResponse
    {
        public FakeResponse(string key, string text)
        {
            responseKey = key;
            responseText = text;
        }

        public string responseKey;
        public string responseText;
    }
}
