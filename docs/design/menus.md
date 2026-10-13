# Menus, hideout, profile, settings, audio

Status: **implemented** (first pass). Owner brief: *"functional menus — start and
basic settings — and pause with Escape mid-run."*

## Flow

```
Main menu ──► Play: character select / create ──► Hideout
    ├──► Settings                                   ├──(Mirror, E)──► Altered Reality ──(exit portal / collapse / abandon)──► Hideout
    └──► Training Grounds (sandbox, Main.tscn)      └──(portal)────► Survival Rift ──► Results ──► Hideout
```

(Updated for the endgame, 2026-10-11: "Quick Run" is gone — the Survival Rift is
entered from the hideout.)

`project.godot`'s main scene is now `scenes/menus/MainMenu.tscn`. Scene changes
go through `Core/Scenes.Go`, which also unpauses and clears the panel stack.

## Main menu

The arena, bosses and enemies drawn behind the title (the camera drifts), the menu,
and a line of lifetime records once there are any (runs, victories, best time, best
level, gold). Keyboard/gamepad focus works on every button.

## Characters

**Play** opens character select: each character with portrait (class or
ascendancy sprite), level, class, best tier, realities cleared and deaths; Play,
Delete (asks to confirm), New character. Creation: a name and one of three class
cards (sprite, attributes, description, class mechanic, starting gems,
ascendancies). Up to 6 characters.

## Hideout

PoE's hideout, minimal: your persistent character with its gear, the inventory and
equipment strip (**I**), three unkillable training dummies (5000 life, 2500 regen)
to try skills on, the debug stat readout as a character sheet, and a **portal** —
walk into it to start a run. Click-to-move works here (LMB), and RMB is Fireball as
in the sandbox.

Endgame additions: the **Mirror of Realities** (E: tier picker with shard counts,
empower for gold, step through, or enter the Survival Rift), the **Altar of
Ascension** (E: choose an ascendancy, spend its points), and the character line at
the top (name, level, class, gold, best tier, unspent points). The debug readout is
hidden here now that the panels exist.

## Character panels (K / P)

Available in the hideout and inside realities, built on `ModalPanel` (pauses the
game, joins the panel stack, Escape or the button closes):

- **Skills (K)**: the 4 loadout slots with their support sockets; click a slot or
  socket, then a gem from the collection. A gem moves if it was socketed elsewhere;
  right-click empties. Only supports that fit the targeted skill are listed.
- **Passive tree (P)**: pan by dragging, zoom with the wheel, LMB allocate, RMB
  refund (gold); hover reads a node.
- **Altar**: see docs/design/classes.md -> "Ascendancies".

## Controls (2026-10-12)

| Key | Action |
|---|---|
| WASD | move (Shift sprint, Space dodge, Ctrl force-move) |
| LMB, RMB, 1, 2, 3, 4 | the six loadout skills, aimed at the cursor (realities, hideout) |
| E | interact: Mirror, Altar, Stash, the exit portal after a boss |
| I / C / K / P | inventory, character sheet, skills, passive tree |
| Esc | close the top panel, else pause |

In the Survival Rift skills auto-cast and auto-aim (no mana). The boss's exit
portal is used with E, never by walking onto it, since loot lands around it.

**Character sheet (C)**: attributes with what each grants, defences (life, mana,
stamina, armour, resistances, dodge), offence (speeds, critical, area,
projectiles, ailments), utility and records. **Tooltips** on the passive tree float
next to the cursor (`HoverTooltip`) instead of a line under the tree, so long text
never resizes the panel.

## Pause (Escape)

`UI/PauseMenu.cs`, in the run, the hideout and the sandbox. Escape first closes an
open panel (the inventory) — the existing `PanelStack` rule — and only pauses when
there is nothing to close. Two consumers of one key need an order-independent
handshake, so `PanelStack` now records the frame it closed something
(`ClosedThisFrame`) and whether anything is open (`HasOpen`). The pause menu never
opens over a screen that already owns the pause (level-up, results). It shows the
current build in a run, and offers Resume, Settings, Abandon run (→ results, gear
kept) and Quit.

## Settings

`Core/GameSettings.cs` (autoload) persisted to `user://settings.cfg`; the panel
(`UI/SettingsPanel.cs`) is shared by the main menu and the pause menu, applies every
change live and saves on close.

- Audio: master, music, effects volume (Music/SFX buses are created at startup).
- Display: fullscreen, V-Sync, screen shake, damage numbers.
- Gameplay: auto-cast, auto-aim.

Screen shake can be turned off without losing information: every shake accompanies
a visible telegraph or flash.

## Level-up and results

- Level-up: three cards (keys 1–3), reroll (R, two per run), the build so far
  underneath. Queued level-ups are offered back to back. Card colour: gold keystone,
  ember new skill, green support, rarity colour for passives.
- Results: victory/defeat, time, level, kills, elites, bosses, gold, the build, and
  **three items — keep one**, shown with the normal item tooltip text.

## Profile

`Core/Profile.cs` (autoload). The persistent character: equipped gear, the
inventory grid, gold, records. The live `Player` owns its inventory/equipment while
a scene runs; scenes call `ApplyTo(player)` on spawn and `CaptureFrom(player)`
before leaving. Saved to `user://profile.json` on every capture: items are stored
as base id + rarity + item level + name + rolled base stats + affixes (id, tier,
value) and rebuilt with `EquipmentItem.Restore` — nothing is re-rolled on load. The
run build and run-only bonuses are stripped before capture.

## Look

`UI/UiTheme.cs` builds one theme in code (deep violet panels, gold for selectable
things, ember for warnings) and the screens are assembled from its helpers rather
than hand-laid `.tscn` files — consistent, resolution-independent, one place to
tune. The window is 1280×720 with `canvas_items` stretch; the default texture
filter is Nearest for the pixel art.

## Audio

`Core/Sfx.cs` (autoload) synthesises every sound at startup (short envelopes over
sine/square/noise) and three looping music beds (menu, run, boss). There are no
audio assets yet; replacing any sound with a real file is a one-line change.

## Open questions

- Key rebinding UI (the actions exist in `project.godot`).
- A stash and a vendor in the hideout (gold has no sink yet).
- Controller support pass (focus works; dodge/aim on sticks does not yet).


## In-game HUD: the bottom-centre cluster **[IMPLEMENTED 2026-10-12]**

Modelled on the owner's reference (AION 2). `UI/PlayerHudBar.cs`, shared by the
reality and Survival Rift HUDs:

- A long red **life bar** on the left and a cyan **mana bar** on the right, slanted
  at their outer ends, both draining towards the centre, with a lighter trail that
  shows recent damage before catching up. Values sit by the inner ends.
- A **hexagonal level badge** where they meet, ringed by an **experience arc**
  (with the percentage under the level).
- Under the bars: stamina (left), class-resource pips (right), the **flasks** as
  bottles with their key (Q, R) and charges, and dodge charges as diamonds under the
  badge. The **skill row** (LMB, RMB, 1-4) is its own control below the cluster.

It replaced bottom-left progress bars and a separate skill row: one cluster at the
bottom centre keeps the eye near the character. Custom-drawn rather than built
from ProgressBars so the bars can be slanted, mirrored and trail.


## HUD layout editor **[IMPLEMENTED 2026-10-12]**

Pause (Esc) → **Edit HUD**. The game stays paused, the pause menu steps aside and
every movable panel gets a labelled frame that can be dragged anywhere on screen
(kept at least half on screen). **Reset layout** puts everything back; **Done** or
Esc saves and returns to the pause menu.

- Panels register with `HudLayout.Register(control, id, label, preview?)`, which
  adds a `HudMovable` node that records the panel's default position once the HUD
  has laid itself out and applies the saved offset. The layout is one offset per id
  in `user://hud_layout.cfg`, in the 1280×720 canvas the UI is authored in (the
  stretch mode scales it to any window).
- Movable now: character name, reality info + minimap, boss life, the life/mana
  cluster, skills, notifications, the introduction guide; in the Survival Rift
  level, timer and kills too. Ids are shared, so moving the life cluster or the boss
  bar in a reality moves it in the Survival Rift as well.
- Panels that are usually hidden show a placeholder while editing (a sample boss
  bar, a sample notification) so they can be placed before they are needed.

- **Resizing** (added on request): drag a frame's corner grip, or use the mouse
  wheel over it, to scale the panel from 50% to 200%. Panels scale around their
  centre (the corner follows the cursor) and are nudged back inside the screen if
  growing pushed them off an edge. The size is saved next to the offset
  (`[scales]` in the same file); Reset puts sizes back to 100% too.

Rejected: per-screen layouts (players expect "my life bar is here" everywhere) and
non-uniform stretching (bars and icons would distort).

## Overhead bars **[IMPLEMENTED 2026-10-12]**

A small life bar (and mana bar, where there is mana) floats over the player so
you can watch it without looking down. `UI/OverheadBars.cs` is a world-space child
of the Player, drawn above the world; the life bar keeps the same damage trail as
the HUD and pulses below 30%. Settings → "Life and mana bars over your character"
turns it off.

## Overlay map **[IMPLEMENTED 2026-10-12]**

**Tab** toggles a large, see-through map over the game in realities, Diablo / Path
of Exile style, centred on the player so it can stay open while fighting: explored
floor as a faint wash, walls as bright outlines, the boss gate, the entrance, the
boss arena (or the exit portal once the boss is down), visible monsters (coloured
by rarity, bosses bigger) and the player as an arrow pointing where they move.
The corner minimap stays. `UI/MapOverlay.cs`; the key comes in through `_Input`
(the overlay is hidden most of the time).


## Target frame **[IMPLEMENTED 2026-10-12]**

Replaces the flat boss bar, modelled on AION 2's target frame (`UI/TargetFrame.cs`):

- **Name line:** the monster level in a bronze ring, the name in its rarity colour
  (bosses in ember).
- **The bar:** an ornate frame with arrow-tip caps, a recent-damage trail, phase
  marks at the boss's thresholds (the next one pulses as life closes in), the
  percentage, a rank badge on the right tip (a star for bosses and uniques, a
  diamond for rares) and a central emblem showing the boss's element (flame for
  the Ember Colossus, a skull for the Bone Herald, water for Nereth).
- **Under it:** the monster's modifiers as tags, a **cast bar** that fills until a
  telegraphed attack lands (with the attack's name), and the **ailments** on the
  target (ignite, chill, shock, poison stacks, curse) with their time left.
- **Who it shows:** the boss during its fight; otherwise the monster under the
  mouse (kept for 3 s after the cursor leaves it), or the nearest awake rare or
  unique within ~7 m. So rares get their name, life and modifiers on screen too.

The cast bar needed no per-attack timing: `BossEnemy` records the window of its
pending telegraphs (`Slam`/`Lane`, and the few direct spawns call
`NoteTelegraph`), and the bar runs from the first telegraph to the last impact.
The frame is the "Target / boss life" panel in the HUD editor.

## Hideout HUD **[IMPLEMENTED 2026-10-13]**

The owner found the hideout's explanations unintuitive: a status line top-left, a
two-line wall of key hints along the bottom and a bare "[E] Mirror of Realities"
text. They became pieces that each explain one thing, styled like the rest of the
HUD (dark glass panels, gold accents, the AION-style cluster):

- **Character plate** (top left): class portrait, name, level and class, best tier
  cleared, an XP bar, gold with its coin icon, and a gold pill when there are points
  to spend ("2 passive points to spend ›" opens the tree; "Ascendancy ready at the
  altar ›" walks you there).
- **Menu bar** (top centre): key caps for I, C, K, P, H, ? and Esc with what they do
  underneath. They are buttons too, so the keys are learnt by seeing them; P shows a
  badge with unspent passive points.
- **Town directory** (top right): every station, coloured by its accent, with the
  quarter it's in, the distance and an arrow pointing at it from you. Clicking a row
  walks you there on the navigation mesh; the altar row gets a dot when ascendancy
  points are waiting. Since stations can be moved in edit mode, quarters and
  arrows are computed from where they stand now.
- **E prompt**: a card floating over the station you stand next to: a key cap, the
  station's name in its accent colour and what it does ("Craft", "Open realities",
  "Walk in — endless waves..."). That station's own world label hides while the
  card shows.
- **Life, mana, flasks and skills**: the same cluster and skill row as in realities,
  so the training dummies are a real place to try skills.
- **Town guide** ("Your hideout"): opens by itself on an account's first visit and
  from ? / F1 or the menu bar afterwards. A card per station (picture, what it's
  for, which quarter it's in now, a "Show me" button that walks you there) and the
  controls that matter in town.
- A "Hideout" banner fades in on arrival.

All new panels are movable in Edit HUD (`character`, `hideout_menu`, `town`,
`vitals`, `skills`). The whole HUD hides while the town editor is open.

## Feedback **[IMPLEMENTED 2026-10-13]**

Esc → **Send feedback** opens a form inside the pause menu: kind (**Bug**, **Idea**,
**Feature request**), a one-line title, a description, an optional second field
(steps to reproduce for bugs, "why would it help?" for requests) and a checkbox to
include game info (version from `version.txt` next to the executable, OS and GPU,
window size and voxel view, where the player is, character level and class —
nothing personal). It's shown before sending.

**Open on GitHub** opens the repository's new-issue page in the browser with the
matching **issue form** (`.github/ISSUE_TEMPLATE/bug_report.yml`, `idea.yml`,
`feature_request.yml`) and every field filled in through URL parameters; the player
only presses Submit. The forms apply the labels `bug`, `idea` and `enhancement`
(labels passed in the URL are ignored for non-collaborators, form labels are not),
and `.github/workflows/labels.yml` makes sure those labels exist. **Copy text** puts
the same report on the clipboard as Markdown. Very long reports are trimmed to keep
the URL under GitHub's limit.

Why not create the issue directly from the game: that needs a GitHub token inside the
client, which anyone can extract from a .NET build and use to spam the repo. The
planned next step is a relay (most likely a Discord integration: a webhook into a
forum channel with tags, behind a small relay that holds the secret), which would
let players send feedback without a GitHub account. The form stays the same; only
`Feedback`'s sending step changes.

## HUD and side panels **[IMPLEMENTED 2026-10-13]**

The owner caught the hideout HUD painting over the open inventory (town directory,
menu bar and the mana end of the life/mana cluster all drew on top of it). Two fixes:

- The inventory draws **above every HUD** (layer 12; realities and the Survival Rift
  draw their HUD at 10, the hideout at 4), still under modal panels and the tooltip.
- An open inventory is a **blocker** (`HudLayout.AddBlocker`). Each movable HUD panel
  says what it does when one opens over it (`HudAvoid`): the menu bar and the boss /
  target frame **slide** sideways out from under it; the town directory and the
  reality info and minimap **fade** out; the life/mana cluster **stays** and the
  panel covers its right end, as in Path of Exile. Everything slides back when the
  inventory closes; the shift is temporary and never saved into the HUD layout.
