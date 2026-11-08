---
name: test-flakiness
description: "Find flaky headless tests: runs the suites several times (or reads saved logs), tabulates per-suite and per-check pass rates with scripts/flaky_stats.sh, separates flaky from broken from environment problems, and recommends a fix. Use when a suite 'sometimes fails', before blaming a change for a red run, or for '¿qué tests son inestables?'."
argument-hint: "[--runs=N] [suite-filter] | [log-dir ...]"
---

# /test-flakiness

A flaky test passes and fails with no code change. It trains everyone to ignore red runs.
Known offenders (CLAUDE.md): `SkillReworkTest`'s `SPARK-PIERCES-ONE`, and `CoopRealityTest`
crashing with `Handle is not initialized` on the .NET 8 runtime.

## 1. Collect runs

- Default: `./scripts/flaky_stats.sh --runs=5 [filter]` — runs `run_all_tests.sh` five times,
  each into its own log dir, then prints the table. In cloud sessions wrap it in the .NET 8
  recipe from CLAUDE.md (move `global.json` aside, **put it back**). The full suite takes long:
  with no filter, ask before going past 3 runs; prefer filtering to the suspect suites.
- Saved logs: `./scripts/flaky_stats.sh DIR1 DIR2 …` (one dir per run, `<Suite>.log` files).
- Run in its own shell call, never chained with `"$GODOT_BIN" --headless` (the runner kills it).

## 2. Read the table

The script gives, per suite (by its `RESULT:` line — a missing line counts as a fail) and per
check (`NAME: PASS|FAIL` lines), runs, passes, fails and the pattern in run order. It emits
observations; the classification is yours:

| Pattern | Means |
|---|---|
| all `F` | **Broken**, not flaky — a real failure (or a dead C# run: check the log has output) |
| mixed `P`/`F`, same check fails | **Flaky check** — timing, randomness, order |
| suite `F` with no check failing | **Infrastructure** — crash, timeout, hang, no RESULT line |
| fails only from run N on | **Leaked state** — leftover processes, user:// files, ports |

## 3. Diagnose each flaky item

Open the failing logs side by side with a passing one and look for: unseeded `GD.Randi`/
`Random` (seed it in the test), frame-count or wall-clock waits (wait on a condition instead),
physics/navigation not settled, ports in use (co-op 7790–8190), leftover Godot processes,
`user://` files from a previous run, the 8-runtime gchandle crash, CPU starvation (idle
`VBCSCompiler` — the runner shuts it down).

## 4. Recommend

Per item: cause (or "unknown, evidence: …"), fix (with file:line), and how to prove the fix
(rerun N times, all `P`). **Never skip, disable or loosen a check to make it green**; if a fix
must wait, record it in CLAUDE.md's known-flaky notes so a red run isn't misread. Report in
chat; with the owner's ok, save to `docs/devlog/journal.md` as a short entry.

Adapted from Claude Code Game Studios (MIT, `.claude/THIRD_PARTY.md`).
