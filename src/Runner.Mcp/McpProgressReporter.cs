using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SdvTestFramework.Protocol;

namespace SdvTestFramework.Runner.Mcp;

public sealed class McpProgressReporter
{
    private readonly JsonElement? _progressToken;
    private readonly Func<JsonRpcNotification, CancellationToken, Task> _writeNotificationAsync;

    public static McpProgressReporter None { get; } = new(null, static (_, _) => Task.CompletedTask);

    public McpProgressReporter(
        JsonElement? progressToken,
        Func<JsonRpcNotification, CancellationToken, Task> writeNotificationAsync)
    {
        _progressToken = progressToken is { } token ? token.Clone() : null;
        _writeNotificationAsync = writeNotificationAsync;
    }

    public bool Enabled => _progressToken.HasValue;

    public async Task ReportAsync(int progress, int? total, string message, CancellationToken cancellationToken)
    {
        if (_progressToken is not { } token)
            return;

        var parameters = new JsonObject
        {
            ["progressToken"] = JsonNode.Parse(token.GetRawText())!,
            ["progress"] = progress,
            ["message"] = message,
        };
        if (total is { } totalValue)
            parameters["total"] = totalValue;

        using var doc = JsonDocument.Parse(parameters.ToJsonString());
        var notification = new JsonRpcNotification
        {
            Method = "notifications/progress",
            Params = doc.RootElement.Clone(),
        };
        try
        {
            await _writeNotificationAsync(notification, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException ex)
        {
            throw new McpProgressWriteException("failed to write MCP progress notification", ex);
        }
    }
}

internal sealed class McpProgressWriteException : IOException
{
    public McpProgressWriteException(string message, IOException innerException)
        : base(message, innerException)
    {
    }
}
