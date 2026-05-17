# MCP Static Resources And Prompts Design

## Purpose

Add the first non-tool MCP surface to Frobby so coding agents can discover core
documentation, scenario context, and workflow prompts through protocol-native
`resources/*` and `prompts/*` methods instead of guessing local filesystem paths.

This slice intentionally stays static and read-only. It does not add
subscriptions, resource templates, dynamic report resources, or prompt list-change
notifications.

## Protocol Shape

Frobby continues to advertise MCP protocol version `2024-11-05`. The initialize
result declares:

- `tools: {}`
- `resources: {}`
- `prompts: {}`

Supported new methods:

- `resources/list`
- `resources/read`
- `prompts/list`
- `prompts/get`

Unsupported resource subscription/template methods continue returning normal
method-not-found errors until a later slice needs them.

## Resources

Resources use a Frobby-owned URI scheme so clients do not need to know local
checkout paths:

- `frobby://docs/wiki/index`
- `frobby://docs/wiki/examples`
- `frobby://docs/rpc-schema`
- `frobby://docs/mcp-quickstart`
- `frobby://scenarios/list`

Document resources read Markdown files from the current process working directory.
Missing optional docs should return an invalid-params JSON-RPC error, not crash the
server.

`frobby://scenarios/list` returns a small Markdown index of `*.test.json` files under
`tests/sdv` when that directory exists. If no scenario directory exists, it returns
a useful empty-state message. The resource is intentionally an index, not arbitrary
file access.

## Prompts

Prompts are workflow templates for user-selected actions:

- `create_scenario`: guide an agent to add a new Frobby scenario for a mod behavior.
- `debug_failed_scenario`: guide an agent through report-first failure diagnosis.
- `add_mod_ui_coverage`: guide an agent through click-first, draw-call-first mod UI
  coverage.
- `explain_available_tools`: summarize how to use Frobby MCP tools/resources/prompts.

Prompt responses use MCP prompt messages with `role: "user"` and text content. They
may embed resource URIs in prose, but this first slice does not return embedded
resource content blocks.

Prompt arguments are minimal:

- `create_scenario`: optional `mod_name`, `behavior`, `scenario_dir`.
- `debug_failed_scenario`: optional `report_dir`, `scenario_name`.
- `add_mod_ui_coverage`: optional `mod_name`, `panel_or_menu`.
- `explain_available_tools`: no arguments.

Unknown prompt names and malformed arguments return invalid-params errors.

## Testing

Add MCP server tests for:

- initialize advertises `resources` and `prompts`.
- `resources/list` includes the static resource descriptors.
- `resources/read` returns Markdown text for a known doc resource.
- `resources/read` returns a scenario index resource with a stable empty/non-empty
  shape.
- unknown resource URIs return JSON-RPC invalid-params.
- `prompts/list` includes all four prompts with argument metadata.
- `prompts/get` returns workflow text and includes supplied arguments.
- unknown prompt names return JSON-RPC invalid-params.

Run MCP tests, runner tests that cover scenario loading, and a full solution build.
