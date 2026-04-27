using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using SdvTestFramework.Protocol;

namespace SdvTestFramework.Runner.Recording;

/// <summary>Captured RPC call — method name + raw params JSON for replay in a scenario.</summary>
public sealed record RecordedStep(string Method, string ParamsJson);

/// <summary>
/// Subscribes to <see cref="JsonRpcSession.RequestReceived"/> (the existing event at
/// <c>src/Protocol/JsonRpcSession.cs:27</c>), filters out reads (<c>state.*</c>) + lifecycle
/// calls (<c>scenario.begin</c>/<c>scenario.end</c>), and buffers the remaining tuples.
/// </summary>
/// <remarks>
/// Call <see cref="OnRequest"/> directly (or subscribe via <see cref="Subscribe"/>) from
/// the receiver side. <see cref="WriteToFile"/> serializes the buffer as a scenario JSON.
/// </remarks>
public sealed class RpcTraceRecorder
{
    private readonly List<RecordedStep> _steps = new();
    private readonly object _lock = new();

    /// <summary>Number of steps buffered so far.</summary>
    public int Count { get { lock (_lock) return _steps.Count; } }

    /// <summary>Snapshot of steps in order received. Safe to enumerate.</summary>
    public IReadOnlyList<RecordedStep> Steps
    {
        get { lock (_lock) return _steps.ToArray(); }
    }

    /// <summary>Attach to a session; returns a callback for unsubscription.</summary>
    public System.Action Subscribe(JsonRpcSession session)
    {
        System.Action<JsonRpcRequest> handler = OnRequest;
        session.RequestReceived += handler;
        return () => session.RequestReceived -= handler;
    }

    /// <summary>Process one incoming request. Filters reads + lifecycle; buffers everything else.</summary>
    public void OnRequest(JsonRpcRequest req)
    {
        if (ShouldSkip(req.Method)) return;

        var paramsJson = req.Params is { } p ? p.GetRawText() : "{}";
        lock (_lock) _steps.Add(new RecordedStep(req.Method, paramsJson));
    }

    private static bool ShouldSkip(string method)
    {
        // Reads have no replay value.
        if (method.StartsWith("state.", System.StringComparison.Ordinal)) return true;

        // The recorded scenario has its own begin/end lifecycle; including the original's
        // begin/end would double-wrap.
        if (method == "scenario.begin" || method == "scenario.end") return true;

        return false;
    }

    /// <summary>
    /// Write the buffer as a scenario JSON at <paramref name="path"/>. Creates parent dirs.
    /// The scenario has <paramref name="name"/> + <c>config.seed = <paramref name="seed"/></c>
    /// + recorded steps + empty assertions array (user adds assertions post-hoc).
    /// </summary>
    public void WriteToFile(string path, string name, int seed)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var stepsArray = new JsonArray();
        foreach (var s in Steps)
        {
            var stepObj = new JsonObject { ["action"] = s.Method };
            // params can be arbitrary JSON; parse + re-attach as a JsonNode so the emitted
            // file has it as a structured object (not a string-escaped blob).
            try { stepObj["args"] = JsonNode.Parse(s.ParamsJson) ?? new JsonObject(); }
            catch { stepObj["args"] = new JsonObject(); }
            stepsArray.Add(stepObj);
        }

        var obj = new JsonObject
        {
            ["name"] = name,
            ["config"] = new JsonObject { ["seed"] = seed },
            ["steps"] = stepsArray,
            ["assertions"] = new JsonArray(),
        };

        File.WriteAllText(path, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
