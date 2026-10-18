# Crafting, merchant and disenchanting

**Status: [IMPLEMENTED 2026-10-13] first pass.** The owner asked for "crafting like
Path of Exile, with different currencies that change the item in different ways —
invent something and we'll improve it tomorrow", plus selling gear for gold at a
merchant, disenchanting gear into a material per rarity, and forging a piece with a
chosen modifier. Everything below is a first proposal meant to be tuned. Numbers,
names and recipes are all expected to change.

Code: `scripts/Items/Crafting.cs` (rules), `scripts/UI/CraftingBoard.cs` (the
inventory's Crafting tab), `scripts/UI/MerchantPanel.cs`, `scripts/UI/ArtificerPanel.cs`,
`scripts/Hideout.cs` (the stations).
Tests: `EndgameSmokeTest` (`CRAFT-*`, `MERCHANT-*`, `DISENCHANT-YIELD`, `FORGE-*`,
`ARTIFICER-BENCH`, `INVENTORY-CRAFTING-TAB`, `CRAFT-SEAL`) and `ItemsSmokeTest` (`TOOLTIP-*`).

## What it is

Three loops that give loot somewhere to go:

1. **Orbs** drop from monsters and change an item in place: upgrade its rarity,
   reroll it, add or remove a modifier, reroll its numbers.
2. **The merchant** buys any item for gold and sells the cheap shards.
3. **Disenchanting** breaks an item into dust of its rarity; **the forge** spends
   dust to make a new item that is guaranteed to carry the modifier you chose.

Orbs gamble, the forge is deterministic. That pairing is the point: PoE's orbs are
exciting but can feel like a slot machine with no floor, while a "pick the stat you
want" forge alone kills the thrill of the drop. Having both lets a player chase
a perfect item with orbs on top of a forged guaranteed base.

## Orbs **[IMPLEMENTED]**

All names are original. Each maps loosely to a PoE orb so the genre's players read
them instantly.

| Orb | Does | PoE analogue | Merchant |
|---|---|---|---|
| Kindling Shard | common → magic with 1–2 modifiers | Transmutation | 20 g |
| Flux Shard | rerolls a magic item | Alteration | 30 g |
| Graft Shard | adds a modifier to a magic item with room | Augmentation | 35 g |
| Ash Orb | strips all modifiers → common | Scouring | 60 g |
| Crown Orb | magic → rare, adds one modifier | Regal | — |
| Tempest Orb | rerolls a rare (new name, 3–4 modifiers) | Chaos | — |
| Tuning Orb | rerolls every value within its tier, base stats too | Divine | — |
| Sundering Orb | removes one random modifier | Annulment | — |
| Ascendant Orb | adds a modifier to a rare with room | Exalted | — |

Which orb drops is a cascade from the rarest down (2026-10-14; it replaced fixed
drop weights): see items.md -> "Drop tables (cascade)" for the table.

Rules every orb follows:

- **Affix limits**: magic items carry at most 2 modifiers, rares at most 4 (still at
  most 2 Tempers and 2 Sigils, never two modifiers on the same stat — the existing
  `AffixRoller` rules). Added modifiers roll like drops: weighted by affix weight,
  tier weighted toward the best reachable at the item's level.
- An orb that can't do anything **is not consumed** and says why ("Tempest Orb
  needs a rare item", "This item has no room for another modifier").
- **Majestic and Set items can't be altered.** They are authored items.
- Rare names change on Crown and Tempest, stay on Ascend / Sunder / Tune; magic
  names always follow their modifiers.

**Orbs are counters, not grid items.** `items.md` -> "Currency" rejected PoE's
"currency takes inventory slots" model, and this keeps that: orbs live in the wallet
next to gold, so they never clutter the bag. What changed is
the earlier "this project is taking the Diablo model": the owner wants PoE-style
crafting, so orbs *are* now a crafting economy, just without the inventory Tetris.

### Where orbs come from **[IMPLEMENTED]**

In realities, each kill rolls an orb: 3.5% normal, 10% magic, 45% rare, 120%
unique-rare (scaled by the reality's quantity bonus), and a boss drops
`2 + tier/4`. Which orb it is rolls by cascade (items.md -> "Drop tables
(cascade)"), with the reality's rarity multiplier making rare orbs likelier. Orbs
are a pickup with a coloured orb and, for the rarer ones (under 10 % on their
cascade step), a light beam, and are collected by walking over them with a toast
naming them. The Survival Rift drops them too (run.md -> "Experience and pickups").

### Shared between characters **[DECIDED 2026-10-13]**

Orbs and dust belong to the **account** (`Profile.Currency`, saved in `profile.json`),
like the stash: farm on one character, craft for another. Gold stays per character.
Saves from before this carried orbs per character; loading pools them into the
account once.

### Using orbs: the inventory's Crafting tab **[IMPLEMENTED 2026-10-13]**

The inventory (I) has two tabs, **Inventory** and **Crafting**. Crafting swaps the
paper doll for the orb board (every orb with its count, the Artificer's Seal, the
dust and gold totals); the item grid stays below it. Click an orb to pick it up (its
name follows the cursor), then left-click an item: PoE's right-click-orb,
left-click-item flow, available anywhere, not just in the hideout. **Shift** keeps
the orb in hand for repeated use; right-click or Esc puts it away (Esc a second time
closes the panel). While an orb is in hand, items it can be used on light up green
and the rest dim, and the tooltip says what it will do or why it can't.

Crafting only touches items in the inventory: unequip to craft. That avoids having to
re-apply the equipment's stats mid-craft, and is the usual ARPG convention anyway.

The first pass had the orbs at the Artificer's bench; the owner asked for them under
I, so the bench now does disenchanting and forging only.

### Orb tooltips **[PLANNED 2026-10-18]**

Owner report (translated): *"Hovering an orb in the Crafting tab shows its tooltip
only after a delay, and it's annoying. It should appear at once, like item
tooltips."*

**Cause.** The orb buttons (`CraftingBoard._Ready`, `b.TooltipText = ...`) use
Godot's built-in tooltip. Godot shows it only after the mouse has rested on the
control for `gui/timers/tooltip_delay_sec` (0.5 s by default, unset in
`project.godot`), and the wait starts over whenever the mouse moves. It is also
unthemed: `UiTheme` never styles `TooltipPanel`/`TooltipLabel`, so it appears as
Godot's default grey box in the default font, unlike every other panel. Item
tooltips are ours: `ItemTooltipControl` is shown from the grid's hover polling in
`_Process`, on the first frame the mouse is over an item.

**Fix.**

1. A shared helper `UiTheme.InstantTooltip(Control target, Action<HoverTooltip>
   fill)`: one `HoverTooltip` (the follow-the-mouse panel the Mirror's jewel tree
   and the passive tree already use) per `CanvasLayer`, created on first use as a
   child of the target's canvas layer so it draws on the same layer (the inventory
   is layer 12, modals 20). `MouseEntered` → `fill` + `ShowTooltip()` on the same
   frame; `MouseExited`, `VisibilityChanged` (hidden) and `TreeExiting` →
   `HideTooltip()`. Disabled buttons must get it too (a disabled `BaseButton`
   keeps its mouse filter, so enter/exit still arrive; the test below checks it),
   which matters for orbs you have none of.
2. Orb buttons, through the helper (nothing else in the board changes):
   - title: the orb's name, 18 px, in its colour;
   - its effect (`orb.Effect`), 15 px;
   - "You have ×23" (13 px dim), or "You have none. Orbs drop in realities and the
     Survival Rift" (+ " · Odran sells it for 20 gold" for the four he sells);
   - "Click to pick it up, then click an item · Shift keeps it in hand ·
     right-click puts it away" (13 px dim).
   The Artificer's Seal button and the dust/gold icons get the same treatment.
3. Every other `TooltipText` in the UI moves to the helper too, so one tooltip looks
   and behaves the same everywhere (the consistency rule): the hideout menu bar's key
   chips and the town directory rows (`HideoutHud`), the minimap (`TownMap`), the
   stash's Pending tab (`StashPanel`), Arehucaz's Buy buttons (`ShardVendorPanel`),
   the level-up reroll buttons (`UpgradeCards.SetRerollState`). `HudEditor`'s empty
   `TooltipText = ""` stays.
4. Safety net for anything added later: theme the built-in tooltip in
   `UiTheme.Build` (`TooltipPanel` = the dark glass box, `TooltipLabel` = `Text` at
   15 px) and set `gui/timers/tooltip_delay_sec = 0.15` in `project.godot`.

Rejected: only lowering the global delay. It still waits for the mouse to stop and
still looks like a different program.

**Tests** (EndgameSmokeTest, next to `INVENTORY-CRAFTING-TAB`):

- `TOOLTIP-ORB-INSTANT`: open the inventory on the Crafting tab, emit `MouseEntered`
  on an orb button: the shared `HoverTooltip` is visible on that same frame and its
  text contains the orb's name and effect; `MouseExited` hides it; switching back to
  the Inventory tab hides it; an orb with a count of 0 shows "You have none".
- `TOOLTIP-NO-NATIVE`: walk the hideout scene tree with the inventory (both tabs),
  Odran, Arehucaz, the stash and the hideout HUD built: no `Control` has a non-empty
  `TooltipText`.

Capture: `RunCapture --only=crafting` gains `craft-orb-tooltip.png` (the mouse
warped over the Tempest Orb button, snapped two frames later).

### Seals **[IMPLEMENTED 2026-10-13]**

The owner's answer to "lock a modifier": the **Artificer's Seal** protects one
modifier of an item from the next orb, and every seal on the same item costs more.

- Crafting tab → Artificer's Seal → click an item → choose the modifier.
- Cost: **40 gold, doubling with every seal on that item**, plus **1 + (seals so far)
  dust of the item's rarity** (gilded for a rare). The count is stored on the item
  (`SealCount`, saved), so the price follows the item, not the player.
- The sealed modifier keeps its tier and value through the next orb: Tempest and
  Flux reroll everything else around it, Sundering can't pick it, Ash keeps it (and
  the item's rarity), Tuning leaves its value alone.
- **The seal breaks after that one orb.** A permanent lock would make Tempest spam
  free after one payment; a one-orb seal makes each protected attempt cost more,
  which is the sink the owner asked for.
- One seal at a time per item; sealing another modifier moves it (and still counts
  as a new, more expensive seal). The tooltip marks the sealed line "(sealed)".

## Merchant **[IMPLEMENTED]**

Odran the Trader has a stall in the hideout plaza (docs/design/hideout.md). Right-click an inventory item to sell it:

| Rarity | Price |
|---|---|
| Common | 1 + ilvl/8 |
| Magic | 4 + ilvl/3 + 2 × modifiers |
| Rare | 12 + ilvl/2 + 4 × modifiers |
| Majestic / Set | 60 + ilvl |

The tooltip shows the price while the merchant is open. Everything sold in this
visit sits on a **buyback** shelf and can be bought back for the same price until
the panel closes, so a misclick never costs a good item. He also sells the four
cheap shards (table above), which gives gold a steady sink besides stash tabs and
respecs.

## Dust and disenchanting **[IMPLEMENTED]**

Artificer's forge (in the smithy) → Disenchant tab → right-click an item. It becomes dust of its own rarity:

| Item | Dust | Amount |
|---|---|---|
| Common | Grey Dust | 1 + ilvl/20 |
| Magic | Azure Dust | 1 + ilvl/30 |
| Rare | Gilded Dust | 1 + ilvl/40 (+1 with 4 modifiers) |
| Majestic / Set | Prismatic Dust | 1 |

The tooltip shows the yield while the tab is open. Selling and disenchanting
compete on purpose: gold now or crafting power later.

## Forging **[IMPLEMENTED]**

Artificer's forge → Forge tab: choose a base, choose a modifier it can carry, choose a grade.
The new item carries that modifier, marked **crafted** (its own light-cyan colour
and a "(crafted)" note in the tooltip), plus some random modifiers:

| Grade | Result | Chosen modifier | Random extras | Cost |
|---|---|---|---|---|
| Grey | magic | best tier − 2 | 0 | 6 Grey + 20 g |
| Azure | magic | best tier − 1 | 1 | 4 Azure + 4 Grey + 60 g |
| Gilded | rare | best tier | 2–3 | 3 Gilded + 4 Azure + 150 g |
| Prismatic | rare | best tier, perfect roll | 3 | 1 Prismatic + 3 Gilded + 400 g |

"Best tier" is the highest the modifier can reach at the **forge's item level**: the
monster level of the highest reality tier the character has cleared, so forging keeps
pace with drops and clearing higher tiers is what unlocks better forging. The panel
previews the tier and value range per grade before you pay.

The crafted modifier is a normal modifier afterwards: Tempest or Sunder can remove
it, Tuning rerolls its value. (PoE protects crafted mods from some orbs; not worth
the rules yet.)

Prismatic dust only comes from Majestic and Set items, which don't drop yet, so the
top grade is waiting for them.

## Tooltip **[IMPLEMENTED]**

Reorganised at the owner's request, Path of Exile style:

- name (rarity colour, larger), base type (when the name isn't already the base);
- separator, item level, base stats;
- separator, modifiers: Tempers first, then Sigils, in a soft blue (crafted ones in
  light cyan);
- **no tiers by default**. Holding **Alt** shows, after each modifier, in small dim
  text and in parentheses: tier, pool, modifier name and the tier's roll range —
  `(T2 Temper "Far-flung" · 9–13%)`. A dim "Hold Alt for modifier tiers" line is
  the hint; the tooltip widens while Alt is held so the notes fit beside their lines;
- station footers (sell price, disenchant yield, what the orb in hand does);
- the comparison with your equipped item moved into **its own panel beside the
  tooltip** (as PoE shows the equipped item beside the hovered one), so a long diff
  no longer makes one tower of text.

The panel is now opaque with a border in the item's rarity colour (the old
semi-transparent default let panels behind it show through).

This reverses `items.md` -> "Tiers are player-visible" only in presentation: tiers
are still fully visible, one key away, and still in the drop descriptor for filters.

## Open questions

- Balance: drop rates, prices, dust yields and forge costs are first guesses. The
  autoplay bot doesn't craft, so this needs play.
- Should forging let you choose **two** modifiers at a higher grade?
- Orb icons: currently coloured circles and text buttons; proper pixel-art orbs
  belong in the art pipeline.
- NPCs reuse the Paladin and Sage ascendancy sprites; they deserve their own.
