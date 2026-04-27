---
description: Scaffold a new spike investigation with structure and report template
argument-hint: <spike-topic>
---

# /spike — Start a new spike investigation

You're going to scaffold a spike investigation for: **$ARGUMENTS**

Spikes are time-boxed learning exercises. They are **not** production code. The goal is to answer a question or de-risk an unknown.

## Steps

1. Create directory `docs/spikes/YYYY-MM-<slug>/` where slug is a kebab-case version of the topic.
2. Inside, create `REPORT.md` using this structure:

```markdown
# Spike: <topic>

**Started:** YYYY-MM-DD
**Time box:** <N> days
**Related milestone:** <M0/M1/...>

## Question

<What we're trying to learn. One paragraph.>

## Hypothesis

<What we expect to find, and why.>

## Approach

<How we'll investigate. Bullet list.>

## Findings

_(fill in as we go)_

## Recommendation

_(fill in at end: proceed / pivot / escalate)_

## Artifacts

<Links to scratch code, logs, screenshots generated during the spike.>
```

3. Create `scratch/` subdirectory for throwaway code.
4. Ask the user to confirm the time box and the specific question before you start investigating.

Do not write production code during a spike. All scratch code lives in the spike directory and stays there unless explicitly promoted.
