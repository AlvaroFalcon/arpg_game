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
  bottles with their key (Q, R) and charges, dodge charges as diamonds under the
  badge, and the **skill row** (LMB, RMB, 1-4) below everything.

It replaced bottom-left progress bars and a separate skill row: one cluster at the
bottom centre keeps the eye near the character. Custom-drawn rather than built
from ProgressBars so the bars can be slanted, mirrored and trail.
