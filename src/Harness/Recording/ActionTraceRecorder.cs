using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SdvTestFramework.Harness.Recording;

/// <summary>
/// Records gameplay actions during a session for later replay. Subscribes (externally,
/// via <c>ModEntry</c>'s SMAPI event handlers) to capture <see cref="RecordedAction"/>
/// events into a buffer; on stop, translates via <see cref="ActionTraceTranslator"/>
/// and writes the resulting scenario JSON via <see cref="IFileSink"/>.
/// </summary>
/// <remarks>
/// Test-friendly: takes <see cref="IFileSink"/> + log delegate via constructor. SMAPI
/// event hookup happens in <c>ModEntry</c>; this class just exposes <c>Start</c> /
/// <c>Stop</c> / <c>Record</c> + <c>IsRecording</c>.
/// </remarks>
public sealed class ActionTraceRecorder
{
    private readonly IFileSink _sink;
    private readonly Action<string> _log;
    private readonly object _lock = new();

    private string? _activeName;
    private string? _activeOutputDir;
    private List<RecordedAction>? _buffer;

    /// <summary>Initializes the recorder with the given sink and log delegate.</summary>
    public ActionTraceRecorder(IFileSink sink, Action<string> log)
    {
        _sink = sink;
        _log = log;
    }

    /// <summary>True when a recording session is active.</summary>
    public bool IsRecording { get { lock (_lock) return _buffer is not null; } }

    /// <summary>Start a session. Logs + no-ops if one is already active.</summary>
    public void Start(string name, string outputDir)
    {
        lock (_lock)
        {
            if (_buffer is not null)
            {
                _log($"[harness_record_actions] session '{_activeName}' already in progress; type harness_record_stop first");
                return;
            }
            _activeName = name;
            _activeOutputDir = outputDir;
            _buffer = new List<RecordedAction>();
            var path = Path.Combine(outputDir, $"{name}.test.json");
            _log($"[harness_record_actions] recording session '{name}' — type harness_record_stop to finalize. Output: {path}");
        }
    }

    /// <summary>Record an action. Wired by SMAPI event handlers in production. No-op when not recording.</summary>
    internal void Record(RecordedAction action)
    {
        lock (_lock)
        {
            _buffer?.Add(action);
        }
    }

    /// <summary>Stop, translate, write. Logs + no-ops if no session active.</summary>
    public void Stop()
    {
        List<RecordedAction>? buffer;
        string? name;
        string? outputDir;
        lock (_lock)
        {
            if (_buffer is null)
            {
                _log("[harness_record_stop] no active recording session");
                return;
            }
            buffer = _buffer;
            name = _activeName;
            outputDir = _activeOutputDir;
            _buffer = null;
            _activeName = null;
            _activeOutputDir = null;
        }

        var steps = ActionTraceTranslator.Translate(buffer);
        var stepsArray = new JsonArray();
        foreach (var s in steps)
        {
            stepsArray.Add(new JsonObject
            {
                ["action"] = s.Action,
                ["args"] = s.Args is { } args ? JsonNode.Parse(args.GetRawText()) : new JsonObject(),
            });
        }
        var obj = new JsonObject
        {
            ["name"] = name!,
            ["config"] = new JsonObject { ["seed"] = 42 },
            ["steps"] = stepsArray,
            ["assertions"] = new JsonArray(),
        };
        var contents = obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var path = Path.Combine(outputDir!, $"{name}.test.json");
        try
        {
            _sink.Write(path, contents);
            _log($"[harness_record_stop] wrote {steps.Count} steps to {path}");
        }
        catch (Exception ex)
        {
            _log($"[harness_record_stop] write failed: {ex.Message}");
        }
    }
}
