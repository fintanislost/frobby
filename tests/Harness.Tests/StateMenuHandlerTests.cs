using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol.Models;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class StateMenuHandlerTests
{
    [Fact]
    public void AddCurrentPanelExtras_ReflectsCustomMenuPanelState()
    {
        var state = new MenuState();

        StateMenuHandler.AddCurrentPanelExtras(state, new FakeTerminalMenu(new FakeChartPanel()));

        Assert.Equal("FakeChartPanel", state.Extra["current_panel_type"]);
        Assert.Equal("G", state.Extra["current_panel_hotkey"]);
        Assert.Equal("CHART", state.Extra["current_panel_title"]);
        Assert.Equal("OneMonth", state.Extra["current_panel_timeframe"]);
    }

    [Fact]
    public void AddReadableTextExtras_AddsDialogueTextFromFakeMenu()
    {
        var state = new MenuState { Type = "DialogueBox", Present = true };

        StateMenuHandler.AddReadableTextExtras(state, new FakeDialogueMenu());

        Assert.Equal("Camilla", state.Extra["character"]);
        Assert.Equal("Welcome to the grove.", state.Extra["dialogue_text"]);
    }

    [Fact]
    public void TryProjectDialogue_ReturnsNullWhenMenuIsNull()
    {
        Assert.Null(StateMenuHandler.TryProjectDialogue(null));
    }

    [Fact]
    public void TryProjectDialogue_ProjectsReadableDialogue()
    {
        var projected = StateMenuHandler.TryProjectDialogue(new FakeDialogueMenu());

        Assert.NotNull(projected);
        Assert.Equal("FakeDialogueMenu", projected!.MenuType);
        Assert.Equal("Camilla", projected.Speaker);
        Assert.Equal("Welcome to the grove.", projected.Text);
    }

    private sealed class FakeTerminalMenu
    {
        private readonly object _currentPanel;

        public FakeTerminalMenu(object currentPanel)
        {
            _currentPanel = currentPanel;
        }
    }

    private sealed class FakeChartPanel
    {
        public string Hotkey => "G";
        public string Title => "CHART";
        internal FakeTimeframe Timeframe => FakeTimeframe.OneMonth;
    }

    private enum FakeTimeframe
    {
        OneDay,
        FiveDay,
        OneMonth,
    }

    private sealed class FakeDialogueMenu
    {
        public object characterDialogue = new FakeCharacterDialogue();
        public string dialogue = "Welcome to the grove.";
    }

    private sealed class FakeCharacterDialogue
    {
        public FakeSpeaker speaker = new();
    }

    private sealed class FakeSpeaker
    {
        public string Name = "Camilla";
    }
}
