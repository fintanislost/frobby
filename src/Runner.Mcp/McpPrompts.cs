using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SdvTestFramework.Protocol;

namespace SdvTestFramework.Runner.Mcp;

internal static class McpPrompts
{
    private static readonly PromptDescriptor[] Prompts =
    [
        new(
            "create_scenario",
            "Create Frobby Scenario",
            "Guide an agent through adding a scenario for a mod behavior.",
            [
                new("mod_name", "Name of the mod under test."),
                new("behavior", "Behavior or workflow the scenario should cover."),
                new("scenario_dir", "Scenario directory, such as tests/sdv."),
            ]),
        new(
            "debug_failed_scenario",
            "Debug Failed Frobby Scenario",
            "Guide report-first diagnosis of a failed scenario.",
            [
                new("report_dir", "Path to the report directory or hub."),
                new("scenario_name", "Scenario name to investigate."),
            ]),
        new(
            "add_mod_ui_coverage",
            "Add Mod UI Coverage",
            "Guide click-first, draw-call-first UI coverage for a Stardew mod.",
            [
                new("mod_name", "Name of the mod under test."),
                new("panel_or_menu", "Panel or menu to cover."),
            ]),
        new(
            "explain_available_tools",
            "Explain Available Frobby MCP Tools",
            "Summarize the server's tools, resources, and prompts.",
            []),
    ];

    public static JsonElement BuildListResult()
        => JsonSerializer.SerializeToElement(new
        {
            prompts = Prompts.Select(p => new
            {
                name = p.Name,
                description = p.Description,
                arguments = p.Arguments.Select(a => new
                {
                    name = a.Name,
                    description = a.Description,
                    required = false,
                }),
            }),
        }).Clone();

    public static bool TryGet(JsonElement? parameters, out JsonElement result, out JsonRpcError? error)
    {
        result = default;
        error = null;

        if (parameters is not { ValueKind: JsonValueKind.Object } p ||
            !p.TryGetProperty("name", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String)
        {
            error = McpError.InvalidParams("'name' is required");
            return false;
        }

        var name = nameElement.GetString()!;
        var prompt = Prompts.FirstOrDefault(p => p.Name == name);
        if (prompt is null)
        {
            error = McpError.InvalidParams($"unknown prompt: {name}");
            return false;
        }

        if (p.TryGetProperty("arguments", out var arguments) &&
            arguments.ValueKind != JsonValueKind.Object)
        {
            error = McpError.InvalidParams("'arguments' must be an object");
            return false;
        }

        var args = arguments.ValueKind == JsonValueKind.Object
            ? arguments
            : default(JsonElement);
        var text = BuildPromptText(prompt.Name, args);

        result = JsonSerializer.SerializeToElement(new
        {
            description = prompt.Title,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new
                    {
                        type = "text",
                        text,
                    },
                },
            },
        }).Clone();
        return true;
    }

    private static string BuildPromptText(string name, JsonElement args)
        => name switch
        {
            "create_scenario" => CreateScenarioPrompt(args),
            "debug_failed_scenario" => DebugFailedScenarioPrompt(args),
            "add_mod_ui_coverage" => AddModUiCoveragePrompt(args),
            "explain_available_tools" => ExplainAvailableToolsPrompt(),
            _ => throw new InvalidOperationException($"Unhandled prompt: {name}"),
        };

    private static string CreateScenarioPrompt(JsonElement args)
    {
        var modName = GetOptionalString(args, "mod_name") ?? "the mod";
        var behavior = GetOptionalString(args, "behavior") ?? "the target behavior";
        var scenarioDir = GetOptionalString(args, "scenario_dir") ?? "tests/sdv";

        return $"""
Create a Frobby JSON scenario for {modName} covering {behavior}.

Use these resources first:
- frobby://docs/wiki/index
- frobby://docs/wiki/examples
- frobby://docs/rpc-schema

Work in `{scenarioDir}`. Prefer player-like input, click-first UI actions, semantic
draw/text assertions, and frozen final screenshots. Keep mod-specific ids,
coordinates, and fixtures in the scenario file, not in Frobby source.
""";
    }

    private static string DebugFailedScenarioPrompt(JsonElement args)
    {
        var reportDir = GetOptionalString(args, "report_dir") ?? "the report directory";
        var scenarioName = GetOptionalString(args, "scenario_name") ?? "the failed scenario";

        return $"""
Debug {scenarioName} using Frobby's report-first workflow.

Start with {reportDir}. Inspect the summary, the failed scenario page, step
screenshots, final frozen screenshot, and assertion labels before changing the
scenario or mod code. Prefer fixing synchronization with waits or next-frame
captures before weakening assertions.

Useful resources:
- frobby://docs/mcp-quickstart
- frobby://docs/wiki/index
- frobby://docs/rpc-schema
""";
    }

    private static string AddModUiCoveragePrompt(JsonElement args)
    {
        var modName = GetOptionalString(args, "mod_name") ?? "the mod";
        var panelOrMenu = GetOptionalString(args, "panel_or_menu") ?? "the target UI";

        return $"""
Add click-first Frobby UI coverage for {modName}'s {panelOrMenu}.

Use Frobby's UI testing conventions:
- drive UI with click or hover actions when possible;
- use draw/text assertions before bitmap assertions;
- add text-bounds assertions for fixed panes;
- capture path screenshots for debugging and a frozen final screenshot for validation.

Useful resources:
- frobby://docs/wiki/examples
- frobby://docs/rpc-schema
""";
    }

    private static string ExplainAvailableToolsPrompt()
        => """
Explain Frobby's MCP surface to the user.

Cover:
- tools for listing, scaffolding, running scenarios, capturing state, and raw RPC calls;
- resources for docs and scenario indexes;
- prompts for creating scenarios, debugging failures, and adding UI coverage.

Use frobby://docs/mcp-quickstart and frobby://docs/rpc-schema as reference.
""";

    private static string? GetOptionalString(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private sealed record PromptDescriptor(
        string Name,
        string Title,
        string Description,
        IReadOnlyList<PromptArgument> Arguments);

    private sealed record PromptArgument(string Name, string Description);
}
