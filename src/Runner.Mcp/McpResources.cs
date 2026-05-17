using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SdvTestFramework.Protocol;

namespace SdvTestFramework.Runner.Mcp;

internal static class McpResources
{
    private const string Markdown = "text/markdown";

    private static readonly ResourceDescriptor[] Resources =
    [
        new(
            "frobby://docs/wiki/index",
            "Frobby Wiki Hub",
            "Task-oriented documentation hub for Frobby agents and mod developers.",
            Markdown,
            "docs/wiki/index.md"),
        new(
            "frobby://docs/wiki/examples",
            "Frobby Scenario Examples",
            "Pointers to real Starberg and SVE scenarios grouped by testing pattern.",
            Markdown,
            "docs/wiki/examples.md"),
        new(
            "frobby://docs/rpc-schema",
            "Frobby RPC Schema",
            "JSON-RPC method and scenario action reference.",
            Markdown,
            "docs/rpc-schema.md"),
        new(
            "frobby://docs/mcp-quickstart",
            "Frobby MCP Quickstart",
            "Setup and usage guide for the Frobby MCP server.",
            Markdown,
            "docs/mcp-quickstart.md"),
        new(
            "frobby://scenarios/list",
            "Frobby Scenario Index",
            "Markdown index of repo-local tests/sdv scenario files when present.",
            Markdown,
            RelativePath: null),
    ];

    public static JsonElement BuildListResult()
        => JsonSerializer.SerializeToElement(new
        {
            resources = Resources.Select(r => new
            {
                uri = r.Uri,
                name = r.Name,
                description = r.Description,
                mimeType = r.MimeType,
            }),
        }).Clone();

    public static bool TryRead(JsonElement? parameters, out JsonElement result, out JsonRpcError? error)
    {
        result = default;
        error = null;

        if (parameters is not { ValueKind: JsonValueKind.Object } p ||
            !p.TryGetProperty("uri", out var uriElement) ||
            uriElement.ValueKind != JsonValueKind.String)
        {
            error = McpError.InvalidParams("'uri' is required");
            return false;
        }

        var uri = uriElement.GetString()!;
        var descriptor = Resources.FirstOrDefault(r => r.Uri == uri);
        if (descriptor is null)
        {
            error = McpError.InvalidParams($"unknown resource URI: {uri}");
            return false;
        }

        var root = FindRepoRoot();
        var text = descriptor.Uri == "frobby://scenarios/list"
            ? BuildScenarioIndex(root)
            : ReadMarkdownResource(root, descriptor, out error);

        if (error is not null)
            return false;

        result = JsonSerializer.SerializeToElement(new
        {
            contents = new[]
            {
                new
                {
                    uri = descriptor.Uri,
                    mimeType = descriptor.MimeType,
                    text,
                },
            },
        }).Clone();
        return true;
    }

    private static string ReadMarkdownResource(string root, ResourceDescriptor descriptor, out JsonRpcError? error)
    {
        error = null;
        var relativePath = descriptor.RelativePath;
        if (relativePath is null)
        {
            error = McpError.InvalidParams($"resource has no backing file: {descriptor.Uri}");
            return "";
        }

        var fullPath = Path.Combine(root, relativePath);
        if (!File.Exists(fullPath))
        {
            error = McpError.InvalidParams($"resource file not found: {descriptor.Uri}");
            return "";
        }

        return File.ReadAllText(fullPath);
    }

    private static string BuildScenarioIndex(string root)
    {
        var scenarioDir = Path.Combine(root, "tests", "sdv");
        var sb = new StringBuilder();
        sb.AppendLine("# Frobby Scenario Index");
        sb.AppendLine();

        if (!Directory.Exists(scenarioDir))
        {
            sb.AppendLine("No `tests/sdv` scenario directory was found for this checkout.");
            return sb.ToString();
        }

        var files = Directory.EnumerateFiles(scenarioDir, "*.test.json", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            sb.AppendLine("The `tests/sdv` directory exists, but no `*.test.json` scenarios were found.");
            return sb.ToString();
        }

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file);
            sb.Append("- `");
            sb.Append(relative.Replace(Path.DirectorySeparatorChar, '/'));
            sb.AppendLine("`");
        }

        return sb.ToString();
    }

    private static string FindRepoRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "README.md")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "docs")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return Directory.GetCurrentDirectory();
    }

    private sealed record ResourceDescriptor(
        string Uri,
        string Name,
        string Description,
        string MimeType,
        string? RelativePath);
}
