---
description: Write a new Harmony patch following project conventions
argument-hint: <target-method>
---

# /harmony-patch — Author a Harmony patch

You're going to write a Harmony patch targeting: **$ARGUMENTS**

## Before writing any code

1. Load @.claude/rules/harmony-patching.md
2. Load @.claude/rules/sdv-conventions.md
3. Confirm the target method exists in the current SDV/SMAPI version:
   - Check SMAPI's public API if possible
   - If targeting a private/internal method, flag this as needing extra review
4. Determine patch type (prefix / postfix / transpiler). Default to prefix for observation, postfix for return inspection. Transpilers require user approval.

## Required output

The patch must include:

- Full header comment block per rules/harmony-patching.md
- Null assertion at registration (`MethodInfo != null`)
- Unit or integration test covering it
- Entry in `docs/patches.md` listing all active patches

## After writing

1. Write the test first. Confirm it fails.
2. Write the patch.
3. Confirm the test passes.
4. Run the full integration suite to check for regressions against other patches.
5. Update `docs/patches.md`.

If the target method can't be resolved, stop and ask. Don't widen the signature matcher.
