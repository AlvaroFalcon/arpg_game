# Economy: <resource or system>

> Template for `docs/design/economy.md` (or one section of it per currency). Adapted from
> Claude Code Game Studios (MIT, `.claude/THIRD_PARTY.md`). Delete the guidance lines (`>`)
> when filling it in. Every number has a source: `file:line`, a bot run (command + date), or
> **modelled** (formula only). Owner: economy-designer.

**Status:** draft | decided | implemented — **Date:** YYYY-MM-DD

## What it covers
> Which resources and exchanges, and which player behaviour they should encourage
> (e.g. "orbs: crafting is the main way to improve gear after tier 10").

## Currencies

| Resource | Scope (account / character) | Stored in | Cap | Role |
|---|---|---|---|---|
| Gold | | `Wallet` / `CharacterData.Currency` | | |
| Orbs (each) | account | `Profile.Currency` | | |
| Dust | account | | | |
| Shards | | | | |

## Faucets (where it comes from)

| Source | Resource | Amount | Frequency | Scales with | Source of the number |
|---|---|---|---|---|---|
| Monster drops | | | per kill | tier, rarity | `DropTables.cs:…` |
| Reality clear / boss | | | per reality | tier | |
| Rift reward / cash-out | | | per run | | |
| Quests | | | one-time | | `QuestCatalog.cs:…` |
| Selling / disenchanting | | | | item level | |

## Sinks (where it goes)

| Sink | Resource | Cost | Frequency | Scales with | Source |
|---|---|---|---|---|---|
| Skill levels (Escaldón) | gold | | per level | level | |
| Crafting orbs / bench | | | per craft | | |
| Gem shop / supports | | | | | |
| Rift rerolls (gold) | | | per reroll | | |
| Furnishings (Ysolde) | | | one-time | | |
| Buyback | | | | | |

## Flow per hour of play

| Point of progression | Earned / h | Spent / h (typical) | Net | Hours to the next meaningful purchase |
|---|---|---|---|---|
| Tier 1 | | | | |
| Tier 10 | | | | |
| Tier 20 | | | | |

> Measured with `scripts/autoplay.sh --mode=reality --tier=<t> --runs=6` unless marked modelled.

## Rules and invariants
> E.g. "nothing can be sold for more than it costs to buy", "no orb is strictly worse than
> another at every point", "co-op: loot is per player; the party doesn't multiply the faucet".

## Failure modes checked
- [ ] Inflation (faucets grow faster than sinks with tier)
- [ ] Dead currency (nothing worth buying with it at some stage)
- [ ] Loops (buy → sell / craft → disenchant gains)
- [ ] Walls (a jump in hours-to-next-purchase)
- [ ] Co-op (2–4 players change the rates?)

## Why this way / alternatives rejected

## Open questions
