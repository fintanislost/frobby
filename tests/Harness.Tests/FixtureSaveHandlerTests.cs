using System.Text.Json;
using SdvTestFramework.Harness.Handlers;
using SdvTestFramework.Protocol;
using Xunit;

namespace SdvTestFramework.Harness.Tests;

public class FixtureSaveHandlerTests
{
    [Fact]
    public void Handle_MissingName_ThrowsInvalidParams()
    {
        var req = JsonDocument.Parse("""{}""").RootElement;
        var ex = Assert.Throws<JsonRpcException>(() => FixtureSaveHandler.Handle(req));
        Assert.Equal(JsonRpcErrorCode.InvalidParams, ex.Code);
        Assert.Contains("name", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = "Requires live SDV — integration tested via FixtureBuilderIntegrationTests (T11 smoke).")]
    public void Handle_AtTitleScreen_ThrowsGameStateInvalid() { }

    [Fact(Skip = "Requires live SDV — integration tested via FixtureBuilderIntegrationTests (T11 smoke).")]
    public void Handle_InSave_TriggersSaveGameSave_AndReturnsPath() { }
}
