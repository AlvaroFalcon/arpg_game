---
name: soak-test
description: "Long-running soak test: plays many realities / long Rift runs back to back with the autoplay bot and watches for memory growth, frame-time drift, GC spikes, node-count leaks and state that breaks after N repetitions. Use before a release, after changes to scene loading, pooling, loot or networking, or for 'prueba larga', 'memory leak', 'se vuelve lento con el tiempo'."
argument-hint: "[reality|rift|coop] [--minutes=60]"
---

# /soak-test

A smoke test proves the critical path works once; a soak test proves it still works after an
hour. Leaks, drift and "after the 40th reality" bugs only show up this way.

## 1. Plan (tell the user before starting — these take a long time)

| Mode | Command (build C# first) | Exercises |
|---|---|---|
| reality | `./scripts/autoplay.sh --mode=reality --class=<c> --tier=10 --runs=30` | scene change hideout ↔ reality, cave generation, loot, pooling |
| rift | `./scripts/autoplay.sh --runs=10 --report=30` (+ `--rift-tier`, endless) | long single scene, horde peaks, evolutions |
| coop | `scenes/tools/CoopBench.tscn -- --clients=3 --run-time=1800` (`--mode=reality`) | replication, bandwidth over time |
| frame budget | `./scripts/perf_bench.sh --mode=rift|reality --reps=N` | timings per run |

Pick ~60 minutes of game time unless the user says otherwise. Run in the background and poll
the log; stdout is block-buffered, so read `user://autoplay.log` / `user://bench/` files
(CLAUDE.md), not the pipe.

## 2. What to record (sample every reality / every 60 s)

- **Memory:** managed heap (`System.GC.GetGCMemoryInfo`, already logged on spikes by
  `AutoplayBot.Reality.cs`), `Performance.Monitor.MemoryStatic`, process RSS (`ps -o rss`).
- **Objects:** `ObjectNodeCount`, `ObjectCount`, orphan nodes (`ObjectOrphanNodeCount`),
  enemies/ground labels alive between realities (should return to the hideout baseline).
- **Time:** frame ms p50/p95/max per window, `[BOT] spike` lines and whether gen2 caused them.
- **Correctness:** each reality cleared/collapsed as expected, no `ERROR`/`FATAL`/exceptions,
  save file still loads (`Profile` untouched with in-memory characters).

If a counter isn't logged yet, add a temporary `[SOAK]` log line in the bot (keep it behind a
`--soak` flag if you commit it) rather than guessing.

## 3. Judge

Compare the first 10 % of samples with the last 10 %:
- heap / RSS / node count growing roughly linearly with realities → **leak** (find what
  survives a scene change: signals to autoloads, static lists, `GameEvents` handlers not
  removed, pooled objects never returned);
- p95 frame time drifting > 20 % → **drift**;
- a failure that only appears after N repetitions → **state accumulation** (log N).

Verdict: `STABLE` / `LEAK SUSPECTED` / `DRIFT` / `NOT ASSESSED — <why>` (e.g. the run died early).
Report numbers, a small table of first-vs-last, and the suspect with file:line. Hand fixes to
performance-expert / godot-expert; append a journal entry when something was found.

Adapted from Claude Code Game Studios (MIT, `.claude/THIRD_PARTY.md`).
