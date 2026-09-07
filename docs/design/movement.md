# Movement

## What it is

The player has two input modes that share a single `Velocity` on a `CharacterBody2D`:

- **Direct (WASD)**: `Input.GetVector("move_left", "move_right", "move_up", "move_down")`
  drives velocity immediately, normalized on diagonals by Godot's built-in
  `GetVector` behavior.
- **Path-follow (hold right mouse button)**: while `move_to` (RMB) is held, the
  player's `NavigationAgent2D.TargetPosition` is retargeted to the mouse position
  every physics frame. The agent then walks the computed path toward that target,
  including after the button is released, until the path is finished or overridden.

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

Godot 4.1's `NavigationAgent2D` exposes no explicit "cancel path" call, so retargeting
to `GlobalPosition` is the available idiom. It's guarded behind
`!IsNavigationFinished()` so it fires once on interrupt rather than forcing a repath
every frame WASD is held.

## Why hold-to-move over single-click-only

Retargeting every physics frame while RMB is held (rather than only on `_press`)
means:
- A drag while holding RMB continuously steers the destination, closer to how
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
sufficient here because the target is only ever set lazily on the first `move_to`
input, never synchronously in `_Ready()`, so there's no meaningful window where a
stale/empty path would actually run the character somewhere wrong — worst case is
a frame or two of the guard returning early, which the existing "navigation
finished" fallback already treats as standing still.

## Open questions

- **Avoidance** (`NavigationAgent2D` has built-in avoidance parameters) is off for
  now. Once summons/companions/other agents share the navmesh, avoidance radius and
  priority will need tuning so they don't stack on top of each other or the player.
- **Dash/dodge**: no dedicated ability exists yet. When added, it will likely need
  to interrupt path-follow the same way WASD does (an override check before the nav
  branch), and probably bypass `MoveAndSlide`'s normal collision response for the
  duration of the dash — needs its own design pass when combat is scoped.
- Should releasing RMB mid-drag *cancel* the remaining path instead of letting it
  finish? Currently it always finishes the last-set target. Note this is deliberately
  different from the WASD interrupt above: WASD is an explicit competing movement
  order, whereas letting go of the mouse is just the end of a click. Revisit once real
  playtesting feedback exists.
