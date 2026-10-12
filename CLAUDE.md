# ARPG Game — Project Guide

Godot game project. This file orients any agent (or future-you) working here.

## Engine rules

- **Godot 4.x only.** Never write or suggest Godot 3 syntax/APIs.
- **Language: C# (.NET), not GDScript.** Requires the Mono/.NET build of the Godot editor (plain `Godot.app` cannot run C#) — see Toolchain below.
  - `[Export]`, `[Signal]`, `[GlobalClass]` attributes (not GDScript annotations)
  - Signals: `[Signal] public delegate void FooEventHandler(int bar);` + `Foo += Handler;` / `EmitSignal(SignalName.Foo, x)`
  - Nodes/scenes still authored as `.tscn`; scripts attached are `.cs`
  - Nullable reference types and modern C# (target framework set by the Godot-generated `.csproj`, currently `net6.0`) — don't fight the generated project settings
- Prefer composition via scenes + partial classes/components over deep inheritance chains.
- If a Godot C# API is unclear or version-sensitive, check the installed Godot version first (`godot --version`, and confirm it's the **mono** build) rather than assuming — the C# API has real gaps vs GDScript that shift between versions.

## Target platforms

- **Windows and macOS only, for now.** No Web/HTML5 export — the .NET runtime doesn't run in the browser sandbox, so C# projects structurally cannot export to Web in Godot 4 (not a temporary gap). If Web ever becomes a requirement, that means dropping C# for GDScript, not a build-config fix.
- Export presets live in `export_presets.cfg`, **committed to git** (Godot's own docs confirm this is safe — no secrets, just build config). Two presets exist: `Windows Desktop` → `build/windows/ArpgGame.exe`, `macOS` → `build/macos/ArpgGame.zip`. Both verified working end-to-end (real `dotnet publish` + packaged exe/app bundle) as of project setup.
- To export: `godot --headless --path . --export-release "Windows Desktop" build/windows/ArpgGame.exe` (or `"macOS" build/macos/ArpgGame.zip`) — this alone triggers the C# publish step, no separate build command needed. `build/` is git-ignored (export output, not source).

## Toolchain

- Godot editor: **Mono/.NET build** required, installed at `~/Applications/Godot_mono.app` — the standard (non-mono) build silently can't load C# scripts. `godot --version` must contain `mono`.
- **Always open the editor via [scripts/run_editor.sh](scripts/run_editor.sh), never from Finder/Dock/`open`.** A GUI-launched app gets a minimal environment without your shell `PATH`, so Godot can't find the SDK at `~/.dotnet` (it probes `/usr/local/share/dotnet`, which doesn't exist here) and fails with *"Failed to load .NET runtime ... hostfxr"* — C# is then dead in that session. The script exports `DOTNET_ROOT`/`PATH` first and refuses to launch a non-mono binary.
- .NET SDK: installed at **`~/.dotnet`** (.NET 6 SDK, matches this project's `net6.0` target). **Do not rely on a Homebrew-installed `dotnet`** if one exists on this machine — Homebrew's keg layout puts `hostfxr` under `libexec/host/fxr/` instead of the standard location Godot's resolver checks, and if it's a newer major version (e.g. 7.x) than this project targets, Godot's MSBuild/SDK resolution picks it up via `PATH` regardless of `DOTNET_ROOT`, causing a `FileNotFoundException` for `System.Runtime` at the wrong version. Both `DOTNET_ROOT=~/.dotnet` **and** `~/.dotnet` prepended to `PATH` are required together — `scripts/run_headless.sh` sets both automatically; do the same for any other Godot invocation (editor, `--build-solutions`, `--export-release`).
- Export templates: the **mono** export-templates package, installed under `~/Library/Application Support/Godot/export_templates/4.1.1.stable.mono/`.
- Known quirks (Godot 4.1.1 mono, macOS): headless `--quit`/`--quit-after` exits with code **1** by design — not a failure signal, check stdout/output files instead. `--build-solutions` run fully headless (no prior GUI editor session) can hang indefinitely after the actual compile succeeds (spamming harmless `EditorSettings` errors) — check `.godot/mono/temp/bin/Debug/ArpgGame.dll`'s existence/timestamp rather than trusting a clean process exit; prefer `--export-release`, which triggers the build internally and does exit cleanly. The one-time initial `ArpgGame.csproj`/`.sln` generation (via "Project > Tools > C# > Create C# Solution") has **no headless/CLI equivalent** in this version — it must be triggered once from the actual editor GUI; after that, headless builds/exports work fine.
- **Opening this project with a non-mono Godot silently breaks it — and corrupts scenes on save.** A plain `Godot.app` (e.g. the one at `~/Downloads/Godot.app`) loads the project without any error, renders nodes fine, but ignores every C# script: nothing responds to input, no `_Ready`/`_PhysicsProcess` runs. Worse, it **strips `script = ExtResource(...)` lines from any scene it re-saves**, because it can't resolve the C# type — so scenes silently lose their script attachments. Symptom pattern: visuals render but nothing moves, and `git diff` shows script lines vanishing from `.tscn` files. Always open with `~/Applications/Godot_mono.app` (`--version` must contain `mono`). If script lines have gone missing, restore them by hand after reopening in the mono build.
- **Never invoke Godot through a symlink — it silently disables C#.** Godot locates its bundled `GodotSharp` API assemblies *relative to the executable path it was invoked through*, not relative to the real binary. `~/bin/godot` and `~/bin/godot4` are symlinks into `Godot_mono.app`, and `~/bin` is on `PATH`, so a bare `godot` resolves there — Godot then looks for `~/bin/GodotSharp/` (which does not exist) and dies at startup with *"The specified runtimeconfig.json ... does not exist"*, `hostfxr_initialize_for_runtime_config failed`, and a `recursive_mutex lock failed` abort. Same binary, same flags, same environment: only the path it was reached by differs. `scripts/run_headless.sh` and `scripts/run_editor.sh` now canonicalise whatever they resolve (via `os.path.realpath`) before running it, so both are safe — but any ad-hoc `godot ...` invocation is still exposed. Use the real path inside the `.app`, or the scripts.
- **Never judge a headless test by the absence of FAIL lines.** When C# fails to load the scene prints *nothing*, so `grep -c ": FAIL"` returns 0 and a completely dead run is indistinguishable from a clean pass. Assert on the positive `RESULT: PASS` line and treat its absence as a failure.
- **Leftover Godot processes silently disable C#.** `scripts/run_headless.sh` decides whether it has a Mono build by running `godot --version` and grepping for `mono`. If earlier Godot processes are still alive (very easy after a run that hangs at shutdown — see the NavigationServer note below), that `--version` call itself **hangs**, the check produces no output, and the script warns *"is not a Mono/.NET build"* and runs anyway. The scene then loads with **no C# at all**: no `_Ready`, no test output, no quit condition, so it hangs forever and the log is empty. Symptom pattern: a test that passed minutes ago now produces zero output and never exits. Fix: `pkill -9 -f "Godot.*--headless"`, confirm it is empty, then re-run. Match on `--headless` rather than `Godot_mono`: the broader pattern also kills the *editor*, and relaunching that from Finder/Dock then hits the missing-`PATH` problem above. Worth doing between consecutive headless runs as a matter of course.
- **Judge a headless test by its RESULT line in a log, not by the process exiting.** Any scene that touches the NavigationServer hangs at shutdown *after* passing, and that now includes several test scenes rather than just `MovementSmokeTest` — anything asserting on movement or pickup pulls navigation in. A runner that pipes to `grep` and waits for exit will time out on a green test. Redirect to a file, poll it for `RESULT: (PASS|FAIL)`, then kill the process.
- **Godot block-buffers stdout when it is not a terminal.** A test that prints under ~4 KB and then hangs at shutdown never flushes, so redirecting to a file and killing the process yields an empty log — indistinguishable from "crashed at startup". `script -q /dev/null` does not help here (no tty is available in agent shells). Two workarounds that do: let the process exit on its own (it flushes at exit), or have the test mirror its verdict to a file before quitting — `MovementSmokeTest` writes `user://movement_smoketest_result.txt` for exactly this reason, since it is the one scene that reliably hangs at shutdown.
- **Running a scene headless does NOT build C# first.** `godot --headless <scene>` runs against the last-compiled `.godot/mono/temp/bin/Debug/ArpgGame.dll`, so new/edited `.cs` files are silently ignored until built. Build first with `DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH dotnet build ArpgGame.csproj` — it's fast (~2s), exits cleanly, and writes to the same DLL path the runtime loads (unlike `--build-solutions`, which can hang).
- **Pass scene paths as `res://...`, not a relative path.** `godot --headless --path . scenes/foo.tscn` resolves the scene against the *cwd*, not the project, silently falls back to the main scene, and then runs forever if that scene has no quit condition. Use `res://scenes/foo.tscn` and add `--quit-after <frames>` as a safety net.
- Quitting a scene that used the NavigationServer can emit `FATAL: Condition "!rc_owner" is true.` at shutdown and leave the process hanging *after* everything ran successfully. Read the captured stdout to judge the result; kill the leftover process rather than treating the hang as a failure.

- **Linux (cloud sessions):** the same toolchain works on Linux. Godot 4.1.1 mono for Linux unzipped anywhere (e.g. `/opt/godot/...`), .NET 6 via `dotnet-install.sh --channel 6.0 --install-dir ~/.dotnet`, and `GODOT_BIN` pointed at the real binary. A fresh checkout must be imported once with `"$GODOT_BIN" --headless --path . --editor --quit` (with `DOTNET_ROOT`/`PATH` set — without them the editor crashes on a missing hostfxr). Re-run that import after adding a new C# script that a scene references for the first time, and after re-exporting art (changed PNGs are only re-imported by the editor). Windowed captures work under Xvfb with the compatibility renderer: `xvfb-run -a -s "-screen 0 1280x720x24" "$GODOT_BIN" --path . --rendering-driver opengl3 res://scenes/tools/RunCapture.tscn -- --out=/tmp/shots`.
- **`pkill -f` patterns match your own shell.** A pattern like `pkill -f "Godot_v4"` also matches the bash command line that contains it and kills the very shell running it. Use a bracket trick: `pkill -9 -f "[G]odot_v4.1.1"`.

## Scripts

- `scripts/run_all_tests.sh [filter]` — builds C#, runs every `scenes/tests/*.tscn` headless, judges by the `RESULT:` line (never by exit codes or absence of FAIL), kills leftovers between runs.
- `scripts/autoplay.sh --runs=3 [--skill=spark] [--report=30]` — the autoplay bot plays full runs headless at ~3× speed for balance work; lines are mirrored to `user://autoplay.log` (stdout is block-buffered). `--mode=reality --class=warrior|rogue|arcanist --tier=1 [--level=N] --runs=6` plays Altered Realities back to back with an in-memory character (the real profile is untouched).
- Tests that can hang at shutdown mirror their lines to `user://test_results/<Name>.txt`; `run_all_tests.sh` reads that file too (see `EndgameSmokeTest`).
- `scenes/tools/RunCapture.tscn` — scripted windowed run that saves devlog/PR screenshots (menu, skill pick, mid-run, level-up, pause, both bosses and their mechanics, results). `-- --only=endgame` captures character select/create, hideout, Mirror, skills/tree/altar panels, all three realities and Nereth's mechanics with demo characters.
- `.github/workflows/release.yml` + `tools/launcher/` — releases and the Windows launcher (see `docs/design/distribution.md`). Publish a version by pushing a tag: `git tag v0.2.0 && git push origin v0.2.0`. The launcher is a separate .NET Framework 4.8 project (`ArpgGame.csproj` excludes `tools/**`); build it with `dotnet build tools/launcher -c Release`.
- `tools/art/` — the EasyPixel art pipeline (see `docs/design/art-pipeline.md`): `npm install && npm run setup && npm run build`; `npx tsx voxels.ts` exports the voxel models (`assets/voxels/`) used by the HD-2D view — re-run it (then re-import) whenever sprites or tilesets change.

## Game structure (PoE-style endgame + persistent characters)

- Main scene `scenes/menus/MainMenu.tscn` → Play (character select/create) → `scenes/Hideout.tscn` → Mirror (E) → `scenes/reality/Reality.tscn` (an Altered Reality: room-graph layout, packs, boss behind a gate) → exit portal → hideout. The hideout portal is the Survival Rift (`scenes/run/Run.tscn`, the original survivor mode). `scenes/Main.tscn` is the original dummy sandbox ("Training Grounds").
- Persistent character code lives in `scripts/Characters/` (classes, passive tree, gems/loadout, ascendancies, class resources); realities in `scripts/World/` (layouts, flow field, tiers, controller). Design: `docs/design/endgame.md`, `classes.md`.
- Autoloads (`scenes/core/`): `GameSettings` (user://settings.cfg), `Profile` (characters + stash, user://profile.json), `Sfx` (procedural sounds/music), `HdView` (voxel HD-2D view). Code must tolerate their absence: tests run scenes directly.
- **Graphics: 2D simulation, voxel presentation.** Gameplay is all 2D nodes; `scripts/View3D/` renders a voxel 3D world under the 2D canvas with an orthographic camera synced to the Camera2D (ground plane lines up pixel-for-pixel with 2D overlays). 2D sprites are hidden via `SelfModulate` alpha 0 — don't use `SelfModulate` for gameplay effects. Headless runs never create the 3D stage. Forward+ captures in the cloud: install `mesa-vulkan-drivers`, run with `VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json` and `--rendering-driver vulkan` (Compatibility/opengl3 works but has no shadows/glow/DOF in 4.1).
- Design docs per system: endgame, classes, run, progression, dodge, enemies, bosses, menus, art-pipeline, distribution (plus the earlier movement, skills, stats, items, affixes).

## Repo layout (create as needed, keep consistent)

```
project.godot
scenes/            # .tscn, organized by feature/area
scripts/           # .gd / .cs, mirrors scenes/ structure where sensible
assets/            # art, audio, fonts (sprites/tiles exported by EasyPixel)
assets/art_src/    # editable EasyPixel .epx.json sources of every sprite
tools/art/         # EasyPixel-core art pipeline (Node/TypeScript)
addons/            # third-party plugins
docs/
  design/          # design decisions, written AFTER a system is decided/implemented (see below)
  devlog/
    journal.md     # raw running notes, append-only, one entry per notable session
    posts/         # polished devlog posts drafted from journal.md
    screenshots/   # images referenced by devlog posts
build/             # git-ignored export output (Windows .exe, macOS .app/.zip)
ArpgGame.csproj / .sln  # generated by Godot, targets net6.0 — don't hand-edit the TFM
```

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

It auto-detects the Mono/.NET Godot binary (checks `$GODOT_BIN`, `godot4`/`godot` on PATH, then `~/Applications/Godot_mono.app`), sets `DOTNET_ROOT`/`PATH` to the correct `~/.dotnet` SDK (see Toolchain above), and runs `--headless --path .` against the project. Use `--quit-after <N>` style args or a dedicated headless test scene/autoload for automated checks. Remember: exit code 1 on quit is normal here, not a failure.

## Agents

| Agent | Use for |
|---|---|
| [game-dev-blogger](.claude/agents/game-dev-blogger.md) | Writing devlog posts from `docs/devlog/journal.md`, technical-but-friendly tone, screenshots, decision rationale |
| [game-designer](.claude/agents/game-designer.md) | Design/mechanics questions, genre research, balancing advice — general game design expertise, not Godot-specific |
| [godot-expert](.claude/agents/godot-expert.md) | Writing/reviewing GDScript or C#, scene setup, engine architecture, performance, headless testing |

## MCP tools available

- `mcp__godot__*` — scene creation, node manipulation, running/stopping the project, reading debug output. Prefer these over raw CLI when doing structured scene edits.
- Context7 — use for any Godot API / GDScript question where behavior might be version-specific; don't rely on training data for engine APIs.
