using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Mcp;
using Xunit;

namespace SdvTestFramework.Runner.Mcp.Tests;

public class McpServerTests
{
    private static async Task<string[]> RunServerWith(string input, ToolRegistry? reg = null)
    {
        var inBytes = Encoding.UTF8.GetBytes(input);
        using var stdin = new MemoryStream(inBytes);
        using var stdout = new MemoryStream();
        var server = new McpServer(reg ?? new ToolRegistry(), lifecycle: null);
        await server.RunAsync(stdin, stdout, CancellationToken.None);
        var outStr = Encoding.UTF8.GetString(stdout.ToArray());
        return outStr.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public async Task Initialize_ReturnsServerInfo_AndToolsCapability()
    {
        const string req = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\",\"clientInfo\":{\"name\":\"test\"},\"capabilities\":{}}}\n";
        var lines = await RunServerWith(req);

        Assert.Single(lines);
        var doc = JsonDocument.Parse(lines[0]);
        Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt32());
        var result = doc.RootElement.GetProperty("result");
        Assert.Equal("2024-11-05", result.GetProperty("protocolVersion").GetString());
        Assert.Equal("sdv-test-mcp", result.GetProperty("serverInfo").GetProperty("name").GetString());
        Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _));
    }

    [Fact]
    public async Task ToolsList_ReturnsRegisteredTools()
    {
        var lines = await RunServerWith("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}\n");
        var doc = JsonDocument.Parse(lines[0]);
        Assert.Equal(2, doc.RootElement.GetProperty("id").GetInt32());
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        Assert.Equal(JsonValueKind.Array, tools.ValueKind);
        Assert.Equal(0, tools.GetArrayLength());
    }

    [Fact]
    public async Task ToolsCall_UnknownTool_ReturnsMethodNotFound()
    {
        const string req = "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"nope\",\"arguments\":{}}}\n";
        var lines = await RunServerWith(req);

        var doc = JsonDocument.Parse(lines[0]);
        var err = doc.RootElement.GetProperty("error");
        Assert.Equal(-32601, err.GetProperty("code").GetInt32());
        Assert.Contains("nope", err.GetProperty("message").GetString());
    }
}
