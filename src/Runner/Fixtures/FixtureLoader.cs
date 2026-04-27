using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using SdvTestFramework.Protocol.Json;

namespace SdvTestFramework.Runner.Fixtures;

/// <summary>
/// Loads and validates fixture scripts (<c>*.fixture.json</c>) per <c>schemas/fixture.schema.json</c>.
/// Mirrors <c>ScenarioLoader</c>'s pattern. Fails loudly with <see cref="FixtureLoadException"/>.
/// </summary>
public static class FixtureLoader
{
    private static readonly JsonSchema Schema = LoadSchema();

    private static JsonSchema LoadSchema()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "schemas", "fixture.schema.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "schemas", "fixture.schema.json"),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return JsonSchema.FromFile(c);
        throw new FileNotFoundException(
            "fixture.schema.json not found in any known location. Candidates: " + string.Join(", ", candidates));
    }

    /// <summary>
    /// Reads, parses, schema-validates, and deserializes the given fixture script.
    /// Throws <see cref="FixtureLoadException"/> on any failure.
    /// </summary>
    public static FixtureSpec Load(string path)
    {
        if (!File.Exists(path))
            throw new FixtureLoadException(path, "file not found");

        string json;
        try { json = File.ReadAllText(path); }
        catch (IOException ex) { throw new FixtureLoadException(path, $"read failed: {ex.Message}", ex); }

        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException ex) { throw new FixtureLoadException(path, $"invalid JSON: {ex.Message}", ex); }
        if (node is null) throw new FixtureLoadException(path, "empty file");

        var result = Schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });
        if (!result.IsValid)
        {
            var messages = string.Join("; ",
                result.Details
                    .Where(d => !d.IsValid && d.Errors is { Count: > 0 })
                    .Select(d => $"{d.InstanceLocation}: {d.Errors!.First().Value}"));
            if (string.IsNullOrEmpty(messages))
                messages = "validation failed (no detailed error available)";
            throw new FixtureLoadException(path, $"schema validation failed: {messages}");
        }

        try
        {
            return JsonSerializer.Deserialize<FixtureSpec>(json, ProtocolJson.Options)
                ?? throw new FixtureLoadException(path, "deserialization returned null");
        }
        catch (JsonException ex)
        {
            throw new FixtureLoadException(path, $"deserialization failed: {ex.Message}", ex);
        }
    }
}
