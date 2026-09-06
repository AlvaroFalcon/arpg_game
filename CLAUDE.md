# Path to Exile — Project Guide

Godot game project. This file orients any agent (or future-you) working here.

## Engine rules

- **Godot 4.x only.** Never write or suggest Godot 3 syntax/APIs.
  - `@export`, `@onready`, `@export_range(...)` etc (annotations, not `export(...)` / `onready var`)
  - Typed GDScript where practical (`var hp: int = 10`, `func take_damage(amount: int) -> void:`)
  - Signals via `signal foo(bar: int)` + `foo.connect(callable)` / `foo.emit(x)`, not `connect("foo", self, "_on_foo")`
  - `class_name` for reusable scripts, autoloads set up via Project Settings
- Prefer composition via scenes + `class_name` scripts over deep inheritance chains.
- If a GDScript API is unclear or version-sensitive, check installed Godot version first (`godot --version`) rather than assuming.

## Repo layout (create as needed, keep consistent)

```
project.godot
scenes/            # .tscn, organized by feature/area
scripts/           # .gd / .cs, mirrors scenes/ structure where sensible
assets/            # art, audio, fonts
addons/            # third-party plugins
docs/
  design/          # design decisions, written AFTER a system is decided/implemented (see below)
  devlog/
    journal.md     # raw running notes, append-only, one entry per notable session
    posts/         # polished devlog posts drafted from journal.md
    screenshots/   # images referenced by devlog posts
scripts_tools/      # (repo-root ./scripts is for game logic; put dev/CI tooling here if it grows)
```

(No `project.godot` exists yet — this is pre-implementation. Create the actual Godot project structure when development starts.)

## Design documentation policy

There is **no upfront GDD**. Design specs get written down incrementally:

- When a mechanic/system is **decided** (even before code exists) or **implemented**, write it up in `docs/design/<topic>.md`.
- Keep each doc scoped to one system (combat, inventory, movement, enemy AI, itemization, etc.), not one giant doc.
- Include: what it is, why it was designed that way, alternatives considered/rejected, open questions.
- The **game-designer** agent (below) is responsible for drafting/updating these when consulted on a design question with a concrete outcome. The **godot-expert** agent should flag when an implementation detail constitutes a design decision worth documenting.

## Devlog policy

- After a session with notable progress (a working feature, a hard bug solved, a design pivot), append a short raw entry to `docs/devlog/journal.md`: date, what changed, why, screenshot references if relevant.
- Periodically (when asked, or when the journal has enough material), the **game-dev-blogger** agent compiles journal entries into a polished post under `docs/devlog/posts/`, technical but friendly, explaining decisions and trade-offs, embedding screenshots from `docs/devlog/screenshots/`.
- Screenshots: capture via the headless/editor run tooling below, save into `docs/devlog/screenshots/`, reference with relative markdown image links.

## Running the game headless

Use [scripts/run_headless.sh](scripts/run_headless.sh) so agents can launch/test the game without a GUI session:

```bash
./scripts/run_headless.sh [extra godot args...]
```

It auto-detects a Godot 4 binary (checks `$GODOT_BIN`, `godot4`/`godot` on PATH, then common `/Applications` and `~/Downloads` install locations) and runs `--headless --path .` against the project. Use `--quit-after <N>` style args or a dedicated headless test scene/autoload for automated checks — ask the godot-expert agent to wire up a proper headless test entrypoint once there's actual game code.

A Godot.app (4.1.1) was found under `~/Downloads` at setup time but isn't installed to `/Applications` or on `PATH` — move it there (or `export GODOT_BIN=/path/to/Godot.app/Contents/MacOS/Godot`) so the script and MCP Godot tools work reliably.

## Agents

| Agent | Use for |
|---|---|
| [game-dev-blogger](.claude/agents/game-dev-blogger.md) | Writing devlog posts from `docs/devlog/journal.md`, technical-but-friendly tone, screenshots, decision rationale |
| [game-designer](.claude/agents/game-designer.md) | Design/mechanics questions, genre research, balancing advice — general game design expertise, not Godot-specific |
| [godot-expert](.claude/agents/godot-expert.md) | Writing/reviewing GDScript or C#, scene setup, engine architecture, performance, headless testing |

## MCP tools available

- `mcp__godot__*` — scene creation, node manipulation, running/stopping the project, reading debug output. Prefer these over raw CLI when doing structured scene edits.
- Context7 — use for any Godot API / GDScript question where behavior might be version-specific; don't rely on training data for engine APIs.
