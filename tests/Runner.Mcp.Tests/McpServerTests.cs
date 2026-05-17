using System.IO;
using System.Linq;
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
        var capabilities = result.GetProperty("capabilities");
        Assert.True(capabilities.TryGetProperty("tools", out _));
        Assert.True(capabilities.TryGetProperty("resources", out _));
        Assert.True(capabilities.TryGetProperty("prompts", out _));
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

    [Fact]
    public async Task ResourcesList_ReturnsStaticResources()
    {
        var lines = await RunServerWith("{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"resources/list\"}\n");
        using var doc = JsonDocument.Parse(lines[0]);

        var resources = doc.RootElement.GetProperty("result").GetProperty("resources");
        var uris = resources.EnumerateArray()
            .Select(r => r.GetProperty("uri").GetString())
            .ToArray();

        Assert.Contains("frobby://docs/wiki/index", uris);
        Assert.Contains("frobby://docs/rpc-schema", uris);
        Assert.Contains("frobby://docs/mcp-quickstart", uris);
        Assert.Contains("frobby://scenarios/list", uris);
    }

    [Fact]
    public async Task ResourcesRead_DocResource_ReturnsMarkdownText()
    {
        const string req = "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"resources/read\",\"params\":{\"uri\":\"frobby://docs/wiki/index\"}}\n";
        var lines = await RunServerWith(req);
        using var doc = JsonDocument.Parse(lines[0]);

        var content = doc.RootElement.GetProperty("result").GetProperty("contents")[0];
        Assert.Equal("frobby://docs/wiki/index", content.GetProperty("uri").GetString());
        Assert.Equal("text/markdown", content.GetProperty("mimeType").GetString());
        Assert.Contains("Frobby", content.GetProperty("text").GetString());
    }

    [Fact]
    public async Task ResourcesRead_ScenarioList_ReturnsMarkdownIndex()
    {
        const string req = "{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"resources/read\",\"params\":{\"uri\":\"frobby://scenarios/list\"}}\n";
        var lines = await RunServerWith(req);
        using var doc = JsonDocument.Parse(lines[0]);

        var content = doc.RootElement.GetProperty("result").GetProperty("contents")[0];
        Assert.Equal("frobby://scenarios/list", content.GetProperty("uri").GetString());
        Assert.Equal("text/markdown", content.GetProperty("mimeType").GetString());
        Assert.Contains("Scenario", content.GetProperty("text").GetString());
    }

    [Fact]
    public async Task ResourcesRead_UnknownUri_ReturnsInvalidParams()
    {
        const string req = "{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"resources/read\",\"params\":{\"uri\":\"frobby://docs/nope\"}}\n";
        var lines = await RunServerWith(req);
        using var doc = JsonDocument.Parse(lines[0]);

        var err = doc.RootElement.GetProperty("error");
        Assert.Equal(-32602, err.GetProperty("code").GetInt32());
        Assert.Contains("frobby://docs/nope", err.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PromptsList_ReturnsStaticPrompts()
    {
        var lines = await RunServerWith("{\"jsonrpc\":\"2.0\",\"id\":8,\"method\":\"prompts/list\"}\n");
        using var doc = JsonDocument.Parse(lines[0]);

        var prompts = doc.RootElement.GetProperty("result").GetProperty("prompts");
        var names = prompts.EnumerateArray()
            .Select(p => p.GetProperty("name").GetString())
            .ToArray();

        Assert.Contains("create_scenario", names);
        Assert.Contains("debug_failed_scenario", names);
        Assert.Contains("add_mod_ui_coverage", names);
        Assert.Contains("explain_available_tools", names);
        Assert.Contains(
            prompts.EnumerateArray(),
            p => p.GetProperty("name").GetString() == "create_scenario" &&
                 p.GetProperty("arguments").EnumerateArray().Any(a => a.GetProperty("name").GetString() == "mod_name"));
    }

    [Fact]
    public async Task PromptsGet_WithArguments_ReturnsPromptMessages()
    {
        const string req = """
{"jsonrpc":"2.0","id":9,"method":"prompts/get","params":{"name":"create_scenario","arguments":{"mod_name":"Starberg","behavior":"chart panel opens"}}}
""";
        var lines = await RunServerWith(req + "\n");
        using var doc = JsonDocument.Parse(lines[0]);

        var result = doc.RootElement.GetProperty("result");
        Assert.Contains("Create Frobby Scenario", result.GetProperty("description").GetString());
        var message = result.GetProperty("messages")[0];
        Assert.Equal("user", message.GetProperty("role").GetString());
        var text = message.GetProperty("content").GetProperty("text").GetString();
        Assert.Contains("Starberg", text);
        Assert.Contains("chart panel opens", text);
        Assert.Contains("frobby://docs/rpc-schema", text);
    }

    [Fact]
    public async Task PromptsGet_UnknownName_ReturnsInvalidParams()
    {
        const string req = "{\"jsonrpc\":\"2.0\",\"id\":10,\"method\":\"prompts/get\",\"params\":{\"name\":\"nope\"}}\n";
        var lines = await RunServerWith(req);
        using var doc = JsonDocument.Parse(lines[0]);

        var err = doc.RootElement.GetProperty("error");
        Assert.Equal(-32602, err.GetProperty("code").GetInt32());
        Assert.Contains("nope", err.GetProperty("message").GetString());
    }
}
