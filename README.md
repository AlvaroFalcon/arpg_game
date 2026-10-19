# ARPG Game

A top-down action RPG with loot, a deep build system and online co-op, built in **Godot 4.1 (.NET) and C#**.
It's inspired by the loot-driven ARPGs its author has played a lot. "ARPG Game" is a working title: the game has no official name yet.

The world is **simulated in 2D and drawn as voxels**. Every gameplay node is 2D. A 3D voxel stage with an orthographic camera renders under the 2D canvas and lines up with it pixel for pixel, giving an HD-2D look without 3D gameplay code.

> Devlog: https://alvarofalcon.github.io/devlogs/arpg-game/ · Design docs: [`docs/design/`](docs/design/) · Map of the code: [`docs/PROJECT_MAP.md`](docs/PROJECT_MAP.md)

---

## Contents

- [What's in the game](#whats-in-the-game)
- [Controls](#controls)
- [Getting started](#getting-started)
- [Running, testing and tooling](#running-testing-and-tooling)
- [How the project is organised](#how-the-project-is-organised)
- [Architecture in five ideas](#architecture-in-five-ideas)
- [Documentation](#documentation)
- [Releases](#releases)
- [Working with agents](#working-with-agents)

---

## What's in the game

| Area | What it is | Design doc |
|---|---|---|
| **Persistent characters** | Classes, a passive tree, skill and support gems, ascendancies and per-class resources. Characters, the stash and currency are saved to `user://profile.json`. | [classes](docs/design/classes.md), [progression](docs/design/progression.md) |
| **Hideout town** | A walled town that you can furnish (H opens edit mode). It holds Odran the Trader (sell and buy back, shards, skill gems), Ysolde's Furnishings, the Artificer's forge and the Mirror. | [hideout](docs/design/hideout.md), [crafting](docs/design/crafting.md) |
| **Altered Realities** | The endgame. You open a reality of tier 1–20 at the Mirror. Each one is a cellular-automata cavern generated fresh on every entry, with monster packs, a boss behind a gate and an exit portal. | [endgame](docs/design/endgame.md), [bosses](docs/design/bosses.md), [enemies](docs/design/enemies.md) |
| **Survival Rift** | The original survivor-style mode, entered through the hideout portal. You survive waves, pick upgrade cards, and at the Colossus choose between cashing out and pushing on. | [run](docs/design/run.md) |
| **Items and crafting** | Base items, affixes with tiers, rarities, a grid inventory, loot filters with ground labels, orbs used from the Crafting tab, and disenchanting and forging. | [items](docs/design/items.md), [affixes](docs/design/affixes.md), [crafting](docs/design/crafting.md) |
| **Combat** | Projectile, melee, area and orbit skills with forks and pierces; damage types, mitigation, ailments, a dodge roll and flasks. | [skills](docs/design/skills.md), [stats](docs/design/stats.md), [dodge](docs/design/dodge.md), [movement](docs/design/movement.md) |
| **Quests** | Six tutorial quests that each teach one station of the town, plus a quest log (J). | [quests](docs/design/quests.md), [tutorial](docs/design/tutorial.md) |
| **Online co-op (up to 4)** | A host-authoritative ENet listen server with join codes and UPnP. Play → *With friends*, or Esc → *Invite friends* from a solo hideout. You share the host's hideout, and any player can propose a Rift or a reality through a popup with a 30 s countdown. Difficulty and loot scale with each extra player. | [multiplayer](docs/design/multiplayer.md) |
| **Menus and UX** | Main menu, character select and create, settings, a movable HUD, an overlay map, tooltips and loading screens. | [menus](docs/design/menus.md) |

## Controls

| Action | Key |
|---|---|
| Move | `W` `A` `S` `D` (or click to move) |
| Skills | `LMB`, `RMB`, `1`–`4` |
| Dodge roll | `Space` |
| Sprint | `Shift` |
| Force move | `Ctrl` |
| Interact / pick up | `E` / `F` |
| Life / mana flask | `Q` / `R` |
| Inventory · Skills · Passives · Character · Quests | `I` · `K` · `P` · `C` · `J` |
| Map | `Tab` or `M` |
| Ground labels / reveal filtered | `Z` / `Alt` |
| Hideout edit mode | `H` |
| Pause (and *Invite friends* in the hideout) | `Esc` |

## Getting started

### Requirements

- **Godot 4.1.1 (.NET / mono build)**. The standard build cannot run C#. `godot --version` must contain `mono`.
- **.NET 6 SDK** installed at `~/.dotnet`. It's pinned by `global.json`; the project targets `net6.0`.
- Optional: Node 20+ for the art pipeline (`tools/art/`).

### First run

```bash
# macOS: always open the editor through the script, so Godot finds ~/.dotnet
./scripts/run_editor.sh

# or build and run the game headless
DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH dotnet build ArpgGame.csproj
./scripts/run_headless.sh
```

The main scene is `scenes/menus/MainMenu.tscn`.

> **Read [`CLAUDE.md`](CLAUDE.md) → *Toolchain* before your first build.** It documents a set of traps that can silently disable C#, including symlinked binaries, a Homebrew `dotnet`, leftover Godot processes and opening the project with a non-mono build, plus the Linux setup used in cloud sessions.

### Co-op on one machine

```bash
./scripts/coop_local.sh          # host window + one friend joining 127.0.0.1
COOP_CLIENTS=3 ./scripts/coop_local.sh   # a party of four
```

## Running, testing and tooling

| Command | What it does |
|---|---|
| `./scripts/run_all_tests.sh [filter]` | Builds the C# and runs every `scenes/tests/*.tscn` headless (18 suites), judging each by its `RESULT:` line. |
| `./scripts/run_headless.sh [godot args]` | Finds the mono Godot binary, sets `DOTNET_ROOT`/`PATH` and runs the project headless. |
| `./scripts/autoplay.sh --runs=3` | A bot plays full runs at about 3× speed for balance work (`--mode=reality --class=… --tier=…` plays realities). |
| `res://scenes/tools/RunCapture.tscn -- --only=<set>` | Scripted windowed captures for the devlog and PRs (menus, endgame, crafting, town, coop, quests…). |
| `res://scenes/tools/CoopBench.tscn` | Bandwidth and CPU for a party of four (Rift, or `--mode=reality`). |
| `res://scenes/tools/RiftBench.tscn`, `ExplosionBench.tscn` | Performance benches for the Rift and for mass deaths. |

Tests are scenes. Each prints `[TAG] CHECK: PASS|FAIL` lines and a final `RESULT: PASS|FAIL`. The co-op tests (`CoopSmokeTest`, `HideoutCoopTest`, `CoopRealityTest`) start extra Godot processes as clients on localhost.

## How the project is organised

```
project.godot            Godot project (main scene, autoloads, input map)
ArpgGame.csproj / .sln   generated by Godot, net6.0
scenes/                  .tscn files, by feature (menus, run, reality, enemies, core autoloads, tests, tools)
scripts/                 C# sources, mirroring the features (Characters, Combat, Items, Net, World, UI…)
resources/               .tres data: items and affixes, skills, stat blocks, currency, voxel shaders
assets/                  sprites, tiles, portraits, UI art, voxel models; art_src/ holds the editable sources
tools/art/               EasyPixel art pipeline (TypeScript): sprites, heroes, voxels, launcher art
tools/launcher/          Windows launcher and updater (.NET Framework 4.8, separate project)
docs/design/             one design doc per system, written once a system is decided or built
docs/devlog/             journal.md (raw notes), screenshots/, and the first posts
.claude/agents/          specialised agents (designer, Godot expert, multiplayer, performance, security, UI/UX, blogger)
```

For the full annotated map, with a mind map of how the folders and systems relate, see **[`docs/PROJECT_MAP.md`](docs/PROJECT_MAP.md)**.

## Architecture in five ideas

1. **2D simulation, voxel presentation.** Gameplay, physics and collisions are all 2D. `scripts/View3D/` mirrors actors into a voxel 3D stage under the canvas, and 2D sprites are hidden through `SelfModulate` alpha. Headless runs never create the 3D stage, so tests stay fast.
2. **Data as resources.** Base items, affixes, skills and stat blocks are `.tres` resources under `resources/`. Stats go through a modifier stack (Flat → Increased → More) in `scripts/Stats/`.
3. **Autoloads as services.** `GameSettings`, `Profile` (save data), `Sfx`, `HdView` and `NetSession` live in `scenes/core/`. Code must tolerate their absence, because tests run scenes directly.
4. **Events over coupling.** Gameplay events that other systems listen to, such as quests, go through the static `GameEvents` bus (`scripts/Core/GameEvents.cs`).
5. **Host-authoritative co-op.** One player hosts with ENet. Clients send inputs; the host simulates and replicates snapshots and events. `CoopHost`/`CoopClient` hold the shared replication, with `RiftHost`/`RealityHost` per mode. Every RPC stays on channel 0, and every payload is size-capped and sanitised (`NetGuard`).

## Documentation

- **[`docs/design/`](docs/design/)**: one document per system: what it is, why it was designed that way, the alternatives that were rejected, and open questions. There's no upfront GDD; docs are written when a system is decided or implemented.
- **[`docs/devlog/journal.md`](docs/devlog/journal.md)**: append-only raw notes, one entry per notable session.
- **Devlog site**: polished posts at https://alvarofalcon.github.io/devlogs/arpg-game/.
- **[`CLAUDE.md`](CLAUDE.md)**: the project guide for agents and contributors: engine rules, the toolchain and its traps, and the scripts.

## Releases

Two export presets live in `export_presets.cfg`: **Windows Desktop** (`build/windows/ArpgGame.exe`) and **macOS** (`build/macos/ArpgGame.zip`). There is no Web export, because .NET can't run in the browser in Godot 4.

Every push to `main` publishes a version through `.github/workflows/release.yml`. The Windows launcher (`tools/launcher/`) installs the game and keeps it updated. See [distribution](docs/design/distribution.md).

```bash
godot --headless --path . --export-release "Windows Desktop" build/windows/ArpgGame.exe
```

## Working with agents

The repo is set up to be developed with Claude Code. Specialised agents in [`.claude/agents/`](.claude/agents/) cover:
- game design
- Godot and C#
- multiplayer
- performance
- security
- UI/UX
- the devlog

[`CLAUDE.md`](CLAUDE.md) tells each agent when to use which tool and how to verify work: build, then run the headless tests and judge them by their `RESULT:` line.
