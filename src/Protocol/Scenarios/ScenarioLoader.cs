using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using SdvTestFramework.Protocol.Json;
using SdvTestFramework.Protocol.Models;

namespace SdvTestFramework.Protocol.Scenarios;

/// <summary>Thrown by <see cref="ScenarioLoader"/> when a scenario file can't be parsed or doesn't validate.</summary>
public sealed class ScenarioLoadException : Exception
{
    /// <summary>Constructs with file path and message; message format is <c>"{file}: {message}"</c>.</summary>
    public ScenarioLoadException(string file, string message) : base($"{file}: {message}") { }

    /// <summary>Constructs with file path, message, and an inner exception (e.g., the underlying JSON parse error).</summary>
    public ScenarioLoadException(string file, string message, Exception inner) : base($"{file}: {message}", inner) { }
}

/// <summary>
/// Loads and validates scenario files (<c>*.test.json</c>) per <c>schemas/scenario.schema.json</c>.
/// Fails loudly with <see cref="ScenarioLoadException"/> on missing files, invalid JSON, or
/// schema-violating content.
/// </summary>
public static class ScenarioLoader
{
    private static readonly JsonSchema Schema = LoadSchema();

    private static JsonSchema LoadSchema()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "schemas", "scenario.schema.json"),
            // repo-relative during dev + tests
            Path.Combine(Directory.GetCurrentDirectory(), "schemas", "scenario.schema.json"),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return JsonSchema.FromFile(c);
        throw new FileNotFoundException("scenario.schema.json not found in any known location. Candidates: " +
            string.Join(", ", candidates));
    }

    /// <summary>
    /// Reads, parses, schema-validates, and deserializes the given scenario file.
    /// Throws <see cref="ScenarioLoadException"/> on any failure (missing file, invalid JSON,
    /// schema violations, deserialization errors).
    /// </summary>
    public static ScenarioSpec Load(string path)
    {
        if (!File.Exists(path))
            throw new ScenarioLoadException(path, "file not found");

        string json;
        try { json = File.ReadAllText(path); }
        catch (IOException ex) { throw new ScenarioLoadException(path, $"read failed: {ex.Message}", ex); }

        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException ex) { throw new ScenarioLoadException(path, $"invalid JSON: {ex.Message}", ex); }
        if (node is null) throw new ScenarioLoadException(path, "empty file");

        var result = Schema.Evaluate(node, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
        });
        if (!result.IsValid)
        {
            var messages = string.Join("; ",
                result.Details
                    .Where(d => !d.IsValid && d.Errors is { Count: > 0 })
                    .Select(d =>
                    {
                        var firstError = d.Errors!.First();
                        return $"{d.InstanceLocation}: {firstError.Value}";
                    }));
            if (string.IsNullOrEmpty(messages))
                messages = "validation failed (no detailed error available)";
            throw new ScenarioLoadException(path, $"schema validation failed: {messages}");
        }

        try
        {
            var spec = JsonSerializer.Deserialize<ScenarioSpec>(json, ProtocolJson.Options)
                ?? throw new ScenarioLoadException(path, "deserialization returned null");
            var fullPath = Path.GetFullPath(path);
            spec.Steps = ExpandSteps(
                spec.Steps,
                fullPath,
                Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory(),
                new Stack<string>());
            return spec;
        }
        catch (JsonException ex)
        {
            throw new ScenarioLoadException(path, $"deserialization failed: {ex.Message}", ex);
        }
    }

    private static List<ScenarioStep> ExpandSteps(
        IEnumerable<ScenarioStep> steps,
        string sourcePath,
        string baseDirectory,
        Stack<string> includeStack)
    {
        var expanded = new List<ScenarioStep>();
        foreach (var step in steps)
        {
            var hasAction = !string.IsNullOrWhiteSpace(step.Action);
            var hasInclude = !string.IsNullOrWhiteSpace(step.Include);

            if (hasAction && hasInclude)
                throw new ScenarioLoadException(sourcePath, "step cannot specify both action and include");
            if (!hasAction && !hasInclude)
                throw new ScenarioLoadException(sourcePath, "step requires action or include");
            if (hasInclude && step.Args is not null)
                throw new ScenarioLoadException(sourcePath, "include step cannot specify args");

            if (!hasInclude)
            {
                expanded.Add(step);
                continue;
            }

            var includePath = ResolveIncludePath(baseDirectory, step.Include!);
            expanded.AddRange(LoadIncludedSteps(includePath, includeStack));
        }

        return expanded;
    }

    private static List<ScenarioStep> LoadIncludedSteps(string path, Stack<string> includeStack)
    {
        var fullPath = Path.GetFullPath(path);
        if (includeStack.Contains(fullPath))
        {
            var cycle = includeStack.Reverse().Append(fullPath).Select(Path.GetFileName);
            throw new ScenarioLoadException(fullPath, $"include cycle: {string.Join(" -> ", cycle)}");
        }

        if (!File.Exists(fullPath))
            throw new ScenarioLoadException(fullPath, "include file not found");

        string json;
        try { json = File.ReadAllText(fullPath); }
        catch (IOException ex) { throw new ScenarioLoadException(fullPath, $"include read failed: {ex.Message}", ex); }

        List<ScenarioStep>? steps;
        try { steps = JsonSerializer.Deserialize<List<ScenarioStep>>(json, ProtocolJson.Options); }
        catch (JsonException ex) { throw new ScenarioLoadException(fullPath, $"invalid include JSON: {ex.Message}", ex); }
        if (steps is null)
            throw new ScenarioLoadException(fullPath, "include deserialization returned null");

        includeStack.Push(fullPath);
        try
        {
            return ExpandSteps(
                steps,
                fullPath,
                Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory(),
                includeStack);
        }
        finally
        {
            includeStack.Pop();
        }
    }

    private static string ResolveIncludePath(string baseDirectory, string include)
        => Path.IsPathFullyQualified(include)
            ? include
            : Path.Combine(baseDirectory, include);
}
