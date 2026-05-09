using System.Text.Json;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using SdvTestFramework.Harness.Handlers;
using StardewValley.Menus;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class InputClickMenuAdvanceHandlerTests
{
    [Fact]
    public void Handle_ClicksReflectedNextDialogueButtonWhenPresent()
    {
        var menu = new FakeAdvanceMenu();
        var p = JsonDocument.Parse("{}").RootElement;

        InputClickMenuAdvanceHandler.Handle(p, () => menu, () => 0);

        Assert.Equal((720, 620), menu.LastLeftClick);
    }

    [Fact]
    public void Handle_NoAdvanceButton_ClicksBottomRightFallback()
    {
        var menu = new FakeMenuWithoutAdvanceButton();
        var p = JsonDocument.Parse("{}").RootElement;

        InputClickMenuAdvanceHandler.Handle(p, () => menu, () => 0);

        Assert.Equal((680, 380), menu.LastLeftClick);
        Assert.Equal(new[] { Keys.X, Keys.Enter, Keys.Space }, menu.KeyPresses);
    }

    [Fact]
    public void Handle_DialogueWidthFallback_ClicksBottomRightOfTextPanel()
    {
        var menu = new FakeCharacterDialogueMenu();
        var p = JsonDocument.Parse("{}").RootElement;

        InputClickMenuAdvanceHandler.Handle(p, () => menu, () => 0);

        Assert.Equal((708, 640), menu.LastLeftClick);
        Assert.Equal(new[] { Keys.X, Keys.Enter, Keys.Space }, menu.KeyPresses);
    }

    private sealed class FakeAdvanceMenu : IClickableMenu
    {
        public readonly ClickableComponent nextDialogueButton = new(new Rectangle(700, 600, 40, 40), "next");
        public (int X, int Y)? LastLeftClick { get; private set; }
        public List<Keys> KeyPresses { get; } = new();

        public override void receiveLeftClick(int x, int y, bool playSound = true)
            => LastLeftClick = (x, y);

        public override void receiveKeyPress(Keys key)
            => KeyPresses.Add(key);
    }

    private sealed class FakeMenuWithoutAdvanceButton : IClickableMenu
    {
        public FakeMenuWithoutAdvanceButton()
        {
            xPositionOnScreen = 100;
            yPositionOnScreen = 200;
            width = 640;
            height = 240;
        }

        public (int X, int Y)? LastLeftClick { get; private set; }
        public List<Keys> KeyPresses { get; } = new();

        public override void receiveLeftClick(int x, int y, bool playSound = true)
            => LastLeftClick = (x, y);

        public override void receiveKeyPress(Keys key)
            => KeyPresses.Add(key);
    }

    private sealed class FakeCharacterDialogueMenu : IClickableMenu
    {
        public FakeCharacterDialogueMenu()
        {
            xPositionOnScreen = 0;
            yPositionOnScreen = 250;
            width = 1280;
            height = 450;
        }

        public int dialogueWidth = 768;
        public (int X, int Y)? LastLeftClick { get; private set; }
        public List<Keys> KeyPresses { get; } = new();

        public override void receiveLeftClick(int x, int y, bool playSound = true)
            => LastLeftClick = (x, y);

        public override void receiveKeyPress(Keys key)
            => KeyPresses.Add(key);
    }
}
