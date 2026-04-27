#!/usr/bin/env bash
# Manual MCP-server smoke test.
# Pipes a few JSON-RPC requests to `sdv-test mcp` and asserts the response shapes.
#
# Usage: run from repo root with live SDV available (Xvfb + SDV install + Content Patcher).
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$REPO"

echo "==> Building"
dotnet build -c Release >/dev/null

echo "==> Sending MCP requests"

RESP=$(cat <<'EOF' | timeout 60 dotnet run --project src/Runner -c Release --no-build -- mcp
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","clientInfo":{"name":"smoke"},"capabilities":{}}}
{"jsonrpc":"2.0","id":2,"method":"tools/list"}
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_scenarios","arguments":{"dir":"tests/samples"}}}
EOF
)

echo "==> Responses:"
echo "$RESP"

echo "$RESP" | grep -q '"protocolVersion":"2024-11-05"' || { echo "FAIL: initialize missing protocolVersion"; exit 1; }
echo "$RESP" | grep -q '"name":"run_scenario"' || { echo "FAIL: tools/list missing run_scenario"; exit 1; }
echo "$RESP" | grep -q '11-bitmap-basic' || { echo "WARN: list_scenarios didn't find the bitmap smoke scenario (tests/samples may be empty)"; }

echo "==> manual-smoke.sh PASSED"
