using System;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Runner.Mcp;

namespace SdvTestFramework.Runner.Commands;

/// <summary>
/// <c>sdv-test mcp</c> — run the MCP stdio server. Reads JSON-RPC requests from stdin,
/// writes responses to stdout. stderr is reserved for diagnostic logs (unused by MVP).
/// </summary>
public static class McpCommand
{
    public static async Task<int> RunAsync(ReadOnlyMemory<string> _args, CancellationToken ct)
    {
        var registry = McpServer.BuildRegistry();
        var lifecycle = new SdvLifecycle();
        var server = new McpServer(registry, lifecycle);

        try
        {
            using var stdin = Console.OpenStandardInput();
            using var stdout = Console.OpenStandardOutput();
            await server.RunAsync(stdin, stdout, ct);
            return 0;
        }
        catch (OperationCanceledException) { return 0; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[mcp] fatal: {ex.Message}");
            return 1;
        }
        finally
        {
            await lifecycle.DisposeAsync();
        }
    }
}
