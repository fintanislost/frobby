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
}
