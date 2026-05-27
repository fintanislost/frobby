using System.Collections.Generic;
using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Harness.Rpc;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public sealed class FestivalStartHandlerTests
{
    [Fact]
    public void Handle_ReturnsFestivalStartResult()
    {
        var world = new FakeFestivalStartWorld
        {
            Result = new FestivalStartResult
            {
                Tick = 123,
                Id = "fall27",
                Location = "Town",
                IsFestival = true,
            },
        };
        var p = JsonDocument.Parse("{\"location\":\"Town\"}").RootElement;

        var result = FestivalStartHandler.Handle(p, world);

        Assert.Equal("Town", world.ExpectedLocation);
        Assert.Equal(123, result.GetProperty("tick").GetInt32());
        Assert.Equal("fall27", result.GetProperty("id").GetString());
        Assert.Equal("Town", result.GetProperty("location").GetString());
        Assert.True(result.GetProperty("is_festival").GetBoolean());
    }

    [Fact]
    public void Handle_RejectsEmptyLocation()
    {
        var world = new FakeFestivalStartWorld();
        var p = JsonDocument.Parse("{\"location\":\"\"}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => FestivalStartHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("params.location must not be empty", ex.Message);
    }

    [Fact]
    public void Handle_RejectsMismatchedExpectedLocation()
    {
        var world = new FakeFestivalStartWorld
        {
            ErrorCode = JsonRpcErrorCode.GameStateInvalid,
            ErrorMessage = "festival.start expected location Town but festival is in Forest",
        };
        var p = JsonDocument.Parse("{\"location\":\"Town\"}").RootElement;

        var ex = Assert.Throws<JsonRpcException>(() => FestivalStartHandler.Handle(p, world));

        Assert.Equal(JsonRpcErrorCode.GameStateInvalid, ex.Code);
        Assert.Contains("expected location Town", ex.Message);
    }

    [Fact]
    public void SelectFestivalSetupScript_CyclesYearVariantsLikeStardew()
    {
        var data = new Dictionary<string, string>
        {
            ["set-up"] = "base",
            ["set-up_y2"] = "year two",
            ["set-up_y3"] = "year three",
        };

        Assert.Equal("base", SdvFestivalStartWorld.SelectFestivalSetupScript(data, 1));
        Assert.Equal("year two", SdvFestivalStartWorld.SelectFestivalSetupScript(data, 2));
        Assert.Equal("year three", SdvFestivalStartWorld.SelectFestivalSetupScript(data, 3));
        Assert.Equal("base", SdvFestivalStartWorld.SelectFestivalSetupScript(data, 4));
    }

    [Fact]
    public void SelectFestivalSetupScript_DoesNotMutateAdditionalActorDataIntoLoadActors()
    {
        var data = new Dictionary<string, string>
        {
            ["set-up"] = "fallFest/loadActors Set-Up/playerControl fair",
            ["Set-Up_additionalCharacters"] = "Sophia 47 60 down/Andy 49 70 down",
        };

        Assert.Equal(
            "fallFest/loadActors Set-Up/playerControl fair",
            SdvFestivalStartWorld.SelectFestivalSetupScript(data, 1));
    }

    [Fact]
    public void ParseFestivalAdditionalActors_ParsesSlashDelimitedActors()
    {
        var actors = SdvFestivalStartWorld.ParseFestivalAdditionalActors(
            "Sophia 47 60 down/Andy 49 70 right/Susan 49 65 0");

        Assert.Collection(
            actors,
            actor =>
            {
                Assert.Equal("Sophia", actor.Name);
                Assert.Equal(47, actor.X);
                Assert.Equal(60, actor.Y);
                Assert.Equal(2, actor.FacingDirection);
            },
            actor =>
            {
                Assert.Equal("Andy", actor.Name);
                Assert.Equal(49, actor.X);
                Assert.Equal(70, actor.Y);
                Assert.Equal(1, actor.FacingDirection);
            },
            actor =>
            {
                Assert.Equal("Susan", actor.Name);
                Assert.Equal(49, actor.X);
                Assert.Equal(65, actor.Y);
                Assert.Equal(0, actor.FacingDirection);
            });
    }

    [Fact]
    public void SelectFestivalAdditionalActorData_CyclesYearVariantsLikeStardew()
    {
        var data = new Dictionary<string, string>
        {
            ["Set-Up_additionalCharacters"] = "Sophia 47 60 down",
            ["Set-Up_additionalCharacters_y2"] = "Sophia 47 60 down/Susan 49 71 down",
        };

        Assert.Equal(
            "Sophia 47 60 down",
            SdvFestivalStartWorld.SelectFestivalAdditionalActorData(data, 1));
        Assert.Equal(
            "Sophia 47 60 down/Susan 49 71 down",
            SdvFestivalStartWorld.SelectFestivalAdditionalActorData(data, 2));
        Assert.Equal(
            "Sophia 47 60 down",
            SdvFestivalStartWorld.SelectFestivalAdditionalActorData(data, 3));
    }

    private sealed class FakeFestivalStartWorld : IFestivalStartWorld
    {
        public string? ExpectedLocation { get; private set; }
        public FestivalStartResult Result { get; set; } = new();
        public JsonRpcErrorCode? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }

        public FestivalStartResult StartCurrentFestival(string? expectedLocation)
        {
            ExpectedLocation = expectedLocation;
            if (ErrorCode is { } code)
                throw new JsonRpcException(code, ErrorMessage ?? "festival.start failed");

            return Result;
        }
    }
}
