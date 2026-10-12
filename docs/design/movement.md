# Movement

## What it is

The player has two input modes that share a single `Velocity` on a `CharacterBody2D`:

- **Direct (WASD)**: `Input.GetVector("move_left", "move_right", "move_up", "move_down")`
  drives velocity immediately, normalized on diagonals by Godot's built-in
  `GetVector` behavior.
- **Path-follow (hold a move input)**: while a move input is held, the player's
  `NavigationAgent2D.TargetPosition` is retargeted to the mouse position every
  physics frame. The agent then walks the computed path toward that target,
  including after the button is released, until the path is finished or overridden.

### Which input triggers path-follow **[DECIDED — changed by the third combat pass]**

**It is no longer "hold right mouse button."** Path-follow is triggered by **either**
of two conditions, evaluated as one boolean feeding one retarget line:

1. **The LMB skill slot currently holds the built-in `Move` action, and
   `skill_slot_lmb` is held.** Click-to-move is now the *default occupant of a skill
   slot* rather than a hardcoded input action. A player who binds a real skill over
   LMB loses click-to-move — deliberately.
2. **`force_move` is held** (suggested default `Space`). The dedicated escape hatch
   for when both mouse slots hold skills. Also the answer to "I want to walk over that
   thing without clicking it."

RMB is now a **normal skill slot** with nothing in it by default.

The old `move_to` action (RMB, `deadzone 0.5`) is **deleted** from `project.godot`,
replaced by `skill_slot_1`–`skill_slot_4`, `skill_slot_lmb`, `skill_slot_rmb` and
`force_move`. The mouse actions are named after the **slot**, not the button's current
meaning, so the names stay correct once rebinding UI exists.

**Why this shape and not Diablo's** (where LMB/RMB are skill slots and movement is
fused into a default LMB attack): this project's movement is *pathfinding*, not
move-and-attack. Fusing move into an attack skill would mean every attack drags you
toward the cursor. PoE's answer — LMB defaults to a move action, binding a skill there
replaces it, plus a separate force-move key — fits what's actually built here.

**Why it's safe to take click-to-move away from a player who binds over both mouse
buttons:** WASD is a first-class, always-available, unconditionally-overriding
movement mode in this project (see below). In PoE1 losing click-to-move would be
crippling because it's the *only* movement. Here the worst case is "you play with WASD
like a twin-stick game," which is a perfectly good way to play. That asymmetry is what
made the change acceptable at all.

Full rationale, and the six-slot loadout this serves, live in
`docs/design/skills.md` → *The right-click conflict*.

WASD and path-follow are not separate state machines — every `_PhysicsProcess` call
checks WASD first. If WASD input is nonzero, it drives velocity directly and the
method returns before touching the nav agent at all. There is no explicit "mode"
enum or transition; "mode" is just which branch ran that frame.

## Why WASD always overrides path-follow

If a player is being chased and needs an instant sidestep, waiting for a click to
register (and for pathfinding to plan a route) would feel laggy and imprecise
compared to a raw directional input. WASD is a more precise, lower-latency control
than click-to-move for reactive movement (combat positioning, dodging telegraphed
attacks). Path-follow is for traversal/repositioning across a room, not combat
micro. So responsiveness during the moments the player is most likely to need fast,
exact control (WASD held) takes priority every frame, unconditionally.

**WASD cancels the pending destination, it does not pause it.** The first frame WASD
is pressed while a path is still running, the agent is retargeted to the player's own
current position, which marks its navigation finished. Releasing WASD therefore leaves
the player standing still.

The earlier implementation merely skipped the nav branch while WASD was held, which
meant releasing WASD resumed walking to a destination the player had deliberately
steered away from — it felt like the character ignoring you. Taking manual control is
a statement of intent that should void the previous order, the same way it does in the
ARPGs this is modelled on.

This unconditional priority is also the load-bearing reason the input rebinding above
is safe: whatever a player does to their mouse-button skill slots, WASD always works.

Godot 4.1's `NavigationAgent2D` exposes no explicit "cancel path" call, so retargeting
to `GlobalPosition` is the available idiom. It's guarded behind
`!IsNavigationFinished()` so it fires once on interrupt rather than forcing a repath
every frame WASD is held.

## Why hold-to-move over single-click-only

Retargeting every physics frame while the move input is held (rather than only on
`_press`) means:
- A drag while holding continuously steers the destination, closer to how
  many ARPGs (the genre this game is drawing from) let players "walk the mouse."
- A plain click (press+release within a frame or two) still works correctly as a
  single-target path, because it retargets at least once before release, and the
  existing path-follow logic (steps 4-6 of `_PhysicsProcess`) carries the agent to
  that target after the button comes up — no special-casing needed for the
  single-click case.
- It was simpler to implement than a click-to-set-waypoint-then-lock model, with
  no observable downside for a top-down ARPG-style game.

## Alternatives considered

- **Tile-grid movement** (snap to a grid, one tile per input) — rejected. This is
  an ARPG-style game with continuous positioning (dodging AoE telegraphs, kiting),
  not a grid-tactics game; grid movement would fight that directly.
- **Click-queues-a-path-then-locks** (single click sets one target; subsequent
  clicks are ignored until the path finishes, or explicitly queue up) — rejected
  for now as needlessly restrictive; hold-to-retarget is strictly more flexible for
  the same implementation cost, and doesn't preclude adding path queuing later.

## Implementation notes / version caveat

The `NavigationServer2D.MapGetIterationId()` API (used in some published Godot
snippets to detect "has the nav map finished syncing yet") is **not present** in
this project's pinned engine version, Godot 4.1.1 (confirmed by checking the
installed `GodotSharp.dll` — it was added in a later 4.x release). `Player.cs`
uses `NavigationServer2D.MapIsActive(map)` instead as the closest available guard
against operating on a not-yet-registered navigation map on the first physics
frame(s) after scene load. This is a coarser check (active vs. fully synced) but is
sufficient here because the target is only ever set lazily on the first move
input, never synchronously in `_Ready()`, so there's no meaningful window where a
stale/empty path would actually run the character somewhere wrong — worst case is
a frame or two of the guard returning early, which the existing "navigation
finished" fallback already treats as standing still.

## Open questions

- ~~**The RMB binding is contested.**~~ — **resolved and decided** by the third combat
  design pass. Click-to-move is now the default occupant of the **LMB** skill slot,
  RMB is a free skill slot, and `force_move` is the escape hatch. Documented in
  "Which input triggers path-follow" above. `project.godot`'s `[input]` map and
  `Player.cs`'s move-input branch both change; see `docs/design/skills.md` →
  *The right-click conflict* for the exact spec handed to godot-expert.
  - **Follow-on, not yet answered:** once the LMB slot can hold a skill instead of
    Move, *what does the player see*? There's no hotbar UI, so "my click-to-move
    stopped working" would be indistinguishable from a bug. Not blocking (nothing can
    yet be bound to LMB), but it's the first thing hotbar UI has to communicate.
  - **Also follow-on:** `force_move` currently has no default binding decision beyond
    "suggested `Space`". `Space` is the most likely future dash/dodge key, so this may
    want revisiting when the dash lands — see the dash note below.
- **Avoidance** (`NavigationAgent2D` has built-in avoidance parameters) is off for
  now. Once summons/companions/other agents share the navmesh, avoidance radius and
  priority will need tuning so they don't stack on top of each other or the player.
- **Dash/dodge**: no dedicated ability exists yet, but it is now scheduled — the third
  combat pass puts a dash in milestone 2 as the skill that proves the cooldown lever
  and the no-speed-stat path (`docs/design/skills.md` → *Priority order*). It will be
  a **skill in a loadout slot**, not a bespoke movement mode, which resolves most of
  what this note was worried about. What remains open: it will need to interrupt
  path-follow the same way WASD does (an override check before the nav branch), and it
  will probably need to bypass `MoveAndSlide`'s normal collision response for its
  duration. Note also that it competes with `force_move` for the `Space` key.
- Should releasing the move input mid-drag *cancel* the remaining path instead of
  letting it finish? Currently it always finishes the last-set target. Note this is deliberately
  different from the WASD interrupt above: WASD is an explicit competing movement
  order, whereas letting go of the mouse is just the end of a click. Revisit once real
  playtesting feedback exists.

## Fourth pass: dodge and the Space key **[DECIDED — implemented]**

`force_move` moved from Space to **Shift**: Space is now the **dodge** everywhere
(docs/design/dodge.md), which the open questions above anticipated. Inside a run
there is no click-to-move at all — LMB/RMB are cleared and WASD + dodge is the whole
movement kit (docs/design/run.md → Controls); click-to-move and `force_move` still
work in the hideout and the sandbox. The player's physics layer is now
PlayerFaction only, so enemies pass through it instead of body-blocking.


## Fifth pass: sprint **[IMPLEMENTED 2026-10-12]**

**Shift** sprints (×1.55 speed) while moving, draining stamina at 30/s. Stamina is
two new stats — `MaxStamina` (100) and `StaminaRegen` (22/s, after a 0.7 s pause) —
so passives and gear can build around it later. Running dry exhausts you until
stamina is back to 30 %, so sprint can't be feathered at zero. A thin yellow bar
under mana shows it (red while exhausted). `force_move` moved from Shift to Ctrl.
