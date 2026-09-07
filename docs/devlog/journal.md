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

## 2026-09-07 — Skills system design (three passes) and the first combat slice

Three design passes on `docs/design/skills.md` before writing any code, each one reversing something from the last. Worth recording *what* got reversed, because the reversals were the useful part:

- **Pass 2 → 3: attack/cast speed went back to being derived from tags.** Pass 2 specified an explicit `SpeedStat` field on the definition and rejected tag-derivation. Wrong: the tag is the contract the player reads. If a tooltip says `Spell` and the player buys cast speed, cast speed must work — a separate field means two independent notions of "is this a spell" that can silently disagree, and no UI would ever surface it. Cost of the reversal: `Attack`/`Spell` are now **structural** tags (re-tagging changes behaviour), so tags split into structural vs descriptive.
- **Pass 3 found a real gap by tracing one example.** "Projectiles split in two on hit" is not a `StatModifier` — there's no actor stat called "split", the Flat/Increased/More formula is meaningless for it, and writing the number does nothing unless an executor reads it. That produced **seam 6, `CastModifier`**: a second modifier kind that targets the per-cast parameter block instead of a stat, is order-*dependent* (so it carries a `Priority`), and is applied in a named pipeline stage that runs over an empty list from day one. The design argument: players remember behaviour transforms and forget numeric ones. Every build identity in this genre is a behaviour transform.
- **Cooldowns moved into the first slice** (owner override), with `CooldownRecovery` as a *rate divisor* rather than a subtraction — "-X% cooldown" hits 100% and breaks. Cooldown starts on cast start, not cast end, because on-end secretly makes cast speed into cooldown reduction. Charges are deferred but the runtime state is `(charges, rechargeTimer)` from day one, because the bare-float version would later force a simultaneous change to the cast gate, the debug readout, the hotbar UI and the save file.
- **RMB stopped being `move_to`.** LMB now defaults to a built-in `Move` occupant of the loadout slot, RMB is a free skill slot, and `force_move` (Space) is the escape hatch. This modified already-shipped movement code; `docs/design/movement.md` was corrected. What makes it safe here is that WASD is a first-class always-overriding movement mode — the worst case (skills on both mouse buttons, no force_move) is "you play twin-stick", which is fine.

Then built Milestone 1: entity-agnostic `ActorStats` (renamed from `PlayerStats`), `Life`/`Mana`/damage-carrier/`AttackSpeed`/`CastSpeed`/`CooldownRecovery`, `GetModifiers(query)`, a damage *payload object* rather than a float, `SkillDefinition`/`ProjectileSkillDefinition`/`SkillInstance`, a six-slot loadout, the skill user with cost payment and per-skill timers, the projectile executor, `Fireball.tres`, an inert target dummy, and a debug readout with floating damage numbers.

The projectile executor is written as a **capability set with everything off** — count, spread, pierce, splits, chains, homing, size, speed, range, on-hit-explode all exist as parameters; only `count=1, pierce=0` is implemented. Adding fork later should be a branch in one file with an input that already exists, rather than a simultaneous change to the param block, the modifier type and the UI.

- Verified: clean compile, `SkillsSmokeTest` passes 7/7 (input drives cast, cost paid, cooldown gates a repeat cast, insufficient mana blocks and spends nothing, cooldown recovers, projectile kills the dummy), `MovementSmokeTest` passes 7/7 including three new cases for the input rebinding (LMB defaults to Move, a skill equipped over LMB disables click-to-move, force_move bypasses it).
- **New toolchain note:** `MovementSmokeTest` now segfaults during engine *teardown*, after `RESULT: PASS` is printed — same family as the already-documented NavigationServer shutdown hang, and like that one it says nothing about whether the test passed. Read the captured stdout, not the exit status. Piping to a terminal hides this: the pipe never flushes because the process never exits cleanly, so it looks like a hang with no output. Redirect to a file instead. Not proven pre-existing.
- Follow-up: put four dummies into `Main.tscn` (the playable scene) at 30/105/210/420 life — deliberately varied, because at 35 damage a uniform 30-life dummy one-shots and you learn nothing about how damage accumulates. The 30 stays as the "does it die at all" case. Per-instance `BaseMaxLife` overrides rather than changing the dummy's default, so `SkillsSmokeTest`'s one-shot kill assertion is untouched.

## 2026-09-07 — First two projectile capabilities: count/spread and fork

Wired up two of the inert capabilities the M1 executor was authored with, which was the first real test of whether "author the whole parameter list, implement none of it" actually pays off. It did: **`Count`/`SpreadDegrees` was a change to one method** (`GenerateProjectileSpawnRequests`), because the caller already consumed a `List<ProjectileSpawnRequest>` rather than a single projectile.

- **Renamed `Splits` -> `Forks`.** A *fork* happens on hit; a *split* happens at cast. The design doc's phrasing was "splits in two on hit", which is a fork. Renaming frees "split" for the at-cast case, which is what `Count` already does — the two capabilities were competing for one word.
- **The design doc was wrong about fork, and implementing it is what found that.** The doc says "chain and fork are both Redirect with a decremented counter". True for chain (one projectile, one new direction). False for fork, which is 1 -> 2 and has no single direction to return. `OnHitOutcomeKind` gained a fourth member, `Fork`. Worth noting the seam still did its job — on-hit resolution being an explicit inspectable value is exactly what made adding a fourth kind trivial; only the prose about it was wrong.
- **Spread has two regimes**: below 360 it's a cone divided by `Count - 1` so the outermost projectiles sit on the cone edges; at 360+ it's a nova around the caster divided by `Count`, so the first and last don't overlap. `Count > 1` with `Spread = 0` deliberately stacks them into a "slug" rather than being auto-corrected — Hell Clock's Blunderbuss relic is exactly that effect, and silently fanning a spread the author didn't ask for is worse than an odd-looking one.
- Fork children inherit the parent's travelled distance (one shared range budget, so forking trades reach for coverage) and its already-hit set (or a child instantly re-hits the target its parent just forked off). The already-hit set was justified for pierce back in M1; this is the second thing it turned out to be load-bearing for.
- Fork spawning is deferred: it runs inside an `Area2D` overlap signal, i.e. mid-physics-step. But only the *tree insertion* is deferred — the child is initialized synchronously, while the parent is still alive, since the parent has already called `QueueFree()` by then.

**The lesson of the session, and it's an old one recurring in a convincing disguise.** The first version of the "fork must not cascade" test counted live projectiles in empty space — and it **passed with the bug deliberately reintroduced**. Fork children fly apart at ±30° and never hit anything again, so nothing could ever fork twice regardless of whether the counter was inherited decremented. The test was structurally incapable of failing. Fixed by placing a second dummy on a child's flight path: correct leaves 1 projectile, buggy leaves 3, and re-breaking it now genuinely reports FAIL. `MovementSmokeTest` earned this same lesson two sessions ago with `Input.ActionPress` on an unbound action; the shape of the mistake is identical — asserting on something the bug can't influence.

- New: `scripts/tests/ProjectileCapabilitiesTest.cs` + scene, 5/5 PASS. `SkillsSmokeTest` still 7/7.
- `Fireball.tres` now has `Count = 5, SpreadDegrees = 30, Forks = 2` as visible placeholder values.
- Built `scripts/tools/SkillCapture.cs` + `scripts/capture_skill_screenshots.sh` to produce devlog imagery for the capabilities, following the `MovementCapture` pattern: three scripted phases (pierce, fork, spread), each drawing every projectile's full flight path as a `Line2D` and marking which dummies took a hit. Trails are colour-coded by order of first sighting, which is what makes the fork's parent-vs-children Y readable. Screenshots: `skills-pierce.png`, `skills-fork.png`, `skills-spread.png`. Projectile speed is dropped to 260 (from 600) in the capture only, so a trail has visible length.
- **Two capture bugs worth recording, both of which produced plausible-looking wrong images.**
  - The pierce shot captured at frame 131 while the projectile needed ~171 frames to reach the fourth dummy, so the image showed three hits and a trail stopping short. It looked exactly like an off-by-one in the pierce counter, and it was purely a screenshot-timing bug. Phase durations now outlast the actual flight time.
  - Recolouring a hit dummy via its `Visual` polygon's `Color` silently does nothing: `TargetDummy` runs its own 0.1s white hit-flash that *restores the base colour* when it expires, overwriting the change a few frames later. Used `Modulate` instead, a property `TargetDummy` never touches.
- The capture tool polls the scene for live `Projectile` nodes rather than having `Projectile` emit position signals, so nothing in `scripts/Combat` or `scripts/Skills` knows the capture tool exists.
