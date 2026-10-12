# The introduction: The Threshold **[IMPLEMENTED 2026-10-12]**

## What it is

Every new character starts in **The Threshold**, a short Altered Reality built to
teach the game one thing per room, instead of appearing in the hideout with no
idea what to do. It plays like any reality (same HUD, loot, boss gate and exit
portal); a guide panel on the left shows one objective at a time:

| # | Objective | Finishes when |
|---|---|---|
| 1 | Find your feet: WASD, or Ctrl+click to walk to the cursor | the player has moved ~7 m from the camp |
| 2 | Sprint (Shift, stamina bar) and dodge (Space) | the player dodges |
| 3 | First blood: aim with the mouse, LMB uses the class skill | 3 kills, or the yard's pack is dead |
| 4 | Loot: hover to read, click to pick up; Z toggles names | the guide's guaranteed drop is in the bag (or worn) |
| 5 | Gear up: I opens the inventory, right-click equips; the tooltip compares | that item is equipped |
| 6 | Grow stronger: P passive tree, K skill gems, C character sheet (a gem drops) | the cache's magic pack is dead |
| 7 | The guardian: the gate seals; red markings show where attacks land | the boss is dead |
| 8 | Home: E at the portal; the Mirror opens new realities from shards | (leaving) |

- Layout `threshold` (`LayoutCatalog.Tutorial`): camp → yard → cache → antechamber
  → a round seal room, in a straight line. It is not in `LayoutCatalog.Layouts`, so
  the Mirror never rolls it.
- Tutorial mode (`RealityLaunch.Tutorial` → `RealityController.IsTutorial`): every
  pack marker spawns (3 normals in the yard, 2 magic monsters in the cache), monsters
  and the guardian have 60% life and damage, 99 portals, no shard modifiers.
- `CharacterData.TutorialDone`: false for characters created after this feature,
  true for everything older (missing in the save → true). Leaving the reality any
  way — finishing, Esc → "Skip the introduction", or collapsing — sets it, so the
  introduction is never forced twice. Character select sends a character that
  still owes it to the reality instead of the hideout.

## Why

The owner's friends would land in the hideout with a Mirror, an altar and a portal
and no context. A playable introduction beats a wall of text: each step only
finishes when the player has done the thing, and the guide hands out what a lesson
needs (an item to equip, a gem) at the moment it is taught.

Reusing the reality controller instead of a bespoke scene means the tutorial
teaches the real thing — same HUD, same loot, same boss gate — and costs a layout
and a ~250-line guide rather than a second game mode.

## Alternatives considered

- **Tooltips in the hideout only.** Cheaper, but nothing to practise on.
- **A separate tutorial scene.** Would drift from the real game as it changes.

## Open questions

- Steps are polled each frame; fine for eight steps, not a general quest system.
- No voice-over or arrows pointing at the HUD yet.
