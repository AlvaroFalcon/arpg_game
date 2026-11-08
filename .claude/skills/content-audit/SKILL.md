---
name: content-audit
description: "Compare what the design docs and roadmaps say exists with what the code actually implements: skills, classes/ascendancies, realities and cavern styles, bosses, enemies, affixes, quests, stations, Rift features behind Features.* flags. Reports built / partial / planned-only / built-but-undocumented. Use for 'qué falta por hacer', 'planned vs built', roadmap reviews, or before writing a devlog/patch notes."
argument-hint: "[all | <doc or system, e.g. survival-roadmap | skills | bosses>]"
---

# /content-audit

Docs here are written incrementally and roadmaps (e.g. `docs/design/survival-roadmap.md`) list
work in phases. This skill tells the owner where each item really stands.

## 1. Sources

- **Planned**: the docs in scope (`docs/design/*.md`), especially lists, tables, "Phase"/
  "Roadmap"/"TODO"/"open questions" sections and status tags like **[IMPLEMENTED]**.
- **Built**: the catalogs and registries in code — e.g. skills (`scripts/Skills/`, the catalog
  Escaldón sells from), classes/ascendancies (`scripts/Characters/`), affixes
  (`scripts/Items/AffixLibrary.cs`), quests (`scripts/Quests/QuestCatalog.cs`), cavern styles /
  layouts (`scripts/World/`, `LayoutCatalog`), enemies/bosses (`scripts/Enemies/`), Rift
  systems (`Features.Rift*` flags and `scripts/Run/`, `scripts/Progression/`), stations in the
  hideout. Use `graphify query` to find catalogs you don't know.
- **Tested**: the suites in `scenes/tests/` that cover each item.

## 2. Match

Build a table per area. Match by id/name; when a name differs between doc and code, say so.

| Item | Doc (file:line) | Code (file:line) | Flag | Test | Status |
|---|---|---|---|---|---|

Status: **BUILT** (in code, reachable, tested) · **BUILT – UNTESTED** · **PARTIAL** (some of the
doc's rules missing — list them) · **BEHIND FLAG OFF** · **PLANNED ONLY** · **UNDOCUMENTED**
(in code, in no doc) · **DOC STALE** (doc says it's planned or different but code differs).
An item you couldn't locate in code is `NOT FOUND (searched: …)`, not "planned only" — say how
you searched.

## 3. Report

Counts per status, then the table, then the three lists that matter to the owner: what is
planned and not started, what is half-done, and which docs are stale (offer `/reverse-document`
for those). Chat by default; save to `docs/design/content-audit-YYYY-MM-DD.md` only if asked.

Adapted from Claude Code Game Studios (MIT, `.claude/THIRD_PARTY.md`).
