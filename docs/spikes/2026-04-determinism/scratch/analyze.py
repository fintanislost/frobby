#!/usr/bin/env python3
"""Normalize two draw-event JSONL captures and diff them.

Per-run `tex_ref` is a RuntimeHelpers.GetHashCode of a Texture2D object — stable within
a process, meaningless across processes. We rewrite it to a deterministic ordinal based
on first-seen order (which is itself determined by SDV's content-pipeline load order,
which is what we want to pin).

Usage: analyze.py run1.jsonl run2.jsonl

Exits:
  0 — byte-identical after normalization
  1 — content diverges
  2 — input malformed
"""
from __future__ import annotations

import json
import sys
from typing import Any


def normalize(path: str) -> list[dict[str, Any]]:
    """Normalize a JSONL capture for cross-run comparison.

    Per-run-varying fields dropped or rewritten:
      - tex_ref: renumbered to stable ordinals (first-seen order across the stream).
      - tick: rewritten relative to the first draw's tick, so `Game1.ticks` offset
        at capture-start doesn't cause diffs.
      - meta.dropped: omitted; it's a side effect of the ring-buffer filling, which
        depends on whether capture started one tick earlier.
      - meta.reason: omitted; differs by disarm path (manual vs tick-budget).
    """
    events: list[dict[str, Any]] = []
    ref_map: dict[int, int] = {}
    next_ref_id = 0
    tick_origin: int | None = None
    with open(path, encoding="utf-8") as f:
        for lineno, line in enumerate(f, 1):
            line = line.strip()
            if not line:
                continue
            try:
                obj = json.loads(line)
            except json.JSONDecodeError as e:
                print(f"{path}:{lineno}: malformed JSON: {e}", file=sys.stderr)
                sys.exit(2)
            if obj.get("type") == "meta":
                events.append({"type": "meta",
                               "ticks": obj["ticks"],
                               "events": obj["events"]})
                continue
            if obj.get("type") != "draw":
                print(f"{path}:{lineno}: unknown type {obj.get('type')!r}", file=sys.stderr)
                sys.exit(2)

            if tick_origin is None:
                tick_origin = obj["tick"]
            obj["tick"] = obj["tick"] - tick_origin

            raw_ref = obj["tex_ref"]
            if raw_ref not in ref_map:
                ref_map[raw_ref] = next_ref_id
                next_ref_id += 1
            obj["tex_ref"] = ref_map[raw_ref]
            events.append(obj)
    return events


def divergence_summary(a: list[dict[str, Any]], b: list[dict[str, Any]]) -> list[int]:
    """Return all indices where a[i] != b[i]. Extra events from longer side are
    counted as divergences at the trailing indices."""
    diffs: list[int] = []
    for i in range(max(len(a), len(b))):
        x = a[i] if i < len(a) else None
        y = b[i] if i < len(b) else None
        if x != y:
            diffs.append(i)
    return diffs


def diff_fields(x: dict[str, Any], y: dict[str, Any]) -> list[str]:
    """Return a list of "key: x_val != y_val" strings for fields that differ."""
    keys = set(x.keys()) | set(y.keys())
    out = []
    for k in sorted(keys):
        if x.get(k) != y.get(k):
            out.append(f"{k}: {x.get(k)!r} != {y.get(k)!r}")
    return out


def main() -> int:
    if len(sys.argv) != 3:
        print("usage: analyze.py run1.jsonl run2.jsonl", file=sys.stderr)
        return 2

    a = normalize(sys.argv[1])
    b = normalize(sys.argv[2])

    diffs = divergence_summary(a, b)
    if not diffs:
        print(f"identical: {len(a)} events (post-normalization)")
        return 0

    total = max(len(a), len(b))
    print(f"diverge: {len(diffs)} / {total} events differ ({100*len(diffs)/total:.2f}%)")
    print(f"lengths: len(a)={len(a)} len(b)={len(b)}")
    print()

    # Show first 10 divergences and their field-level deltas.
    print("=== first up to 10 divergences ===")
    for idx in diffs[:10]:
        x = a[idx] if idx < len(a) else None
        y = b[idx] if idx < len(b) else None
        print(f"[{idx}]")
        if x is None or y is None:
            print(f"  A: {x}")
            print(f"  B: {y}")
        else:
            for f in diff_fields(x, y):
                print(f"  {f}")

    # Tally which fields are most commonly different — hints at the root-cause category.
    print()
    print("=== field-level divergence tallies ===")
    tally: dict[str, int] = {}
    for idx in diffs:
        x = a[idx] if idx < len(a) else {}
        y = b[idx] if idx < len(b) else {}
        if not isinstance(x, dict) or not isinstance(y, dict):
            tally["[extra event]"] = tally.get("[extra event]", 0) + 1
            continue
        keys = set(x.keys()) | set(y.keys())
        for k in keys:
            if x.get(k) != y.get(k):
                tally[k] = tally.get(k, 0) + 1
    for k, v in sorted(tally.items(), key=lambda kv: (-kv[1], kv[0])):
        print(f"  {k}: {v}")
    return 1


if __name__ == "__main__":
    sys.exit(main())
