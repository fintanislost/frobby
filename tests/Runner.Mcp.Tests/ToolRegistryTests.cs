using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Mcp;
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests;

public class ToolRegistryTests
{
    private sealed class StubTool : ITool
    {
        public string Name { get; init; } = "stub";
        public string Description => "stub";
        public JsonElement InputSchema => JsonDocument.Parse("{\"type\":\"object\"}").RootElement;
        public Task<McpToolResult> InvokeAsync(JsonElement args, ToolInvocationContext context, CancellationToken ct)
            => Task.FromResult(McpToolResult.Success(JsonDocument.Parse("{}").RootElement));
    }

    [Fact]
    public void Get_ExistingName_ReturnsTool()
    {
        var reg = new ToolRegistry();
        reg.Register(new StubTool { Name = "foo" });
        var tool = reg.Get("foo");
        Assert.NotNull(tool);
        Assert.Equal("foo", tool!.Name);
    }

    [Fact]
    public void Get_UnknownName_ReturnsNull()
    {
        var reg = new ToolRegistry();
        Assert.Null(reg.Get("nope"));
    }
}
