using System.Text.Json;
using Microsoft.Xna.Framework;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using StardewValley.Menus;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class InputClickMenuChoiceHandlerTests
{
    [Fact]
    public void Handle_NoActiveMenu_ThrowsGameStateInvalid()
    {
        var p = JsonDocument.Parse("{\"text_equals\":\"Pet Dusty\"}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() =>
            InputClickMenuChoiceHandler.Handle(p, () => null, () => 0));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
    }

    [Fact]
    public void Handle_TextMatch_ClicksMatchingResponseComponent()
    {
        var menu = new FakeChoiceMenu();
        var p = JsonDocument.Parse("{\"text_equals\":\"Pet Dusty\"}").RootElement;

        InputClickMenuChoiceHandler.Handle(p, () => menu, () => 0);

        Assert.Equal((250, 420), menu.LastLeftClick);
    }

    [Fact]
    public void Handle_KeyMatch_ClicksMatchingResponseComponent()
    {
        var menu = new FakeChoiceMenu();
        var p = JsonDocument.Parse("{\"key\":\"leave\"}").RootElement;

        InputClickMenuChoiceHandler.Handle(p, () => menu, () => 0);

        Assert.Equal((250, 480), menu.LastLeftClick);
    }

    [Fact]
    public void Handle_TextMatch_HoversBeforeClickingMatchingResponseComponent()
    {
        var menu = new FakeHoverRequiredChoiceMenu();
        var p = JsonDocument.Parse("{\"text_equals\":\"Pet Dusty\"}").RootElement;

        InputClickMenuChoiceHandler.Handle(p, () => menu, () => 0);

        Assert.Equal((250, 420), menu.LastHover);
        Assert.Equal((250, 420), menu.LastLeftClick);
    }

    [Fact]
    public void Handle_QuestionChoicesMember_ClicksMatchingResponseComponent()
    {
        var menu = new FakeQuestionChoicesMenu();
        var p = JsonDocument.Parse("{\"text_equals\":\"Pet Dusty\"}").RootElement;

        InputClickMenuChoiceHandler.Handle(p, () => menu, () => 0);

        Assert.Equal((250, 420), menu.LastLeftClick);
    }

    private sealed class FakeChoiceMenu : IClickableMenu
    {
        public FakeResponse[] responses =
        {
            new("pet", "Pet Dusty"),
            new("leave", "Don't pet Dusty"),
        };

        public ClickableComponent[] responseCC =
        {
            new(new Rectangle(100, 400, 300, 40), "pet"),
            new(new Rectangle(100, 460, 300, 40), "leave"),
        };

        public (int X, int Y)? LastLeftClick { get; private set; }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
            => LastLeftClick = (x, y);
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

    private sealed class FakeQuestionChoicesMenu : IClickableMenu
    {
        public string[] questionChoices =
        {
            "Pet Dusty",
            "Don't pet Dusty",
        };

        public ClickableComponent[] responseCC =
        {
            new(new Rectangle(100, 400, 300, 40), "pet"),
            new(new Rectangle(100, 460, 300, 40), "leave"),
        };

        public (int X, int Y)? LastLeftClick { get; private set; }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
            => LastLeftClick = (x, y);
    }

    private sealed class FakeHoverRequiredChoiceMenu : IClickableMenu
    {
        public FakeResponse[] responses =
        {
            new("pet", "Pet Dusty"),
        };

        public ClickableComponent[] responseCC =
        {
            new(new Rectangle(100, 400, 300, 40), "pet"),
        };

        public (int X, int Y)? LastHover { get; private set; }
        public (int X, int Y)? LastLeftClick { get; private set; }

        public override void performHoverAction(int x, int y)
            => LastHover = (x, y);

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            if (LastHover == (x, y))
                LastLeftClick = (x, y);
        }
    }
}
