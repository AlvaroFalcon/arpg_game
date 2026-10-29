# Quests and the quest log

> **Keys changed 2026-10-29** (docs/design/menus.md -> "Controls"): interact is **F** (was E), pick-up is **G** (was F), flasks are **1/2** (were Q/R), skills are LMB/RMB/Q/E. Older text below may still name the old keys.


**Status: [IMPLEMENTED 2026-10-18]**. Planned from a UX review of the hideout (captures
`review-*.png` in `docs/devlog/screenshots/`), built the same day; "Implementation" near the end
lists what was built where and every place it departs from the plan (small). Captures of the
result: `quests-*.png` (`RunCapture --only=quests`).

Owner brief (translated): *"Some mechanics have no tutorial and lose the player.
When they unlock, the player gets a quest that teaches them."* Six quests to start
with (ascendancy, the first Mirror jewel, Arehucaz, two at the Artificer's forge,
Odran), a "!" on every map and arrow that points at the station, an explanation
when you interact, a reward, and a **quest log on J** that shows the turn-in on the
map and can walk you there.

Related plans from the same review: Odran's skill gem tab (hideout.md -> "Odran's
Skill gems tab"), the Rift's cash-out offer (run.md -> "Cash-out offer"), the
crafting orb tooltip (crafting.md -> "Orb tooltips"). menus.md -> "UX review
2026-10-18" lists all the findings.

## Why (findings)

| # | Severity | What a player says | Where |
|---|---|---|---|
| Q1 | Confusion | "I beat a tier-5 boss, a line flashed by, and nothing else happened." The toast ("Ascendancy trial complete! Visit the altar") lasts 2 s in the middle of loot. Afterwards only an ember dot on the directory's altar row and a gold pill on the character plate say anything. | `review-hideout-hud.png` |
| Q2 | Confusion | "I picked up a jewel. What is it for?" The toast says "socket it at the Mirror"; the Mirror opens on the Realities tab, the jewels live on a second tab with a 70-socket tree and no explanation of what a socket does until you hover it. | `review-mirror-jewels.png` |
| Q3 | Confusion | "Who is the hooded guy in the alley?" Arehucaz only sells tiers you've cleared from 2 up, and that rule appears nowhere until his panel is open. | `review-arehucaz.png` |
| Q4 | Confusion | "What's dust? Why would I break an item?" The forge has two tabs and no guidance; the link between disenchanting and forging is only in this repo's docs. | (crafting captures) |
| Q5 | Confusion | "Where do I get more skills?" The skills panel (K) lists only what you own and never says where gems come from. | `review-skills-panel.png` |
| Q6 | Blocker (UX) | The altar's "Become a Samurai" is **permanent and takes one click** with no confirmation. A quest that leads every player there makes this worse. | AscendancyPanel.Choose |

Q6 is fixed with the quest work (see "The altar's confirmation").

## Concepts

- **Quest**: one lesson about one station. It **unlocks by itself** when the
  thing it teaches becomes available (no "accept" step), has **one objective** (an
  event, sometimes counted), a **reward**, and a **turn-in station** (where the
  thing is done).
- **Status**: `Locked → New → Active → Ready → Done` (plus `Skipped` for veterans,
  see "Migration").
  - **New**: unlocked, explanation not read yet. The station shows a gold **"!"**.
  - **Active**: explanation read, objective not met. The station shows a grey
    **"?"** (small, still).
  - **Ready**: objective met, reward not claimed. The station shows a gold **"?"**
    (pulsing).
  - **Done**: claimed. No marker. Listed under Completed in the log.
- **Scope**: `Character` (per character: ascendancy, Arehucaz, Odran's gems) or
  `Account` (once per account, for account-wide systems: Mirror jewels, the forge).
  Account scope also stops a new character from farming account-wide rewards
  (jewels, dust, orbs) by redoing a two-minute quest.

Why WoW-style "!" / "?": the genre's players read it without a legend, it works at
10 px on a minimap, and "?" for "come back" separates "something new here" from
"something to collect here" with one glyph.

## The six quests

| Id | Title | Station (turn-in) | Scope | Unlocks when | Objective (event) | Reward |
|---|---|---|---|---|---|---|
| `odran_wares` | Odran's wares | Odran (`merchant`) | Character | first hideout visit with the introduction done | buy a gem from the Skill gems tab (`gem_bought`) | 300 gold |
| `forge_disenchant` | Breaking it down | Artificer's Forge (`forge`) | Account | the next hideout visit after `odran_wares` is Done | disenchant an item (`disenchanted`) | 6 Azure Dust, 6 Grey Dust, 100 gold |
| `forge_forge` | A modifier of your choosing | Artificer's Forge (`forge`) | Account | `forge_disenchant` is Done (at once) | forge an item (`forged`) | 1 Crown Orb, 2 Graft Shards |
| `arehucaz_shards` | The shard dealer | Arehucaz (`arehucaz`) | Character | `HighestTierCleared >= 2` (he has something to sell) | buy a Mirror Shard (`shard_bought`) | 1,000 gold, 1 Mirror Shard of your best cleared tier |
| `mirror_jewel` | A jewel for the Mirror | Mirror (`mirror`) | Account | the account has found a jewel (`Jewels.Found >= 1`) | socket a jewel (`jewel_socketed`) | 1 Mirror Jewel, 1,000 gold |
| `ascend` | Choose an ascendancy | Altar of Ascension (`altar`) | Character | `AscendancyPoints > 0 && Ascendancy == null` (the tier-5 boss) | choose an ascendancy (`ascendancy_chosen`) | 1,000 gold, 2 Crown Orbs, 2 Tempest Orbs, 1 Tuning Orb |

Decisions behind the table:

- **Arehucaz unlocks on clearing tier 2, not on the first shard.** The owner said
  "when you get a shard", but a tier-1 boss drops tier-1 shards (65 %), and he sells
  only tiers 2 up to your best cleared tier. A quest at the first shard would ask a
  player to buy something he can't sell them yet. Clearing tier 2 always drops at
  least one shard, so in practice the quest still arrives with a shard in hand.
- **The early quests are staggered.** A new character's first hideout visit already
  opens the town guide. Odran comes first (the gem shop is the most useful thing
  for a level-3 character), the forge on the next visit, so the town never greets
  you with three "!" at once.
- **Odran's objective is a gem purchase**, which needs gold. Level-1 gems cost 50–60
  gold (hideout.md), and the Threshold pays more than that (boss gold 70 + drops);
  test `QUEST-ODRAN-AFFORDABLE` checks it with a fresh character after the
  introduction. If it fails, lower the level-1 prices, not the quest.
- **Forge rewards chain into each other**: the disenchant reward (6 Azure, 6 Grey,
  100 gold) covers an Azure-grade forge (4 Azure + 4 Grey + 60 gold); the forge
  reward (a Crown Orb) turns that magic item rare from the Crafting tab, which
  teaches the third loop without a third quest.
- **The jewel reward adds one to `Jewels.Found`** (the same counter a drop raises),
  so it counts towards the 40 "any boss" jewels. With the tree full (69) it pays
  1,000 gold more instead.
- **The shard reward** is of the character's best cleared tier at turn-in (at least
  2), added with `CharacterData.AddShards`.

## Copy

Short, second person, accurate to the code. `{...}` is filled in at runtime.
Each quest has: the toast line, the explanation (the dialogue), the objective, a
hint (shown under the objective in the log and in the panel strip), and the turn-in
line.

### Odran's wares (`odran_wares`)

- Toast: **New quest: Odran's wares** · Odran the Trader, in the plaza
- Speaker: Odran the Trader
- Explanation:
  > "Welcome, welcome! Gold for your gear, and gear for your gold."
  >
  > **Trade:** right-click an item in your bag to sell it. Anything you sold this
  > visit can be bought back for the same price until you walk away. He also sells
  > four cheap crafting shards: Kindling, Flux, Graft and Ash.
  >
  > **Skill gems:** every skill and support gem in the game, unlocked as your
  > character levels. A bought gem goes straight to your skills (K) at level 1.
  > Skill gems level up as you use them; you can own up to 6 of each support, one
  > per loadout slot.
- Objective: Buy a gem from Odran's Skill gems tab
- Hint: Greyed-out gems unlock at a higher character level.
- Turn-in: "Socket it in the Skills panel (K): click a slot, then the gem."

### Breaking it down (`forge_disenchant`)

- Toast: **New quest: Breaking it down** · the Artificer's Forge, in the smithy
- Speaker: the Artificer
- Explanation:
  > "Gear you'll never wear still has worth, if you break it right."
  >
  > In the **Disenchant** tab, right-click an item in your bag. It turns into dust of
  > its rarity: Grey from common items, Azure from magic, Gilded from rare. Higher
  > item levels give more.
  >
  > Selling gives you gold now. Dust is what the forge spends later.
- Objective: Disenchant an item at the Artificer's Forge
- Hint: The tooltip shows how much dust an item gives while the tab is open.
- Turn-in: "That's enough dust for your first forge. Come back when you're ready."

### A modifier of your choosing (`forge_forge`)

- Toast: **New quest: A modifier of your choosing** · the Artificer's Forge
- Speaker: the Artificer
- Explanation:
  > "Now make something. You pick the modifier; the forge guarantees it."
  >
  > In the **Forge** tab: choose a base, then a modifier it can carry, then a grade.
  > Grey and Azure make a magic item, Gilded and Prismatic a rare one. Better grades
  > give a higher tier of your modifier and more random extras, and cost more dust.
  >
  > How high a tier you can forge depends on the highest reality tier you've
  > cleared, so the forge keeps pace with you.
- Objective: Forge an item
- Hint: Your reward from the last quest pays for an Azure forge.
- Turn-in: "Use the Crown Orb from the inventory's Crafting tab (I) to turn a magic
  item rare. Your new one, perhaps."

### The shard dealer (`arehucaz_shards`)

- Toast: **New quest: The shard dealer** · Arehucaz, by the north gate
- Speaker: Arehucaz
- Explanation:
  > "You have walked through tier {best}. Now we can deal."
  >
  > Arehucaz sells Mirror Shards for gold, but only of tiers you've already cleared:
  > tier 2 up to your best, tier {best}. Tier 1 is always free at the Mirror, so he
  > doesn't sell it. A tier-2 shard costs 79 gold; tier {best} costs {price}.
  >
  > He keeps 5 shards on his cloth. They come back after 10 more cleared realities,
  > or at once for 10,000 gold. Buy from him when bosses haven't dropped the tier you
  > want to run.
- Objective: Buy a Mirror Shard from Arehucaz
- Hint: He never sells a tier above your best cleared one.
- Turn-in: "\"Take one more. You'll be back.\""

### A jewel for the Mirror (`mirror_jewel`)

- Toast: **New quest: A jewel for the Mirror** · the Mirror of Realities, in the plaza
- Speaker: the Mirror of Realities
- Explanation:
  > You found a **Mirror Jewel**. Jewels power the Mirror itself, and every
  > character on your account shares them.
  >
  > Open the Mirror's **Jewels** tab and click a glowing socket next to the Mirror's
  > Heart. Each socket adds a bonus to every Altered Reality you open: more items,
  > rarer items, more shards, more monsters, more gold or more crafting orbs. The
  > large sockets at the end of each branch are stronger.
  >
  > A jewel comes back out for free as long as the rest stay connected, so
  > experiment. Jewels work in Altered Realities, not in the Survival Rift. Reality
  > bosses drop them.
- Objective: Socket a jewel in the Mirror's Jewels tab
- Hint: Sockets you can fill now glow green.
- Turn-in: "The Mirror hums. Here is another jewel to place."

### Choose an ascendancy (`ascend`)

- Toast: **New quest: Choose an ascendancy** · the Altar of Ascension, in the temple
  (shown in the reality, right after the tier-5 boss; it replaces the old
  "Ascendancy trial complete!" toast)
- Speaker: the Altar of Ascension
- Explanation:
  > You defeated a tier-5 guardian, and the altar has woken for you.
  >
  > An **ascendancy** is your class's specialisation. As a {class} you can become
  > {list, e.g. "a Barbarian, a Paladin or a Samurai"}. Each has 8 nodes with
  > effects found nowhere else.
  >
  > You have **4 ascendancy points** now, and a tier-12 guardian grants 2 more: 6
  > points for 8 nodes, so pick the ones that fit your build.
  >
  > **The choice of ascendancy is permanent.** Its nodes can be refunded later for
  > gold.
- Objective: Choose an ascendancy at the Altar of Ascension
- Hint: Read all of them first; you can't change your mind.
- Turn-in: "The path is chosen. Spend your points on its nodes; hover one to read it."

## The player's flow (ascendancy, step by step)

1. The tier-5 boss dies. `RealityController.OnBossKilled` sets the ascendancy
   points and calls `Quests.Evaluate`. `ascend` becomes **New**.
2. **Within the same frame**: the quest toast slides in (top left, under the
   character panel) with the `quest_new` chime. It stays 6 s, longer while hovered.
   Clicking it opens the log on that quest.
3. The player goes home. The hideout's arrival banner reads "Hideout · 1 new quest
   · J opens your quests". The altar has a gold "!" above it in the world, on its
   directory row (whose arrow turns gold), on the minimap and on the big map.
4. The player clicks the directory row (or the log's **Walk there**). The walk
   takes them to the altar. The E prompt reads **Altar of Ascension · Ascend · New
   quest**, with the "!" in the card.
5. **E opens the quest dialogue first**, not the altar. Primary button **Open the
   altar** (Enter, or E after 0.35 s); secondary **Later** (Esc). Reading it with
   the primary button sets the quest **Active** (grey "?").
6. The altar panel opens, with a slim gold quest strip under its title:
   "Quest: Choose an ascendancy · Read all of them first; you can't change your mind."
7. The player clicks "Become a Samurai". The button turns into **"Become a Samurai
   — this is permanent. Click again."** (ember, 3 s). Second click: chosen. The
   `ascendancy_chosen` event makes the quest **Ready**: the strip turns green
   ("Quest complete: close the altar to claim your reward") and the `quest_done`
   chime plays. No toast card covers the panel.
8. The player closes the panel. Because they stand at the turn-in station with a
   Ready quest, **the turn-in dialogue opens by itself**: "QUEST COMPLETE", the
   turn-in line, the rewards as icons. **Claim reward** (Enter / E) grants them
   (`quest_claim` coin sound), shows a toast "+1,000 gold · 2 Crown Orbs · …",
   saves, and closes. No marker remains.

Every quest follows the same steps; only the station and the event differ. If the
objective is met somewhere else (none of the six, but a future "clear a tier-3
reality" quest would), the station shows the gold "?" and E opens the turn-in.

## Markers

One query decides every marker: `QuestBook.MarkerAt(stationUid)`, priority
Ready > New > Active > None. Colours: gold `UiTheme.Gold` for New and Ready, grey
`UiTheme.TextDim` for Active; the glyph is drawn in the theme font with a black
outline on a dark disc (`#0d0a14`, 90 % opaque) ringed in the glyph's colour.

| Where | Size and placement | Behaviour |
|---|---|---|
| **World**, above the station | 30 px disc, 22 px glyph, centred 34 px above the station's name label (`Hideout.LabelHeightOf(def) + 34`) | bobs ±3 px at 1.2 Hz; Ready also pulses its scale 1.0–1.12 at 1.5 Hz. Hidden while the E prompt shows for that station (the card carries it). `QuestMarker2D : Node2D` child of the station node, ZIndex 10, so the voxel view's 2D overlay shows it too. |
| **E prompt card** | 22 px disc at the card's right edge | verb line gains " · New quest" / " · Quest ready to turn in" (Active: " · Quest in progress") |
| **Town directory row** | 16 px disc at x = W − 46 (where the altar's ember dot is today); the row's arrow is drawn gold and 20 % larger | the quest glyph wins over the ascendancy-points dot (the same station) |
| **Minimap** | 7 px disc, 10 px glyph, 9 px above the station's dot | no animation (it's tiny) |
| **Big map** | 9 px disc pinned to the badge's top-right (offset (0.7r, −0.7r)) | the name label gets a gold suffix " — New quest" / " — Turn in"; the hover header adds "· Quest: {title}" |
| **Quest tracker** (HUD) | see below | |
| **Menu bar** | a new **J · Quests** chip | ember badge = New + Ready count (like P's passive points) |

## The quest dialogue

`UI/QuestDialog.cs`, a `ModalPanel` (pauses, joins the panel stack, Esc closes) with
`FrameSize` 640×420, centred on the 1280×720 canvas.

```
┌──────────────────────────────────────────────────────── 640 ─┐
│ [72×72 portrait]  NEW QUEST                (12 px, gold caps)  │
│                   Choose an ascendancy     (26 px, gold)       │
│                   Altar of Ascension       (14 px, accent)     │
│ ───────────────────────────────────────────────────────────── │
│ Explanation paragraphs, 16 px, wrapped at 580 px; scrolls past │
│ 220 px (none of the six needs it at 720p).                     │
│                                                                │
│ OBJECTIVE  Choose an ascendancy at the Altar of Ascension      │
│            Read all of them first; you can't change your mind. │
│ REWARD     [coin] 1,000  [crown] ×2  [tempest] ×2  [tuning] ×1 │
│                                    [Later (Esc)] [Open the altar] │
└────────────────────────────────────────────────────────────────┘
```

- Portrait: the station's map icon (`assets/ui/mapicons/map_<id>.png`, scaled 2×
  with nearest filtering) for the Mirror and the altar; NPC sprites for people
  (Odran `UiTheme.Portrait("paladin")`, the Artificer `"sage"`, Arehucaz
  `ItemArt.ForProp("props/arehucaz")`). No new art.
- **New mode**: buttons "Later (Esc)" (plain, 140 px) and the primary (260 px,
  `UiTheme.PrimaryButton`): "Open {station}" / for Odran "Open the Skill gems tab" /
  for the forge "Open Disenchant" or "Open Forge" / for the Mirror "Open the Jewels
  tab". The primary marks the quest read and opens the station on that tab
  (`QuestDef.OpenTab`).
- **Turn-in mode**: header "QUEST COMPLETE" in `UiTheme.Experience` green, the
  turn-in line in place of the explanation, rewards at 32 px, buttons "Later (Esc)"
  and primary **Claim reward**. After claiming it closes; it does not open the
  station (you just came from it).
- Keys: Enter = primary; E = primary after a 0.35 s guard (so the E that opened it
  can't also confirm); Esc = Later.
- Two quests at one station (not with the six, possible later): Ready first, then
  New; after one closes, E again shows the next.

## The quest toast

`UI/QuestToast.cs`: a `CanvasLayer` at layer 15 (above the HUDs at 4–10 and the
inventory at 12, under modal panels at 20, so a station panel opened while a card
shows covers it) holding a queue of cards.

- Card 360×76: a 36 px disc with the glyph ("!" gold for new, a drawn check mark in green for done,
  the coin icon for a claimed reward), a 12 px caps line ("NEW QUEST" / "QUEST COMPLETE" /
  "REWARD"), the title at 17 px, a 13 px dim line ("At the Altar of Ascension · J
  for details" / "Close the altar to claim your reward" / "+1,000 gold · 2 Crown
  Orbs").
- **Where**: with no panel open, top left. In the hideout it sits 8 px under the
  quest tracker (which gains the quest's row at the same moment, so the toast points
  at where the quest now lives); in realities, 10 px under the reality HUD's
  character panel (read from its rect, since it can be moved in Edit HUD).
  **With a station panel open there is no card**: the objective is met inside that
  panel, so the feedback goes where the player acted, the panel's quest strip
  turning green, plus the `quest_done` sound. (A card at the top or bottom would
  cover the Mirror's title or its jewel tree, the largest panel at 1130×650.) If a
  quest changes while some other modal without a strip is open, its card waits until
  the modal closes.
- Slides in from the left (or drops from the top) in 0.18 s, stays 6 s (hover
  pauses), fades in 0.4 s. One at a time, 0.3 s apart.
- Click: opens the log on that quest (not in the Survival Rift).
- Deferred while the town guide is open, and in a reality while a level-up or
  results screen owns the pause.
- Sounds (`Core/Sfx.cs`, procedural like the rest):
  - `quest_new` (unlock): `Build(0.55f, t => (Arp(t, new[] { 659f, 988f }, 0.12f) * 0.8f + Sine(t, 1976f) * 0.15f) * Env(t, 0.55f) * 0.35f)`, played at −4 dB.
  - `quest_done` (objective met): `Build(0.8f, t => (Arp(t, new[] { 523f, 659f, 784f, 1047f }, 0.08f) + Sine(t, 262f) * 0.2f) * Env(t, 0.8f) * 0.32f)`.
  - `quest_claim` (reward): `Build(0.6f, t => (Sine(t, 1800f + 500f * Mathf.Sin(t * 70f)) * 0.5f + Noise() * 0.08f) * Env(t, 0.6f) * 0.3f)`.

## The quest log (J)

`UI/QuestLogPanel.cs`, a `ModalPanel` with `FrameSize` 1010×600. J toggles it in the
hideout and in realities (not in the Survival Rift, whose keyboard belongs to the
fight and the cards). New input action `toggle_quests` (physical J) in
`project.godot`, added to the controls table in menus.md.

```
┌ Quests ─────────────────────────────────────────────────────────── [Close (Esc)] ┐
│ ┌ 320 ──────────────────┐ ┌ 640 ───────────────────────────────────────────────┐ │
│ │ READY TO TURN IN (1)  │ │ Choose an ascendancy                 (24 px, gold)  │ │
│ │ ? A jewel for the Mir │ │ From the Altar of Ascension · Temple (14, accent)   │ │
│ │ ACTIVE (2)            │ │ Explanation (15 px, wrapped, scrolls past 180 px)   │ │
│ │ ! Choose an ascendancy│ │ OBJECTIVE  ☐ Choose an ascendancy at the altar      │ │
│ │ ? The shard dealer    │ │            Read all of them first; …                │ │
│ │ COMPLETED (3)      ▸  │ │ REWARD     [icons]                                  │ │
│ │                       │ │ TURN IN    ┌ town map 300×190 ┐ Temple · 23 m       │ │
│ │                       │ │            │ (altar ringed)   │ [ Walk there ]      │ │
│ └───────────────────────┘ └────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────────────────┘
```

- **List** (320 px): sections Ready, Active (New and Active together, New first),
  Completed (collapsed by default, ▸ expands). Row 48 px: the marker glyph, title
  16 px, station 12 px dim with a 3 px accent bar like the directory rows.
- **Detail** (640 px): title; "From {station} · {quarter}"; the explanation; the
  objective as a checkbox line (☑ and "Done: turn it in at {station}" once Ready);
  the hint; rewards with icons ("Claimed" in dim text for Done quests); **Turn in**:
  a `TownMap` in a new highlight mode (`Highlight = uid`: the other stations dim to
  40 %, the target ringed in gold and pulsing, the player's arrow, the walk route if
  one is under way) at 300×190, the quarter and distance, and the primary **Walk
  there**.
- **Walk there** closes the log and calls `Hideout.WalkTo(uid)`, the station walk
  the directory uses. The turn-in strip (map and button) sits in a footer outside the detail's
  scroll, so the button is always on screen: inside the scroll a long explanation pushed
  it below the fold and a click could not reach it (fixed 2026-10-23; `QUEST-LOG-CLICK-WALKS`
  clicks it for real and `QUEST-LOG-WALK-ARRIVES` walks to every quest's station). In a reality the map is replaced by "In your hideout: the
  Temple" and the button is disabled with "Back in your hideout".
- Checkboxes, the section arrow and check marks are drawn (`_Draw`), not typed:
  the theme's fallback font has no ☐ ☑ ▸ ✓ glyphs (the same reason `UI/Spinner` is
  drawn). The "!" and "?" are plain ASCII.
- Keys: J or Esc closes; ↑/↓ moves the selection; Enter = Walk there. Opens on the
  quest a toast or tracker row was clicked for, otherwise on the first Ready, else
  the first New.
- **Empty state**: "No quests yet. A quest appears when something new opens up in
  your town, and teaches you how it works." The list shows the Completed section
  if there is one.
- Turning in is never done from the log: you claim at the station. That's what makes
  the "?" on the map mean something, and Walk there makes it one click.

## The quest tracker (hideout HUD)

Under the character plate (x 14, y 164), 286 px wide (the plate's width), dark
glass with a gold top border like the plate. Header "QUESTS" 12 px with a J key
cap; up to 3 rows of 26 px (glyph 14 px, title 13 px, station 11 px dim at the
right); a fourth line "+2 more · J" if needed. Clicking a row opens the log on that
quest. Hidden when nothing is New, Active or Ready. `HudLayout.Register(tracker,
"quests", "Quests")` so it can be moved in Edit HUD; it stays visible when the
inventory opens (it's on the left).

Why a tracker: the log is behind a key, and players need to see that a quest is
waiting without opening anything. It also shows the shape of the system: rows of
"!" and "?" teach the two states.

## E prompt and station panels

| Station has | E does |
|---|---|
| nothing | opens the station panel (as today) |
| a **New** quest | opens the quest dialogue; its primary button opens the panel on the right tab |
| an **Active** quest | opens the panel, with the quest strip under its title |
| a **Ready** quest | opens the turn-in dialogue |

- **The quest strip**: a new optional line in `ModalPanel`, `SetQuestStrip(string?
  text, bool done)`: 28 px tall, gold text on a dark gold-tinted band under the
  header ("Quest: Disenchant an item · The tooltip shows how much dust an item
  gives"); turns green on Ready ("Quest complete: close the forge to claim your
  reward"). The station panels set it from `QuestBook` on `Refresh`.
- **Auto turn-in on close**: when a station panel closes and that station has a Ready
  quest, the turn-in dialogue opens right away (deferred one frame so the panel
  stack settles). This is the normal path for all six quests, since every
  objective happens inside its station's panel.
- The prompt's own `PromptText` (tests) includes the quest suffix.

## The altar's confirmation (Q6) **[IMPLEMENTED 2026-10-18]**

Built ahead of the quests: `AscendancyPanel.Choose(id)` returns whether it chose; the
armed button reads "Become a {name}: this is permanent. Click again." in ember for
`ConfirmSeconds` (3 s), and `ascendancy_chosen` is raised through `GameEvents` on the
confirmed click (`GameEvents.AscendancyChosen`, key = the id). `ALTAR-CONFIRM` is in
`EndgameSmokeTest`.

`AscendancyPanel.Choose` gets a two-step confirm, like character deletion: the first
click turns the button into "Become a {name}: this is permanent. Click again." in
ember for 3 s; the second click within that window chooses. A Cancel is not needed:
waiting or clicking another card resets it. Test `ALTAR-CONFIRM`.

## Outside the hideout

- **Realities**: quests can unlock (tier-5 boss, first jewel, tier-2 clear) and the
  toast shows there; J opens the log (read-only turn-in, no walking). No markers:
  realities have no stations.
- **Survival Rift**: no quest UI. A quest that would unlock there (none of the six)
  waits for the next `Evaluate` in the hideout.
- **Co-op**: quests are local to each player's profile. Nothing goes over the
  network.

## Data model and code

New folder `scripts/Quests/`, namespace `ArpgGame.Quests`. Plain C# except the
facade, so `QuestBook` can be unit-tested headless without a scene.

```csharp
public enum QuestScope { Character, Account }
public enum QuestStatus { Locked = 0, New = 1, Active = 2, Ready = 3, Done = 4, Skipped = 5 }
public enum QuestMarker { None, Active, New, Ready }          // priority order
public enum RewardKind { Gold, Currency, Shard, Jewel, Gem }

public sealed record QuestReward(RewardKind Kind, string Id = "", int Amount = 1);
// Currency: Id = an orb or dust id (Crafting.Find). Shard: Id = "best" (best cleared tier, min 2)
// or a tier number. Gem: Id = skill/support id, Amount = 1. Jewel: Amount jewels.

public sealed class QuestDef
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Station { get; init; } = "";          // hideout station uid ("altar")
    public string Speaker { get; init; } = "";
    public QuestScope Scope { get; init; }
    public string? After { get; init; }                  // prerequisite quest id (must be Done)
    public bool AfterNextVisit { get; init; }            // ...unlock only on a hideout_entered event (see below)
    public Func<QuestContext, bool> Unlock { get; init; } = _ => true;
    public Func<QuestContext, bool>? AlreadyDone { get; init; }   // migration: Skipped
    public string Event { get; init; } = "";             // QuestEvents.*
    public Func<QuestEvent, bool>? Match { get; init; }  // e.g. a tier filter
    public int Count { get; init; } = 1;
    public Func<QuestContext, string> Explain { get; init; } = _ => "";
    public string Objective { get; init; } = "";
    public string Hint { get; init; } = "";
    public string TurnIn { get; init; } = "";
    public string Toast { get; init; } = "";              // "Odran the Trader, in the plaza"
    public string? OpenTab { get; init; }                 // "gems", "disenchant", "forge", "jewels"
    public IReadOnlyList<QuestReward> Rewards { get; init; } = Array.Empty<QuestReward>();
}

public sealed class QuestProgress { public QuestStatus Status { get; set; } public int Count { get; set; } public long Since { get; set; } }

public readonly record struct QuestEvent(string Id, int Value = 0, string Key = "");

public sealed class QuestContext   // what predicates read
{
    public CharacterData Character { get; init; } = null!;
    public World.JewelData? Jewels { get; init; }        // account (Profile.Jewels)
}

public interface IRewardSink   // the hideout's PlayerRewardSink: Wallet + CharacterData + Profile
{
    void Gold(long amount); void Currency(string id, int amount);
    void Shards(int tier, int amount); bool Jewel(int amount); void Gem(string id, bool support);
}

public static class QuestEvents
{
    public const string HideoutEntered = "hideout_entered", BossKilled = "boss_killed",
        JewelFound = "jewel_found", ShardFound = "shard_found", AscendancyChosen = "ascendancy_chosen",
        JewelSocketed = "jewel_socketed", ShardBought = "shard_bought", Disenchanted = "disenchanted",
        Forged = "forged", GemBought = "gem_bought", ItemSold = "item_sold";
}

public static class QuestCatalog { public static readonly IReadOnlyList<QuestDef> All; public static QuestDef? Get(string id); }

public sealed class QuestBook   // pure rules over two progress maps
{
    public QuestBook(Dictionary<string, QuestProgress> character, Dictionary<string, QuestProgress> account);
    public IReadOnlyList<QuestDef> Evaluate(QuestContext ctx);                  // returns newly New
    public IReadOnlyList<QuestDef> Notify(QuestContext ctx, QuestEvent e);      // returns newly Ready (+ chained New)
    public void MarkRead(string id);                                            // New -> Active
    public bool TurnIn(string id, QuestContext ctx, IRewardSink sink, out IReadOnlyList<string> lines);
    public QuestStatus StatusOf(string id);
    public QuestMarker MarkerAt(string station);
    public QuestDef? FirstAt(string station);                                   // Ready, then New, then Active
    public IEnumerable<(QuestDef Def, QuestProgress Progress)> Log();
}
```

**The Godot facade** `QuestService` (static; planned as `Quests`, see Implementation):

- `QuestService.Evaluate(Node ctx)` and `QuestService.Raise(Node ctx, string eventId, int value = 0, string key = "")`
  build the context from `Profile.Get(ctx)` (no-op without the autoload, so tests
  that run scenes directly don't break), call the book, save the profile when
  anything changed, play the sound and emit the change.
- Changes are announced on the `Profile` autoload as a Godot signal
  `QuestChanged(string id, int status)`. Subscribers (HUDs, toast, tracker,
  panels) connect in `_EnterTree` and disconnect in `_ExitTree`, because the
  autoload outlives every scene.
- Evaluate is cheap (six predicates); it runs on hideout entry and after every
  event, never per frame. Markers read `MarkerAt` per frame, which is a dictionary
  scan of six entries.

### Where the events come from

**Hooks already in the code (2026-10-18):** `Core/GameEvents.cs` is a static bus,
`GameEvents.Raised(string eventId, int value, string key)`, with the ids as constants.
Raised today: `gem_bought` (`GameEvents.GemBought`; `GemShop.TryBuy`; key = gem id, value 1
for a support), `disenchanted` (`ArtificerPanel.Disenchant`; value = dust, key = rarity),
`forged` (`ArtificerPanel.Forge`; key = rarity of the result), `shard_bought`
(`ShardVendorPanel.Buy`; value = tier), `ascendancy_chosen` (`AscendancyPanel.Choose`;
key = id), `jewel_socketed` (`MirrorPanel.ClickJewel`; key = node id), `item_sold`
(`MerchantPanel.Sell`; value = gold). `boss_killed`, `jewel_found`, `shard_found` and
`hideout_entered` below are raised too since the quests were built. The facade (`QuestService`)
listens to the bus through the `Profile` autoload (`_EnterTree` / `_ExitTree`) instead of
having calls at each site.

| Event | Raised in | When |
|---|---|---|
| `hideout_entered` (and `Evaluate`) | `Hideout._Ready`, after the HUD exists | every hideout load. `AfterNextVisit` quests unlock only during this event: their prerequisite is turned in at a station, i.e. after this visit's event, so the next one is necessarily a later visit, even across a restart. No counter to save. |
| `boss_killed` (value = tier), then `Evaluate` | `RealityController.OnBossKilled`, after `HighestTierCleared` and the ascendancy points are updated (~line 797) | unlocks `ascend` (tier ≥ 5) and `arehucaz_shards` (tier ≥ 2). The old "Ascendancy trial complete!" toast goes; the quest toast replaces it. Not in the tutorial. |
| `jewel_found`, then `Evaluate` | `RealityController.OnPickup`, `PickupKind.Jewel`, after `Jewels.Found++` | unlocks `mirror_jewel`; the toast keeps its "(n/69)" text and the quest toast follows it |
| `shard_found` (value = tier) | `OnPickup`, `PickupKind.Shard` | none of the six; for later quests |
| `ascendancy_chosen` (key = id) | `AscendancyPanel.Choose`, after `c.Ascendancy = id` (the confirmed click) | |
| `jewel_socketed` (key = node) | `MirrorPanel.ClickJewel`, when `changed && !remove` | |
| `shard_bought` (value = tier) | `ShardVendorPanel.Buy`, when `ShardVendor.TryBuy` returned true (~line 149) | |
| `disenchanted` | `ArtificerPanel.Disenchant`, after the dust is added | |
| `forged` | `ArtificerPanel.Forge`, after `Player.Inventory.TryAdd(item)` | |
| `gem_bought` (key = id) | Odran's Skill gems tab (`GemShop.TryBuy` succeeded) | |
| `item_sold` | `MerchantPanel.Sell` | none of the six; cheap to add now |

Rewards are granted through `PlayerRewardSink` (hideout): gold and orbs/dust into
`Player.Wallet` (the source of truth while a scene runs; orbs land in
`Profile.Currency` on capture), shards with `CharacterData.AddShards`, jewels with
`Profile.Jewels.Found++` (capped at `JewelTree.Sockets`; when capped, `Jewel()`
returns false and the book pays 1,000 gold instead), gems with `GemLoot.Collect`.
Then `Profile.CaptureFrom(Player)`, which saves.

## Persistence and migration

- `CharacterData.Quests : Dictionary<string, QuestProgress>`; `Profile.Quests`
  (account) likewise.
- `SaveCharacter.Quests` and `SaveFile.Quests` as nullable
  `Dictionary<string, SaveQuest>` (`SaveQuest { int S; int C; long T; }`). The save
  version stays 2: the fields are additive and absent means empty.
- On load, unknown quest ids are dropped and statuses clamped to 0–5.
- **Veterans**: the first `Evaluate` for a quest with no record runs `AlreadyDone`
  before `Unlock`; true means `Skipped` (no marker, no reward, not listed). Rules:
  `ascend`: `Ascendancy != null`. `mirror_jewel`: `Jewels.Socketed.Count > 0`.
  `arehucaz_shards`: `ShardVendorStock < StockSize || ShardVendorSince > 0` (he has
  sold to them before). The others have no record to check and simply unlock.
  Default chosen: no retroactive rewards (open question 2).
- **Co-op**: `Profile.SerializeCharacter` (the join payload) includes the new field.
  `ValidateCharacterJson` must cap it: at most 32 entries, ids ≤ `NetLimits.IdChars`,
  status 0–5, count 0–999, otherwise "bad quest data". The host ignores quests.
- Deleting a character deletes its quests; account quests stay.
- `Profile.CreateTestCharacter` (admin) leaves quests empty, so testers see them.

## Acceptance criteria

1. Killing a tier-5 boss with a character that has no ascendancy shows the quest
   toast within one frame of the kill, with the `quest_new` sound.
2. In the hideout, the altar shows "!" in the world, on its directory row (gold
   arrow), on the minimap, on the big map and in the E prompt; the tracker lists the
   quest; the J chip shows a badge of 1.
3. E at the altar opens the quest dialogue, not the altar; its primary opens the
   altar with the quest strip; the marker becomes a grey "?".
4. Choosing an ascendancy takes two clicks; the second makes the quest Ready (green
   strip and `quest_done`, within the same frame).
5. Closing the altar opens the turn-in dialogue; Claim adds exactly 1,000 gold, 2
   Crown, 2 Tempest and 1 Tuning Orb, and the quest is Done with no marker left.
6. J lists the quest in the right section with objective, rewards, turn-in station
   and map; Walk there closes the log and starts a walk to that station.
7. Account quests completed on one character never appear on another; character
   quests appear on each character.
8. Saves round-trip every status; a save without the field loads; veterans get
   `Skipped` per the rules; a co-op character payload with bad quest data is refused.
9. No quest UI appears in the Survival Rift.

## Test plan (headless)

New `scenes/tests/QuestSmokeTest.tscn` (+ `scripts/tests/QuestSmokeTest.cs`), lines
mirrored to `user://test_results/QuestSmokeTest.txt` since the hideout uses
navigation and may hang at shutdown. Checks:

| Check | What |
|---|---|
| `QUEST-CATALOG-VALID` | ids unique; every `Station` is a station uid in `HideoutCatalog`; every currency reward id exists in `Crafting`; every quest has toast/explanation/objective/turn-in text |
| `QUEST-UNLOCK-ASCEND` | character with 4 points, no ascendancy: `Evaluate` → New; `MarkerAt("altar")` = New |
| `QUEST-SKIP-VETERAN` | ascendancy already chosen → Skipped, no marker, not in `Log()` |
| `QUEST-NOTIFY-READY` | `ascendancy_chosen` → Ready; `MarkerAt` = Ready |
| `QUEST-TURNIN-ONCE` | `TurnIn` pays the table's rewards into a test sink; a second `TurnIn` returns false and pays nothing |
| `QUEST-ACCOUNT-SCOPE` | `forge_disenchant` Done on character A; character B's book never lists it |
| `QUEST-CHAIN-FORGE` | turning in `forge_disenchant` makes `forge_forge` New at once; `odran_wares` Done makes `forge_disenchant` New only after the next `hideout_entered` |
| `QUEST-AREHUCAZ-TIER2` | `HighestTierCleared` 1 with a tier-2 shard → Locked; 2 → New; the shard reward is of the best tier |
| `QUEST-JEWEL-CAP` | tree full: the jewel reward pays 1,000 gold instead and `Found` doesn't exceed 69 |
| `QUEST-PERSIST` | `SaveJson`/`LoadJson` round trip keeps every status and count; a save without `Quests` loads with empty books; unknown ids dropped |
| `QUEST-VALIDATE-COOP` | `ValidateCharacterJson` refuses 1,000 quest entries and an over-long id |
| `QUEST-HIDEOUT-MARKERS` | hideout with `ascend` New: the directory row reports the marker, `TownMap` reports it for the altar, `PromptText` at the altar contains "New quest", the tracker lists 1 row, the J chip badge is 1 |
| `QUEST-E-DIALOG-FIRST` | E at the altar opens `QuestDialog`, not `AscendancyPanel`; primary → `AscendancyPanel.IsOpen`, status Active |
| `QUEST-AUTO-TURNIN` | Ready while the forge panel is open; closing it opens the turn-in dialogue the next frame |
| `QUEST-LOG` | J opens the log; sections and counts right; the empty state text with no quests; Walk there closes it and `Hideout.WalkingTo == "altar"` after two physics frames |
| `QUEST-TOAST` | an unlock shows a toast card on the same frame and requests `quest_new`; deferred while the guide is open |
| `QUEST-ODRAN-AFFORDABLE` | a new character after the Threshold (scripted clear) has at least the cheapest gem's price |
| `ALTAR-CONFIRM` | one click on "Become a…" doesn't choose; a second within 3 s does |
| `QUEST-NO-RIFT-UI` | the Rift scene has no toast, tracker or J binding |

`RunCapture --only=quests` (new mode) for docs: the tier-5 toast in a reality, the
hideout with markers (world, row, minimap), the big map with a marker, the E prompt
with "New quest", the dialogue, the altar with the strip, the turn-in dialogue, the
log (active and empty), the tracker.

## Implementation **[2026-10-18]**

Built as planned: the six quests, the rules, the markers, the dialogue, the toast, the tracker,
the log, the migration and the co-op caps. Where things live:

| What | Where |
|---|---|
| Model, catalog (all copy), rules | `scripts/Quests/QuestModel.cs`, `QuestCatalog.cs`, `QuestBook.cs` |
| Godot facade and the hideout's reward sink | `scripts/Quests/QuestService.cs` (`QuestService`, `PlayerRewardSink`) |
| Saves | `CharacterData.Quests`, `Profile.Quests`, `scripts/Core/Profile.Quests.cs`; caps in `Profile.Validate.cs` (`NetLimits.MaxQuests` 32, `MaxQuestCount` 999) |
| Events | `GameEvents` (new: `boss_killed`, `jewel_found`, `shard_found`, `hideout_entered`); `Profile` listens in `_EnterTree` and feeds `QuestService.Raise` |
| World marker, drawing helpers | `UI/QuestMarker2D.cs`, `UI/QuestGlyphs.cs` (marker disc, check mark, checkbox, section arrow, `**gold**` markup) |
| Dialogue, toast, tracker, log | `UI/QuestDialog.cs`, `QuestToast.cs`, `QuestTracker.cs`, `QuestLogPanel.cs`, `QuestRewardRow.cs` |
| Station panels | `ModalPanel.StationUid`, `SetQuestStrip`, `RefreshQuestStrip` (each station panel overrides `StationUid`) |
| Hideout glue | `Hideout.Interact`, `TryAutoTurnIn`, `OpenStation`, the arrival banner; `HideoutHud` (prompt, directory, J chip, tracker); `TownMap` (markers, `Highlight` mode) |
| Sounds | `Sfx` (`quest_new`, `quest_done`, `quest_claim`; `Sfx.LastRequested` / `RequestCount` for tests) |
| Input | `toggle_quests` (physical J) in `project.godot`, polled by `CharacterPanels` (the Rift has none) |

How it behaves, in short:

- Every status change goes through `QuestService.Track`: snapshot the six statuses, run the book,
  and for what changed save the profile, request the sound and emit `Profile.QuestChanged`.
  Nothing polls the book except the markers (a six-entry scan).
- E at a station: Ready opens the turn-in, New the explanation (its primary marks the quest
  Active and opens the station on `OpenTab`), anything else the panel. A station panel that
  closes with its quest Ready opens the turn-in one frame later (skipped if another panel opened
  meanwhile, or the player is leaving).
- Claiming pays through `PlayerRewardSink` into `Player.Wallet` (orbs, dust, gold), the character
  (shards, gems) and the account (jewels, capped at 69; a full tree pays 1,000 gold instead),
  then `Profile.CaptureFrom(Player)`.

### Deviations from the plan

- **The facade is `QuestService`, not `Quests`**: a class with the namespace's name would hide the
  `ArpgGame.Quests` namespace from every file in `ArpgGame.*`.
- **Account quests leave a Done mark on the character that finished them** (`QuestBook.TurnIn`),
  and `Log()` lists an account quest that is Done only for that character. That is how "completed
  on one character never appears on another" (criterion 7) holds while the status itself stays
  shared (a second character is never offered it again).
- **`QuestDef.OpenText`** (the dialogue's primary button text) was added to the model; `Toast` is
  the place line and the card appends " · J for details" when it fits in 36 characters.
- **The copy's `**word**` is gold, not bold** (the theme font has no bold face): `QuestGlyphs.Rich`.
  Paragraph breaks are short gaps. The dialogue's text area is 270 px, so all six explanations fit
  at 720p without scrolling (the plan allowed scrolling past 220).
- **Log detail**: the explanation scrolls (about 130 px are left beside the 190 px map). Done
  quests show a "Turned in" line with the turn-in text instead of the map.
- **Big map**: the "New quest" / "Turn in" tag is a small gold second line above the station's
  name instead of a suffix on the same line (the long suffix ran into neighbouring badges). The
  hover header adds "· Quest: {title}" and replaces the static hint while it shows.
- **Realities have no character panel**, so the toast's card sits 158 px under the origin of the
  loot toasts ("Notifications" in Edit HUD), under their six lines; it follows them if moved.
- **A card tells the truth**: a Ready card is dropped when its station's panel is the topmost one
  (decided when it is queued, not only when it would show), and any card whose quest has since
  moved on (read, claimed) is dropped when its turn comes.
- **First visit**: `hideout_entered` is raised after the town guide opens (both deferred, in that
  order), so the unlock toast waits for the guide instead of expiring behind it.
- **The dialogue** hides `ModalPanel`'s own header (`ShowHeader`); Enter is `ui_accept` (Enter and
  Space), its buttons take no focus so a key never presses one twice.
- **Veterans**: `AlreadyDone` is checked on every evaluation while a quest has no record, not
  once; with no record in between it is the same thing.
- **Tests**: `ALTAR-CONFIRM` stayed in `EndgameSmokeTest` where it was built, and
  `QUEST-ODRAN-AFFORDABLE` plays the real introduction (its boss's gold against the cheapest gem).
  Added: `QUEST-ARRIVAL`, `QUEST-ASCEND-READY`, `QUEST-FORGE-FLOW`, `QUEST-BOSS-TOAST`,
  `QUEST-TURNIN-NEEDS-READY`, `QUEST-EXPLAIN-TEXT`, `CHARACTER-QUEST-CAPS` (SecuritySmokeTest) and
  `CHARACTER-QUESTS-JOIN` (CoopSmokeTest). The suite runs the real hideout, a real tier-5 boss kill
  and the Rift scene against an emptied profile and puts the player's account back after.

### Known limits

- Rewards are the plan's first guesses (open question 1).
- `QuestToast` has no card for a quest that changes while a *non-station* modal is open until that
  modal closes (by design); several unlocks at once queue one card per quest, six seconds each.
- The HUD (tracker, directory, chips) only updates while the game runs, so a quest that changes
  while a station panel is open shows on them the moment it closes.

## Alternatives considered

- **A quest giver NPC with a list** (one board in the plaza). Rejected: the lesson
  belongs where the thing is done, and a "!" over each station teaches the town.
- **Auto-granting rewards on completion** (no turn-in). Simpler, but the owner
  asked for a turn-in place, and "?" on the map is a reason to walk back past the
  station you just learnt.
- **Explaining in tooltips and the town guide only.** The guide already exists and
  wasn't enough: players read it once, before any of these systems matter.
- **One quest system for the introduction too** (The Threshold). Later: its guide is
  a polled step list that works (tutorial.md); moving it onto `QuestBook` is a good
  second step once the book exists.

## Open questions (owner)

1. Rewards are first guesses (1,000 gold is a lot at tier 2 and little at tier 12).
   Owner: keep them until they've been played.
2. ~~Veterans: retroactive rewards?~~ Owner (2026-10-17): no — as built, quests
   appear when their requirements are met and veterans' are skipped.
3. Should the log also track non-tutorial goals ("Clear tier 10", "Fill a jewel
   branch")? The model supports counted objectives and `boss_killed` with a tier
   filter already.
4. Spanish text: the UI is English for now (classes.md open question 1).
