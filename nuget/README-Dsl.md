# SdvTestFramework.Runner.Dsl

Typed C# DSL for writing Stardew Valley mod tests. Use `[Scenario]` + ambient static
facets (`Player`, `World`, `Time`, `Draw`, `State`, `Freeze`, `Fixture`, `Bitmap`,
`Wait`) to author tests as plain xUnit methods.

## Install

```bash
dotnet add package SdvTestFramework.Runner.Dsl
```

You also need the CLI tool (which provides SDV launch + harness deployment):

```bash
dotnet tool install -g SdvTestFramework.Cli
```

## Minimal example

```csharp
using SdvTestFramework.Runner.Dsl;
using Xunit;

[Collection("SDV")]
public class ShopMenuTests
{
    [Fact, Scenario(fixture: "m0spike_436515781")]
    public async Task Warp_ShopOpens()
    {
        await Player.Warp("SeedShop", 4, 19);
        await Player.SetMoney(5000);
        var player = await State.Player();
        Assert.Equal(5000, player.Money);
    }
}
```

Quickstart: https://github.com/fintan/sdv-test-framework/blob/main/docs/dsl-quickstart.md
