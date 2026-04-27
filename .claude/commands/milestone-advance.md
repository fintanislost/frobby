---
description: Update milestone tracking after completing a deliverable
---

# /milestone-advance — Update milestone state

Call this after completing a deliverable. You're going to:

1. Read @docs/milestones/current.md to see the active milestone.
2. Ask the user which deliverable just completed (if not obvious from recent context).
3. Check the box in the appropriate `docs/milestones/M<N>-*.md` file.
4. Update `docs/milestones/current.md` status section.
5. If all deliverables in the current milestone are checked:
   - Propose promoting the next milestone to current
   - Draft a short "milestone complete" summary for the commit/PR description
   - Remind the user to tag the commit (e.g., `m1-complete`)
6. If the milestone has a dependency on a spike, confirm the spike's REPORT.md has a recommendation section filled in.

Do not mark deliverables complete unless:
- The code is merged (or in an open PR)
- Tests pass in CI
- Docs are updated
- The milestone's exit criteria for this deliverable are met

If unsure, ask.
