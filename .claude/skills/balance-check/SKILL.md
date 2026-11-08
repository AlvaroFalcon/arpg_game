---
name: balance-check
description: "Check game balance against docs/design/balance-testing.md's targets — skill DPS outliers, gear/tree/gem factors, time to kill per tier, degenerate combos, economy faucets vs sinks, progression dead zones. Uses the damage matrix CSVs and the autoplay bot. Use for 'check balance', 'is X overpowered?', 'revisa el balance', or after a change to skills, trees, affixes or monster scaling."
argument-hint: "[combat|skill <id>|class <id>|economy|progression|path-to-csv]"
---

# /balance-check

Reports whether the numbers in the game meet the targets the owner set. It never edits
numbers by itself: it reports, then proposes.

## Rule zero: no data, no verdict

List every input the check needs and mark it `FOUND` or `ABSENT`. A section whose input is
absent is **`NOT ASSESSED — NO DATA`**, naming what was missing and the command that produces
it. Never estimate it, never fill it from an adjacent number. A scan that found nothing because
there was nothing to scan has verified nothing — say which of the two happened.
"Modelled" (from a formula) and "measured" (from a run) are always labelled.

## 1. Domain

From the argument: **combat** (default), **skill <id>**, **class <id>**, **economy**,
**progression**, or a CSV path (infer the domain from its header).

## 2. Targets

Read `docs/design/balance-testing.md` ("Targets" and the latest "First run"/results sections):
DPS ÷ `ExpectedDps(level)` per gear profile, gear factor ≥ 4×, tree factor 1.3–1.8×, gem level
factor, ±25 % of the group median (single-target and area groups separate), pair interaction
≤ 1.6×, the gearless tier-20 rule. Also `scripts/World/MonsterScaling.cs` (`ExpectedDpsTable`,
`TtkNormal`/`TtkRare`, rarity life). For economy: `docs/design/economy.md` if it exists, else
`crafting.md`/`items.md` — with no stated target, a value is listed as `no target — not judged`.

## 3. Data

- **Committed:** `docs/design/balance/matrix_<class>_t<tier>.csv` (check the date in the doc
  against `git log` of the skill/tree files: a CSV older than the last change to
  `scripts/Skills/` or the tree data is **stale** — say so, and offer to re-run).
- **Fresh:** build C# (CLAUDE.md), then
  `./scripts/autoplay.sh --mode=matrix --class=<c> --tier=20 [--skills=…] [--gear=none,ascendancy,typical] [--trees=none,best]`
  → `user://balance/matrix_<class>_t<tier>.csv` (Linux: `~/.local/share/godot/app_userdata/ARPG Game/`).
  Real play: `./scripts/autoplay.sh --mode=reality --class=<c> --tier=<t> --gear=typical|none --runs=6`
  (`ttk` lines, clear/collapse). These take minutes to an hour: ask before a full matrix.
- **Economy:** drop tables (`scripts/Items/DropTables.cs`), vendor/crafting costs, and reality
  runs' loot from the bot log.

## 4. Analysis

- **Combat:** ratio to expected per skill × gear × tree; group medians and outliers; the three
  factors; gearless tier 20 must not clear comfortably; DoT share; mana sustainability
  (`mana_per_s` vs regen); area skills judged on pack DPS/clear time.
- **Interactions:** pairs/auras/curses that multiply (more × more) — from the matrix's pairs
  mode if run, else listed as candidates to measure.
- **Progression:** time to kill by level/tier for the reference character; jumps (> 1.5× between
  adjacent tiers) and dead zones (no power gain over several levels).
- **Economy:** faucets vs sinks per currency, per hour of play, at tier 1/10/20; buy→sell or
  craft→disenchant loops; currencies with nothing worth buying.

## 5. Report

Write `docs/design/balance/check-YYYY-MM-DD-<domain>.md` (ask first) and summarise in chat:

```
# Balance check — <domain> — <date>
Data: <file, date, measured|modelled> ... (ABSENT items listed)
Verdict: BALANCED | ISSUES FOUND | NOT ASSESSED — NO DATA
## Outliers (value, target, ratio, source)
## Factors (gear / tree / gem — target vs measured)
## Degenerate strategies / interactions
## Not judged (no target)
## Proposed changes (constant: old → new, what it moves, regression guard)
```

Proposals go to the owner; decided changes are applied by systems-designer/economy-designer
and recorded in `balance-testing.md`. Adapted from Claude Code Game Studios (MIT,
`.claude/THIRD_PARTY.md`).
