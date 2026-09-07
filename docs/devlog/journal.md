# Devlog journal (raw notes)

Append-only. One entry per notable session: date, what changed, why, screenshot filenames if captured.
The game-dev-blogger agent reads this to draft polished posts under `posts/`.

---

## 2026-09-07 — Project setup

- Set up `CLAUDE.md`, three agents (godot-expert, game-designer, game-dev-blogger), and headless-run tooling before any actual game code exists.
- Decided: no upfront GDD — design docs get written per-system in `docs/design/` once decided/implemented, not planned wholesale up front.
- Godot 4.1.1 found under `~/Downloads/Godot.app` (not installed to `/Applications` yet, not on PATH).

## 2026-09-07 — First player movement

- Implemented WASD + hold-right-click-to-move (PoE-style) on a `CharacterBody2D` with `NavigationAgent2D`. WASD always overrides path-follow, no explicit mode enum — see `docs/design/movement.md`.
- Built a small PoE-style stat modifier system (`scripts/Stats/`: `Stat`, `StatModifier`, `PlayerStats`) so movement speed reads from a `Flat`/`Increased`/`More` stack instead of a hardcoded constant — sets up equipment/talents to hook in later without a rewrite. See `docs/design/stats.md`.
- Version snag: `NavigationServer2D.MapGetIterationId()` (used in current official docs/examples) doesn't exist in this project's pinned Godot 4.1.1 C# API — added in a later 4.x release. Used `MapIsActive()` instead as the sync guard; documented the tradeoff in `docs/design/movement.md`.
- Verified: headless compile clean, project boots with no script errors. Not yet verified: actual WASD/mouse feel in the editor (headless can't drive input) — manual playtest still needed.

## 2026-09-07 — Two silent toolchain traps, and making the interrupt feel right

Movement "didn't work" in the editor despite passing every headless check. Two separate environment problems, both of which fail *silently*:

- **Opened the project with the non-mono Godot** (`~/Downloads/Godot.app`) instead of `Godot_mono.app`. It loads the project with no error and renders everything fine, but ignores every C# script — so the placeholder art drew and nothing moved. Worse, it **strips `script = ExtResource(...)` lines from any scene it re-saves**, because it can't resolve the C# type. That's why `Main.tscn` kept losing its script attachment between sessions; I'd been restoring it by hand thinking the editor was just stale.
- **Then the mono editor failed with "Failed to load .NET runtime ... hostfxr".** Launching from Finder gives an app a minimal environment with no shell `PATH`, and the SDK here lives at `~/.dotnet` — a location Godot doesn't probe (it looks in `/usr/local/share/dotnet`). `run_headless.sh` had been papering over this all along by exporting `DOTNET_ROOT`/`PATH` itself. Added `scripts/run_editor.sh` to do the same for the GUI, and it refuses to launch a non-mono binary so the first trap can't recur.

Both are now documented in CLAUDE.md. The lesson worth keeping: when headless passes and the editor fails, suspect the environment, not the code — and check *which binary* is actually running.

- **Design change: WASD now cancels a click-to-move destination instead of pausing it.** Original behaviour skipped the nav branch while WASD was held, so releasing WASD resumed walking to a destination the player had deliberately steered away from — it read as the character ignoring you. Godot 4.1's `NavigationAgent2D` has no explicit cancel, so the idiom is retargeting the agent to its own current position, which marks navigation finished. Guarded behind `!IsNavigationFinished()` so it fires once per interrupt rather than forcing a repath every frame.
- Built `scripts/tests/MovementSmokeTest.cs` — a real headless functional test that drives input actions and asserts on positions. Caught a subtlety worth remembering: `Input.ActionPress()` works on an action with **zero bound keys**, so a movement test can pass while real keyboard input does nothing. The test now asserts on `InputMap.ActionGetEvents()` too.
- Verified the cancel fix by deliberately reverting it and re-running: 43.33px of resumed movement with the bug, 0.00px with the fix. A test that has never been seen to fail isn't evidence.
- Added `scripts/capture_devlog_screenshot.sh` + `scenes/tools/MovementCapture.tscn` to generate devlog imagery from a real windowed run, drawing the player's trail colour-coded by input mode. Static shots of a red dot say nothing about movement; trails do. Must run windowed — `--headless` uses the dummy renderer and captures an empty image.
- Screenshots: `screenshots/movement-trail.png` (both input modes), `screenshots/movement-cancel.png` (WASD interrupting a path).
