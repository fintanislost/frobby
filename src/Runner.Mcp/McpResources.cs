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
    private const string Json = "application/json";
    private const string Html = "text/html";

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
        new(
            "frobby://reports/latest/summary",
            "Latest Frobby Report Summary",
            "JSON summary for the latest report known to this MCP server process.",
            Json,
            RelativePath: null),
        new(
            "frobby://reports/latest/index",
            "Latest Frobby Report Index",
            "HTML index for the latest static report when an index.html artifact exists.",
            Html,
            RelativePath: null),
        new(
            "frobby://reports/latest/scenarios",
            "Latest Frobby Report Scenarios",
            "Markdown scenario summary for the latest report known to this MCP server process.",
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

    public static bool TryRead(
        JsonElement? parameters,
        McpReportRegistry reports,
        out JsonElement result,
        out JsonRpcError? error)
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

        string text;
        if (descriptor.Uri.StartsWith("frobby://reports/latest/", StringComparison.Ordinal))
        {
            text = ReadLatestReportResource(descriptor, reports, out error);
        }
        else
        {
            var root = FindRepoRoot();
            text = descriptor.Uri == "frobby://scenarios/list"
                ? BuildScenarioIndex(root)
                : ReadMarkdownResource(root, descriptor, out error);
        }

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

    private static string ReadLatestReportResource(
        ResourceDescriptor descriptor,
        McpReportRegistry reports,
        out JsonRpcError? error)
    {
        error = null;
        if (!reports.TryGetLatestReport(out var report))
        {
            error = McpError.InvalidParams("no latest report is available in this MCP server process");
            return "";
        }

        return descriptor.Uri switch
        {
            "frobby://reports/latest/summary" => ReadLatestSummary(report, out error),
            "frobby://reports/latest/index" => ReadLatestIndex(report, out error),
            "frobby://reports/latest/scenarios" => BuildLatestScenarioIndex(report, out error),
            _ => UnknownDynamicResource(descriptor.Uri, out error),
        };
    }

    private static string ReadLatestSummary(McpReportSnapshot report, out JsonRpcError? error)
    {
        if (!string.IsNullOrWhiteSpace(report.SummaryJson))
            return ValidateJsonText(report.SummaryJson!, "latest in-memory report summary", out error);

        var path = Path.Combine(report.ReportDir, "summary.json");
        if (!File.Exists(path))
        {
            error = McpError.InvalidParams($"latest report summary not found: {path}");
            return "";
        }

        var text = ReadTextFile(path, out error);
        return error is null ? ValidateJsonText(text, path, out error) : "";
    }

    private static string ReadLatestIndex(McpReportSnapshot report, out JsonRpcError? error)
    {
        var path = Path.Combine(report.ReportDir, "index.html");
        if (!File.Exists(path))
        {
            error = McpError.InvalidParams($"latest report index not found: {path}");
            return "";
        }

        return ReadTextFile(path, out error);
    }

    private static string BuildLatestScenarioIndex(McpReportSnapshot report, out JsonRpcError? error)
    {
        error = null;
        JsonDocument? summary = null;
        try
        {
            var summaryJson = !string.IsNullOrWhiteSpace(report.SummaryJson)
                ? report.SummaryJson
                : ReadOptionalSummaryJson(report.ReportDir, out error);
            if (error is not null)
                return "";

            if (!string.IsNullOrWhiteSpace(summaryJson))
                summary = JsonDocument.Parse(summaryJson);
        }
        catch (JsonException ex)
        {
            error = McpError.InvalidParams($"latest report summary is not valid JSON: {ex.Message}");
            return "";
        }

        using (summary)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Latest Frobby Report Scenarios");
            sb.AppendLine();
            sb.AppendLine($"Report directory: `{report.ReportDir}`");

            if (summary is not null &&
                summary.RootElement.ValueKind == JsonValueKind.Object &&
                summary.RootElement.TryGetProperty("run_id", out var runId) &&
                runId.ValueKind == JsonValueKind.String)
            {
                sb.AppendLine($"Run id: `{runId.GetString()}`");
            }

            sb.AppendLine();

            if (TryAppendSummaryScenarios(sb, report.ReportDir, summary?.RootElement) ||
                TryAppendFilesystemScenarios(sb, report.ReportDir))
            {
                return sb.ToString();
            }

            if (summary is not null &&
                summary.RootElement.ValueKind == JsonValueKind.Object &&
                summary.RootElement.TryGetProperty("passed", out var passed) &&
                passed.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                sb.AppendLine($"MCP run result: {(passed.GetBoolean() ? "PASS" : "FAIL")}");
                if (summary.RootElement.TryGetProperty("assertions_run", out var run) &&
                    run.TryGetInt32(out var assertionCount))
                {
                    sb.AppendLine($"Assertions run: `{assertionCount}`");
                }

                sb.AppendLine();
            }

            sb.AppendLine("No per-scenario static report pages were found for the latest report.");
            return sb.ToString();
        }
    }

    private static bool TryAppendSummaryScenarios(StringBuilder sb, string reportDir, JsonElement? root)
    {
        if (root is not { ValueKind: JsonValueKind.Object } summary ||
            !summary.TryGetProperty("scenarios", out var scenarios) ||
            scenarios.ValueKind != JsonValueKind.Array ||
            scenarios.GetArrayLength() == 0)
        {
            return false;
        }

        foreach (var scenario in scenarios.EnumerateArray())
        {
            if (scenario.ValueKind != JsonValueKind.Object)
                continue;

            var name = TryGetString(scenario, "name") ?? "(unnamed scenario)";
            var status = TryGetBool(scenario, "passed") is true ? "PASS" : "FAIL";
            var reportPath = FindScenarioReportPath(reportDir, name);
            sb.Append("- ");
            if (reportPath is not null)
            {
                var relative = Path.GetRelativePath(reportDir, reportPath)
                    .Replace(Path.DirectorySeparatorChar, '/');
                sb.Append('[').Append(name).Append("](").Append(relative).Append(')');
            }
            else
            {
                sb.Append('`').Append(name).Append('`');
            }

            sb.Append(" — ").Append(status);
            var scenarioPath = TryGetString(scenario, "path");
            if (!string.IsNullOrWhiteSpace(scenarioPath))
                sb.Append(" — `").Append(scenarioPath).Append('`');
            sb.AppendLine();
        }

        return true;
    }

    private static bool TryAppendFilesystemScenarios(StringBuilder sb, string reportDir)
    {
        var scenariosDir = Path.Combine(reportDir, "scenarios");
        if (!Directory.Exists(scenariosDir))
            return false;

        var reports = Directory.EnumerateFiles(scenariosDir, "report.html", SearchOption.AllDirectories)
            .OrderBy(Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (reports.Length == 0)
            return false;

        foreach (var reportPath in reports)
        {
            var scenarioName = Path.GetFileName(Path.GetDirectoryName(reportPath));
            var relative = Path.GetRelativePath(reportDir, reportPath)
                .Replace(Path.DirectorySeparatorChar, '/');
            sb.Append("- [").Append(scenarioName).Append("](").Append(relative).AppendLine(")");
        }

        return true;
    }

    private static string? FindScenarioReportPath(string reportDir, string scenarioName)
    {
        var direct = Path.Combine(reportDir, "scenarios", SanitizeName(scenarioName), "report.html");
        if (File.Exists(direct))
            return direct;

        var scenariosDir = Path.Combine(reportDir, "scenarios");
        if (!Directory.Exists(scenariosDir))
            return null;

        return Directory.EnumerateFiles(scenariosDir, "report.html", SearchOption.AllDirectories)
            .FirstOrDefault(path =>
                string.Equals(
                    Path.GetFileName(Path.GetDirectoryName(path)),
                    scenarioName,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static string SanitizeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }

    private static string? TryGetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? TryGetBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    private static string ReadOptionalSummaryJson(string reportDir, out JsonRpcError? error)
    {
        error = null;
        var path = Path.Combine(reportDir, "summary.json");
        return File.Exists(path) ? ReadTextFile(path, out error) : "";
    }

    private static string ValidateJsonText(string text, string source, out JsonRpcError? error)
    {
        error = null;
        try
        {
            using var _ = JsonDocument.Parse(text);
            return text;
        }
        catch (JsonException ex)
        {
            error = McpError.InvalidParams($"{source} is not valid JSON: {ex.Message}");
            return "";
        }
    }

    private static string ReadTextFile(string path, out JsonRpcError? error)
    {
        error = null;
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException ex)
        {
            error = McpError.InvalidParams($"could not read report artifact {path}: {ex.Message}");
            return "";
        }
        catch (UnauthorizedAccessException ex)
        {
            error = McpError.InvalidParams($"could not read report artifact {path}: {ex.Message}");
            return "";
        }
    }

    private static string UnknownDynamicResource(string uri, out JsonRpcError? error)
    {
        error = McpError.InvalidParams($"unknown resource URI: {uri}");
        return "";
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
