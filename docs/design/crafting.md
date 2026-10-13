# Crafting, merchant and disenchanting

**Status: [IMPLEMENTED 2026-10-13] first pass.** The owner asked for "crafting like
Path of Exile, with different currencies that change the item in different ways —
invent something and we'll improve it tomorrow", plus selling gear for gold at a
merchant, disenchanting gear into a material per rarity, and forging a piece with a
chosen modifier. Everything below is a first proposal meant to be tuned. Numbers,
names and recipes are all expected to change.

Code: `scripts/Items/Crafting.cs` (rules), `scripts/UI/MerchantPanel.cs`,
`scripts/UI/ArtificerPanel.cs`, `scripts/Hideout.cs` (the two stations).
Tests: `EndgameSmokeTest` (`CRAFT-*`, `MERCHANT-*`, `DISENCHANT-YIELD`, `FORGE-*`,
`ARTIFICER-BENCH`, `HIDEOUT-STATIONS`) and `ItemsSmokeTest` (`TOOLTIP-*`).

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

| Orb | Does | PoE analogue | Drop weight | Merchant |
|---|---|---|---|---|
| Kindling Shard | common → magic with 1–2 modifiers | Transmutation | 30 | 20 g |
| Flux Shard | rerolls a magic item | Alteration | 26 | 30 g |
| Graft Shard | adds a modifier to a magic item with room | Augmentation | 18 | 35 g |
| Ash Orb | strips all modifiers → common | Scouring | 10 | 60 g |
| Crown Orb | magic → rare, adds one modifier | Regal | 6 | — |
| Tempest Orb | rerolls a rare (new name, 3–4 modifiers) | Chaos | 5 | — |
| Tuning Orb | rerolls every value within its tier, base stats too | Divine | 4 | — |
| Sundering Orb | removes one random modifier | Annulment | 3 | — |
| Ascendant Orb | adds a modifier to a rare with room | Exalted | 1 | — |

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
next to gold (saved per character), so they never clutter the bag. What changed is
the earlier "this project is taking the Diablo model": the owner wants PoE-style
crafting, so orbs *are* now a crafting economy, just without the inventory Tetris.

### Where orbs come from **[IMPLEMENTED]**

In realities, each kill rolls an orb: 3.5% normal, 10% magic, 45% rare, 120%
unique-rare (scaled by the reality's quantity bonus), and a boss drops
`2 + tier/4`. Orbs are a pickup with a coloured orb and, for the rarer ones, a light
beam, and are collected by walking over them with a toast naming them. No Survival
Rift drops yet.

### Using orbs: the Artificer's bench **[IMPLEMENTED]**

Hideout → Artificer's Bench (E) → Orbs tab. Click an orb to pick it up (its name
follows the cursor), then left-click an item: PoE's right-click-orb,
left-click-item flow. **Shift** keeps the orb in hand for repeated use; right-click
or Esc puts it away (Esc a second time closes the bench). While an orb is in hand,
items it can be used on light up green and the rest dim, and the tooltip says what
it will do or why it can't.

Crafting only touches items in the inventory: unequip to craft. That avoids having to
re-apply the equipment's stats mid-craft, and is the usual ARPG convention anyway.

Rejected for now: using orbs from the regular inventory (I) anywhere, as in PoE.
Possible later, but it puts crafting in the middle of combat and needs a currency
strip on the inventory panel; a hideout bench was the smaller, testable first step.

## Merchant **[IMPLEMENTED]**

Odran the Trader stands in the hideout. Right-click an inventory item to sell it:

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

Bench → Disenchant tab → right-click an item. It becomes dust of its own rarity:

| Item | Dust | Amount |
|---|---|---|
| Common | Grey Dust | 1 + ilvl/20 |
| Magic | Azure Dust | 1 + ilvl/30 |
| Rare | Gilded Dust | 1 + ilvl/40 (+1 with 4 modifiers) |
| Majestic / Set | Prismatic Dust | 1 |

The tooltip shows the yield while the tab is open. Selling and disenchanting
compete on purpose: gold now or crafting power later.

## Forging **[IMPLEMENTED]**

Bench → Forge tab: choose a base, choose a modifier it can carry, choose a grade.
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
- Orbs from the regular inventory, anywhere (PoE-style), or keep crafting at the bench?
- Should forging let you choose **two** modifiers at a higher grade, or lock a
  modifier so Tempest keeps it (PoE's "prefixes cannot be changed")?
- Orb icons: currently coloured circles and text buttons; proper pixel-art orbs
  belong in the art pipeline.
- Should orbs and dust be account-wide (in the stash) instead of per character?
- NPCs reuse the Paladin and Sage ascendancy sprites; they deserve their own.
