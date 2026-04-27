#!/usr/bin/env bash
# Claude Code setup for this project.
# Run once after cloning. Idempotent.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$REPO_ROOT"

echo "==> Setting up Claude Code for SDV test framework"
echo ""

# Verify Claude Code is installed
if ! command -v claude &> /dev/null; then
  echo "ERROR: 'claude' CLI not found. Install Claude Code first:"
  echo "  https://docs.claude.com/claude-code"
  exit 1
fi

echo "==> Claude Code found: $(claude --version 2>/dev/null || echo 'version check unavailable')"
echo ""

cat <<'EOF'
==> Plugin setup

The following plugins need to be installed in Claude Code. They're not
installed automatically because /plugin commands run inside Claude Code,
not from a shell. Launch Claude Code in this directory, then run:

  /plugin marketplace add obra/superpowers-marketplace
  /plugin install superpowers@superpowers-marketplace

Optional but recommended:

  /plugin install frontend-design@claude-plugins-official
    # Useful if/when you build the docs site in SvelteKit

  /plugin install commit-commands@claude-plugins-official
    # Standardized commit/PR helpers that complement our commit-style rule

After installing, restart Claude Code. You should see the Superpowers
session-start banner injected. That's how you know it's working.

==> Verification

After plugins are installed, in Claude Code run:

  /help

You should see these new commands available:
  /superpowers:brainstorm
  /superpowers:write-plan
  /superpowers:execute-plan
  /spike            (from this repo)
  /harmony-patch    (from this repo)
  /milestone-advance (from this repo)
  /scenario         (from this repo)

EOF

echo "==> Directory structure check"
REQUIRED_PATHS=(
  "CLAUDE.md"
  ".claude/rules/tdd.md"
  ".claude/rules/harmony-patching.md"
  ".claude/rules/sdv-conventions.md"
  ".claude/rules/commit-style.md"
  ".claude/rules/determinism.md"
  ".claude/rules/draw-call-recorder.md"
  ".claude/rules/fixtures.md"
  ".claude/rules/ci-integration.md"
  ".claude/agents/spike-runner.md"
  ".claude/agents/reviewer.md"
  ".claude/agents/sdv-expert.md"
  ".claude/commands/spike.md"
  ".claude/commands/harmony-patch.md"
  ".claude/commands/milestone-advance.md"
  ".claude/commands/scenario.md"
  ".mcp.json"
  "docs/milestones/current.md"
  "docs/milestones/M0-spike.md"
  "docs/milestones/M1-core.md"
  "docs/milestones/M2-polish.md"
  "docs/milestones/M3-ecosystem.md"
  "docs/spec.md"
  "docs/developer-setup.md"
  "docs/rpc-schema.md"
  "docs/patches.md"
  "docs/open-questions.md"
)

MISSING=0
for p in "${REQUIRED_PATHS[@]}"; do
  if [[ ! -f "$p" ]]; then
    echo "  MISSING: $p"
    MISSING=$((MISSING + 1))
  fi
done

if [[ $MISSING -eq 0 ]]; then
  echo "  All $(echo "${#REQUIRED_PATHS[@]}") expected files present."
else
  echo ""
  echo "WARNING: $MISSING expected files are missing. Setup may be incomplete."
  exit 1
fi

echo ""
echo "==> Git hooks (optional)"
if [[ -d .git ]]; then
  HOOK_DIR=".git/hooks"
  if [[ ! -f "$HOOK_DIR/pre-commit" ]]; then
    echo "  No pre-commit hook installed. Consider adding one that runs the"
    echo "  'reviewer' subagent before commits. Template:"
    echo ""
    echo "    cat > $HOOK_DIR/pre-commit <<'HOOK'"
    echo "    #!/usr/bin/env bash"
    echo "    # Triggers Claude Code reviewer subagent if claude is in PATH"
    echo "    # Skip with: git commit --no-verify"
    echo "    command -v claude >/dev/null && claude review || true"
    echo "    HOOK"
    echo "    chmod +x $HOOK_DIR/pre-commit"
  fi
else
  echo "  Not a git repo; skipping hook suggestions."
fi

echo ""
echo "==> Setup complete"
echo ""
echo "Next steps:"
echo "  1. Launch Claude Code in this directory: claude"
echo "  2. Install Superpowers (see plugin setup above)"
echo "  3. Read CLAUDE.md if you want to understand the project conventions"
echo "  4. Start M0: 'begin the determinism spike following docs/milestones/M0-spike.md'"
echo ""
