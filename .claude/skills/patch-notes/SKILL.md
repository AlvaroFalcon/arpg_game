---
name: patch-notes
description: "Write player-facing patch notes (and an internal changelog) from git history: what's new, balance changes with old → new numbers, fixes, known issues — in player language, Spanish by default. Use before or after a release, for a PR's changes, or for 'notas del parche', 'changelog', 'qué ha cambiado desde la última versión'."
argument-hint: "[range: <from>..<to> | since-last-release | PR <n>] [--internal]"
---

# /patch-notes

Every push to `main` publishes a release (`.github/workflows/release.yml`). Players should get
notes they can read, not commit subjects.

## 1. Range

- `since-last-release` (default): the previous release's tag (`mcp__github__get_latest_release`
  or `git tag --sort=-creatordate`) `..origin/main`.
- `<from>..<to>`, or a PR number (its merge commit's parents).
Read `git log --first-parent --format='%h %s' <range>` (merge commits name the PRs), then each
PR's description (`mcp__github__pull_request_read`) — its "Qué hay de nuevo" / "Qué ha
cambiado" sections (create-pr format) are the best source. Read diffs only where the text is
unclear, especially for numbers.

## 2. Sort

- **Novedades** — new systems, skills, classes, realities, stations, menus.
- **Cambios de balance** — every number that changed, as `old → new` (find both in the diff:
  skill trees, `MonsterScaling`, affixes, drop tables). Group by class/skill.
- **Mejoras** — UI, quality of life, performance (with measured numbers if the PR has them).
- **Correcciones** — bugs, written as what the player saw ("Spark ya no atraviesa…").
- **Problemas conocidos** — open known issues (CLAUDE.md flakes don't count; player-visible bugs do).
Drop pure internals (tests, tooling, docs, refactors) from the player notes.

## 3. Write

Player notes: Spanish unless asked otherwise, second person, no file names or class names, the
in-game names of skills/NPCs/stations (Escaldón, Odran, the Mirror…). Short bullets. Lead with
the biggest change.

```markdown
# ARPG Game <version> — <date>
## Novedades
## Cambios de balance
## Mejoras
## Correcciones
## Problemas conocidos
```

`--internal` adds a developer changelog after it: per PR, link (full URL), files/systems
touched, tests added.

## 4. Deliver

Show it in chat. With the owner's ok, save to `docs/devlog/patch-notes/<version>.md`; it can
then feed the release body or a devlog post (game-dev-blogger). Never push to `main` or create
tags to publish them.

Adapted from Claude Code Game Studios' patch-notes/changelog skills (MIT, `.claude/THIRD_PARTY.md`).
