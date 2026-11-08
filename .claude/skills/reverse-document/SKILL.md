---
name: reverse-document
description: "Write a missing or outdated design doc from the code that implements it: reads the system's scripts, scenes and tests, extracts the rules, numbers and flows, and drafts docs/design/<topic>.md in the project's format (what it is, why, alternatives, open questions), marking every inferred 'why' for the owner to confirm. Use for 'documenta este sistema', 'no hay doc de X', or when a doc and the code disagree."
argument-hint: "<system or path, e.g. scripts/View3D | flasks | loot filter>"
---

# /reverse-document

CLAUDE.md's policy: a system gets `docs/design/<topic>.md` once decided or implemented. Some grew
without one, or the doc drifted. This skill works backwards from the code.

## 1. Scope

Resolve the argument to files: `graphify query "<system>"` / `graphify explain "<Class>"` first
(run `graphify update .` on a fresh checkout), then Grep. List the scripts, scenes, tests and any
existing doc that mentions it. If a doc exists, this becomes a **drift report + update**, not a
new doc.

## 2. Extract (from code, with file:line for every claim)

- **What it is**: the player-facing behaviour, controls, where it appears.
- **Rules and numbers**: constants, formulas, tables, caps, timers, `Features.*` flags.
- **Flow**: states and transitions, signals/`GameEvents`, which autoloads it touches
  (`Profile`, `NetSession`, `GameSettings`…), co-op behaviour (host-authoritative? RPCs?).
- **Persistence**: what is saved (`CharacterData`, `Profile`), and save-format versioning.
- **Tests**: which suites cover it and what they assert; what is untested.
- **History**: `git log --follow` on the main files and the journal (`docs/devlog/journal.md`)
  for the reasons behind decisions.

## 3. Draft

Follow the existing docs' shape (look at one, e.g. `docs/design/dodge.md`): what it is → how it
works (with the numbers) → why it was designed this way → alternatives considered → open
questions. Rules:
- A "why" found in a commit message, PR or journal entry is cited.
- A "why" you inferred is marked **(inferido — confirmar)**. Never present a guess as a decision.
- Where code and an old doc disagree, write what the code does and list the mismatch under
  "Open questions" for the owner to decide which is intended.

## 4. Deliver

Show the draft (or the drift list) and ask before writing. After writing, run
`/graphify . --update` for the doc and add it to CLAUDE.md's list of design docs if it is new.

Adapted from Claude Code Game Studios (MIT, `.claude/THIRD_PARTY.md`).
