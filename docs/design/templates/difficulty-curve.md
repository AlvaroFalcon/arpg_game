# Difficulty curve: <mode — Altered Realities | Survival Rift | tutorial>

> Template for a difficulty doc (or a section of `endgame.md` / `run.md`). Adapted from
> Claude Code Game Studios (MIT, `.claude/THIRD_PARTY.md`). Delete the `>` guidance when
> filling it in. Owner: systems-designer with game-designer.

**Status:** draft | decided | implemented — **Date:** YYYY-MM-DD

## Philosophy
> One paragraph: what the player is allowed to feel and for how long. This game's base is PoE:
> difficulty is mostly opt-in (higher tiers, Fear cards), the build is the answer, and the
> gearless character must struggle (`balance-testing.md`). State what a death costs here.

## Axes of difficulty

| Axis | Driven by | Player controls it? | Where in code |
|---|---|---|---|
| Monster life / damage | tier → monster level (`MonsterScaling`) | yes (tier choice) | |
| Density and pack size | | | |
| Monster rarity mix / modifiers | | | |
| Boss mechanics | `bosses.md` | | |
| Time pressure (collapse, Rift clock) | | | |
| Optional modifiers (Fear cards, …) | | yes | |
| Decision load (shrines, level-up cards) | | | |

> An axis the player can't control is a forced challenge: use it on purpose.

## The curve

| Stage | Tier / time | Monster level | Expected DPS (reference) | TTK normal / rare / boss | Expected deaths | Intended feeling |
|---|---|---|---|---|---|---|
| Tutorial | | | | | 0 | |
| Early | tier 1–5 | | | | | |
| Mid | tier 6–14 | | | | | |
| Late | tier 15–20 | | | | | |
| Beyond (endless, Fear) | | | | | | |

> Numbers from `MonsterScaling.ExpectedDpsTable` and measured `ttk` lines
> (`autoplay.sh --mode=reality`). Mark modelled vs measured.

## Spikes and relief
> Where the curve is meant to jump (a boss, a new mechanic) and where it relaxes (after a boss,
> back in the hideout). An unintended jump > 1.5× between adjacent tiers is a bug.

## Solo vs. party
> How the curve changes with 2–4 players (`multiplayer.md`: party scaling, revive).

## Checks that guard it
> The tests/targets that would catch this curve breaking (e.g. `EndgameSmokeTest` ttk check,
> balance-matrix targets).

## Why this way / alternatives rejected

## Open questions
