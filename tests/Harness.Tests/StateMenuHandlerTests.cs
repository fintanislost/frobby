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
    public void AddReadableTextExtras_AddsDialogueChoiceSummaries()
    {
        var state = new MenuState { Type = "DialogueBox", Present = true };

        StateMenuHandler.AddReadableTextExtras(state, new FakeQuestionMenu());

        Assert.Equal(2, state.Choices.Count);
        Assert.Equal("pet", state.Choices[0].Key);
        Assert.Equal("Pet Dusty", state.Choices[0].Text);
        Assert.Equal("leave", state.Choices[1].Key);
        Assert.Equal("Don't pet Dusty", state.Choices[1].Text);
        Assert.Equal("2", state.Extra["choice_count"]);
    }

    [Fact]
    public void AddReadableTextExtras_ReadsQuestionChoicesMember()
    {
        var state = new MenuState { Type = "DialogueBox", Present = true };

        StateMenuHandler.AddReadableTextExtras(state, new FakeQuestionChoicesMenu());

        Assert.Equal(2, state.Choices.Count);
        Assert.Equal("Pet Dusty", state.Choices[0].Text);
        Assert.Equal("Don't pet Dusty", state.Choices[1].Text);
    }

    [Fact]
    public void AddReadableTextExtras_AddsChoicesWhenDialogueTextIsBlank()
    {
        var state = new MenuState { Type = "DialogueBox", Present = true };

        StateMenuHandler.AddReadableTextExtras(state, new FakeBlankQuestionMenu());

        Assert.Equal(2, state.Choices.Count);
        Assert.Equal("0", state.Choices[0].Key);
        Assert.Equal("Pet Dusty", state.Choices[0].Text);
        Assert.Equal("1", state.Choices[1].Key);
        Assert.Equal("Don't pet Dusty", state.Choices[1].Text);
    }

    [Fact]
    public void AddReadableTextExtras_AddsDialogueReadyTelemetry()
    {
        var state = new MenuState { Type = "DialogueBox", Present = true };

        StateMenuHandler.AddReadableTextExtras(state, new FakeReadyDialogueMenu());

        Assert.Equal("2", state.Extra["dialogue_character_index"]);
        Assert.Equal("3", state.Extra["dialogue_text_length"]);
        Assert.Equal("true", state.Extra["dialogue_ready"]);
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

    [Fact]
    public void TryProjectDialogue_ReadsCurrentDialogueMethod()
    {
        var projected = StateMenuHandler.TryProjectDialogue(new FakeStardewDialogueMenu());

        Assert.NotNull(projected);
        Assert.Equal("Camilla", projected!.Speaker);
        Assert.Equal("The vineyard is quiet this morning.", projected.Text);
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

    private sealed class FakeReadyDialogueMenu
    {
        public string dialogue = "!!!";
        public int characterIndexInDialogue = 2;
        public int safetyTimer = 0;
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

    private sealed class FakeQuestionChoicesMenu
    {
        public string question = "What should I do?";
        public string[] questionChoices =
        {
            "Pet Dusty",
            "Don't pet Dusty",
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

    private sealed class FakeStardewDialogueMenu
    {
        public object characterDialogue = new FakeCharacterDialogueWithText();
    }

    private sealed class FakeCharacterDialogue
    {
        public FakeSpeaker speaker = new();
    }

    private sealed class FakeCharacterDialogueWithText
    {
        public FakeSpeaker speaker = new();

        public string getCurrentDialogue() => "The vineyard is quiet this morning.";
    }

    private sealed class FakeSpeaker
    {
        public string Name = "Camilla";
    }
}
