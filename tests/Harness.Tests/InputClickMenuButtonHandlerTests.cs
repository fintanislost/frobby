using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;
using StardewValley.Menus;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class InputClickMenuButtonHandlerTests
{
    [Fact]
    public void Handle_MissingTarget_ThrowsInvalidParams()
    {
        var p = JsonDocument.Parse("{\"button\":\"left\"}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => InputClickMenuButtonHandler.Handle(p));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
    }

    [Fact]
    public void Handle_NoActiveMenu_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"label\":\"1M\"}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickMenuButtonHandler.Handle(p, () => null, () => 0));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_Label_ClicksCenterOfCurrentPanelButtonRegion()
    {
        var menu = new FakeTerminalMenu(new FakeChartPanel());
        var p = JsonDocument.Parse("{\"label\":\"1M\"}").RootElement;

        var result = InputClickMenuButtonHandler.Handle(p, () => menu, () => 1234);
        var ok = JsonSerializer.Deserialize<MutatorOk>(result, ProtocolJson.Options)!;

        Assert.True(ok.Ok);
        Assert.Equal(1234, ok.Tick);
        Assert.Equal((1160, 62), menu.LastLeftClick);
    }

    [Fact]
    public void Handle_Id_ClicksMatchingButtonRegion()
    {
        var menu = new FakeTerminalMenu(new FakeChartPanel());
        var p = JsonDocument.Parse("{\"id\":\"tf-5d\"}").RootElement;

        InputClickMenuButtonHandler.Handle(p, () => menu, () => 0);

        Assert.Equal((1080, 62), menu.LastLeftClick);
    }

    [Fact]
    public void Handle_Repeat_ClicksMatchingButtonRegionMultipleTimes()
    {
        var menu = new FakeTerminalMenu(new FakeChartPanel());
        var p = JsonDocument.Parse("{\"id\":\"tf-5d\",\"repeat\":3}").RootElement;

        InputClickMenuButtonHandler.Handle(p, () => menu, () => 0);

        Assert.Equal(3, menu.LeftClickCount);
        Assert.Equal((1080, 62), menu.LastLeftClick);
    }

    [Fact]
    public void Handle_RightClick_UsesRightClick()
    {
        var menu = new FakeTerminalMenu(new FakeChartPanel());
        var p = JsonDocument.Parse("{\"label\":\"1D\",\"button\":\"right\"}").RootElement;

        InputClickMenuButtonHandler.Handle(p, () => menu, () => 0);

        Assert.Equal((1000, 62), menu.LastRightClick);
        Assert.Null(menu.LastLeftClick);
    }

    [Fact]
    public void Handle_RepeatLessThanOne_ThrowsInvalidParams()
    {
        var menu = new FakeTerminalMenu(new FakeChartPanel());
        var p = JsonDocument.Parse("{\"id\":\"tf-5d\",\"repeat\":0}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickMenuButtonHandler.Handle(p, () => menu, () => 0));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("repeat", ex.Message);
    }

    [Fact]
    public void Handle_MissingButton_ThrowsGameStateInvalid()
    {
        var menu = new FakeTerminalMenu(new FakeChartPanel());
        var p = JsonDocument.Parse("{\"label\":\"9Y\"}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickMenuButtonHandler.Handle(p, () => menu, () => 0));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("9Y", ex.Message);
    }

    private sealed class FakeTerminalMenu : IClickableMenu
    {
        private readonly object _currentPanel;

        public FakeTerminalMenu(object currentPanel)
        {
            _currentPanel = currentPanel;
        }

        public (int X, int Y)? LastLeftClick { get; private set; }
        public (int X, int Y)? LastRightClick { get; private set; }
        public int LeftClickCount { get; private set; }
        public Keys? LastKey { get; private set; }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            LastLeftClick = (x, y);
            LeftClickCount++;
        }

        public override void receiveRightClick(int x, int y, bool playSound = true)
            => LastRightClick = (x, y);

        public override void receiveKeyPress(Keys key)
            => LastKey = key;
    }

    private sealed class FakeChartPanel
    {
        private readonly FakeButtonRegion _oneDayButton = new("tf-1d", new Rectangle(980, 48, 40, 28), "1D");
        private readonly FakeButtonRegion _fiveDayButton = new("tf-5d", new Rectangle(1060, 48, 40, 28), "5D");
        private readonly FakeButtonRegion _oneMonthButton = new("tf-1m", new Rectangle(1140, 48, 40, 28), "1M");
    }

    private readonly struct FakeButtonRegion
    {
        public FakeButtonRegion(string id, Rectangle bounds, string label)
        {
            Id = id;
            Bounds = bounds;
            Label = label;
        }

        public string Id { get; }
        public Rectangle Bounds { get; }
        public string Label { get; }
    }
}
