# Skill balance testing (2026-11-04) **[LAYER A IMPLEMENTED]**

Owner's brief: test how the skills balance, how they interact where they can, and whether
their damage is excessive. A skill's level and tree should raise its damage, but **gear must
raise it more**: **no character may clear a tier-20 reality without gear and without trouble.**

The plan is below. Layer A (the damage matrix) is built and was run on 2026-11-05; its first
results and the cuts they led to are under "First run" at the end. Owner's decision
(2026-11-04): **lower the damage, leave monster life alone for now.**

## What already exists (and what's missing)

- `MonsterScaling` sets monster life from the damage a **reference character** deals at that
  monster level (`ExpectedDpsTable`: 130 DPS at level 1, 5,500 at level 85). It multiplies that
  by a time to kill per rarity: 0.4 s for a normal monster, 1.5 s magic, 5 s rare, 10 s
  unique, 45 s boss. Tier 20 is monster level 85, so a normal monster has about
  5,500 × 0.4 ≈ 2,200 life, plus 30% resistances and 15% armour reduction.
- `scripts/autoplay.sh --mode=reality --gear=none|typical --tier=N --class=…` plays real
  realities with an in-memory character. With `--gear=typical` it is the reference character
  (gems, ascendancy, best-of-two rares at the tier's item level). Each reality logs a `ttk`
  line by rarity plus clear/collapse/time.
- The playground has a DPS meter (`DpsTracker`, damage over time included) and an immortal
  training dummy.
- The editor's Analysis tab gives skill-only numbers (no character, no gear, no damage over
  time).

What's missing:

1. **The reference character spends no skill-tree points** (`ApplyReferenceCharacter` only
   raises gem levels). Trees are now a large share of damage, so the reference undercounts
   them. `ExpectedDpsTable` was also calibrated before the trees existed and before
   2026-11-04's Power change.
2. There is no measurement that **separates where damage comes from**: level/gems, tree,
   gear, passives/ascendancy, and auras/curses.
3. Nothing measures **interactions** (pairs of skills that multiply each other).
4. No regression guard: a future node or affix can break the curve silently.

## Targets (proposed, for the owner to confirm)

All numbers are single-target DPS against a tier's defences, relative to
`ExpectedDps(level)` (the reference curve monsters are built on). 1.0 means the 0.4 s normal
kill the game is tuned around.

| Profile at tier 20 (monster level 85) | Target DPS / Expected | Means |
|---|---|---|
| No gear, gem 20, full tree, passives + ascendancy | **≤ 0.25** | A normal monster takes ≥ 1.6 s, a rare ≥ 20 s, the boss ≥ 3 min; the run times out or collapses |
| White (normal) gear | ≤ 0.45 | Still struggling |
| Typical rares (today's reference) | 0.9 – 1.1 | The designed experience |
| Good rares (best of 5 per slot) | 1.5 – 2.5 | Gear progress is felt |

The same targets as **shares of the multiplier**, measured at tier 20:

- **Gear factor** (typical rares ÷ no gear) **≥ 4×**. This is "gear must raise it more".
- **Tree factor** (best tree path ÷ no tree, same gem) between **1.3× and 1.8×**.
- **Gem level factor** (gem 20 ÷ gem 1): today 1 + 12% × 19 = 3.28×. Kept, but checked
  against the gear factor (gear > gem level > tree).
- **No skill outlier:** each skill's best-path DPS with typical gear stays within **±25%** of
  the median of its group. Single-target and area skills form separate groups; area skills
  are judged on pack damage (below), not on one target.
- **Interactions:** a pair's DPS ÷ the better of the two alone ≤ **1.6×** (a setup such as
  Douse → Combust is meant to pay off, but not tenfold). Anything above is flagged for a look,
  not failed automatically.
- **Gearless reality at tier 20:** the bot with `--gear=none` and a full tree must **not**
  clear (target: 0 of 6 cleared, or every clear above 2× the typical time, normal-monster
  time to kill ≥ 1.6 s).

Rough expectation before measuring: Fireball at gem 20 deals 30 × 3.28 ≈ 98 per hit;
with a full tree (≈ ×1.6) and no gear that is about 260 DPS, about 5% of 5,500. If the
measurement shows a gearless character near 1.0, the excess comes from passives, the
ascendancy or an interaction, and the decomposition below shows which.

## The test, in four layers

### A. Damage matrix (headless tool, minutes) — `scenes/tools/BalanceMatrix.tscn`

A real `Player` with a real `CharacterData` (so passives, ascendancy, gear, auras and the real
`SkillUser.ResolveContext` + `BuildHitProfile` apply). It casts on a loop at an immortal dummy
that has **the tier's monster defences** (`MonsterScaling.Resistance/ArmourFor` + the theme
affinity), measured by `DpsTracker` (damage over time included).

Axes:

- **Skill:** every skill that deals damage (auras and curses are axes below, not rows).
- **Tree:** no tree; each branch maxed; and the **best path** of 19 points (beam search over
  the tree using the dumped `effects`, then confirmed in the simulation).
- **Gem level:** 1, 10, 20.
- **Character:** level from `ReferenceCharacterLevel(tier)`. Passive tree off/on and
  ascendancy off/on, so their share is visible.
- **Gear:** none, white, typical rares, good rares (same `ApplyReferenceCharacter` rolls,
  seeded so runs repeat).
- **Tier defences:** 1, 10, 20.
- **Targets:** one dummy (single target) and a pack of 8 dummies in a 120 px circle
  (pack damage). The pack number is what area skills, chains, forks and bursts are judged on.

Each run lasts 20 s of game time at the bot's ~3× speed, so the matrix needs pruning. The
full cross product is not run: the decomposition runs one axis at a time from a baseline.
That is ≈ 25 skills × ~6 trees × 4 gear × 3 tiers ≈ 1,800 runs × ~7 s ≈ 3.5 h. Split into
`--skills=` batches; first pass is tier 20 only (≈ 1 h).

Output: `user://balance/matrix.csv` (one row per run: skill, path, gem, gear, tier, DPS
single, DPS pack, mana/s, regen/s, hit, crit rate, DoT share). Plus `matrix.md` with the
targets table filled in, every miss in red, and a **decomposition per skill at tier 20**:
`level×gem × tree × passives × ascendancy × gear = total`.

### B. Interactions (same tool, `--mode=pairs`)

The interactions the game has today, each measured as "pair ÷ best single" on the dummy
(pack where it matters):

| Pair | Why it can explode |
|---|---|
| Douse → Combust; any fire skill with Kindle → Combust | oil makes every fire hit ignite, ignite stacks to 99, Combust detonates them all ×1.3 plus splash |
| Laceration / Bloodthirst / Grounded / Bare Steel → Exsanguinate | the same with bleed stacks |
| Curse of Elements × elemental skills; Curse of the World | resistance shred multiplies everything |
| Elemental Storm (focused) × conversions (Frostfire, Comet, Static Nova, Cold Current) | its focus doubles the bonus for one element |
| Precision × fast multi-hit skills (Double Strike, Flurry, Throwing Knives) | crit chance on many hits |
| Spell Totem × socketed skill (both trees apply) | two trees on one cast; 2 totems with Ancestral Ring |
| Echo / repeats × bursts, kill blasts, aftershocks | each repeat re-triggers every on-hit effect |
| Volley / Blade Count × Impact Burst / Firestorm / Arc Blast | more projectiles × bursts that overlap on one target |
| Immolation + ignite sources; Corruption spread × pack | damage over time across a pack |
| Auras stacking (Storm + Precision + Bloodthirst in the 6 slots) | all passives are always on |

Also a **worst-case search**: start from the best single build and greedily add the aura,
curse and second skill that raise DPS the most. Report the top 10 loadouts by
"DPS without gear ÷ Expected". That is the cheapest way to find the character the owner
fears.

### C. Real realities (autoplay bot, hours)

Layer A says how much damage; this says whether a character survives, finds monsters and
actually clears. Two changes to the bot:

1. `--tree=best|none|branch:<n>`: spend the gems' tree points with layer A's best path
   (today they stay unspent).
2. `--gear=white|good` next to `none|typical`.

Runs: per class (warrior, rogue, arcanist) × main skill (its best single-target skill and its
best area skill from layer A) × gear {none, typical} × tiers {10, 15, 20}, 6 realities each.
Pass: **`--gear=none` at tier 20 never clears comfortably** (see the targets), and
`--gear=typical` clears ≥ 80% at roughly the time it does today.

### D. Regression guard — `scenes/tests/BalanceGuardTest.tscn` (CI, < 2 min)

A small subset of A that runs with every suite:

- 6 representative skills (Fireball, Spark, Cleave, Double Strike, Exsanguinate after 20
  bleed stacks, Combust after Douse), gem 20, best tree, no gear, tier-20 defences, 6 s each.
  Each must stay **≤ 0.25 × ExpectedDps(85)**.
- The same 6 with typical gear: the **gear factor ≥ 4×**.
- Douse→Combust and bleed→Exsanguinate pairs: **≤ 1.6×** the best single.
- No full tree above **1.8×** no tree (read from the dumped `effects`, so it costs nothing).

Thresholds live in one place (`BalanceTargets.cs`), mirrored in the table above.

## Order of work

1. **Bot fix (C.1)** and **layer A at tier 20, no gear vs typical** for every skill: this
   answers the owner's question directly ("can a gearless character farm tier 20?").
2. **Decomposition + interactions (B)**: find what makes an outlier.
3. **Decisions with the owner**: lower the offending nodes or interactions, or raise gear's
   affixes (damage on weapons, % elemental on rings/amulets), then **recalibrate
   `ExpectedDpsTable`** from the new typical-gear numbers so monster life follows.
4. **Layer C** confirms in real realities.
5. **Layer D** locks it in; the editor's Analysis tab and its `effects` stay the quick
   tool for the owner's next changes.

## Open questions for the owner

- Are the targets right? The main ones: gearless ≤ 25% of the reference at tier 20; gear ≥ 4×;
  tree 1.3–1.8×; interactions ≤ 1.6×.
- If damage is cut to keep gearless characters weak, should monster life fall too (keeping
  today's kill speed with typical gear), or should realities become slower?
- Should the gem-level factor (×3.3 at gem 20) shrink so gear's share grows, or should gear
  get stronger damage affixes instead?
- Supports are parked (`Features.SupportGems`): keep them out of the reference until they
  return?

## First run (2026-11-05)

**Tool:** `./scripts/autoplay.sh --mode=matrix --class=arcanist|warrior|rogue --tier=20
[--gear=none,ascendancy,typical] [--trees=none,best] [--skills=…] [--seconds=8] [--gem=20]`
(`scripts/tools/AutoplayBot.Matrix.cs`). It builds an in-memory character at the tier's
reference level (level 70 at tier 20, passives spent, every gem at 20) and enters the
playground at that tier. For each skill, tree and gear profile it casts at tier monsters
that stand still and never attack: one at a time, respawned on death (single-target DPS and
the time a normal monster takes), then a pack of 8 (pack DPS, clear time). It does this
against a Skeleton (armour affinity) and a Slime (fire affinity). Damage is what the monsters
took, damage over time included. `best` is a beam search over the tree, scored on the real
resolved cast. Gear profiles:
- `none`: passives, no ascendancy, no gear.
- `ascendancy`: the same plus the ascendancy.
- `typical`: the bot's reference rares.

Rows go to `user://balance/matrix_<class>_t<tier>.csv`; the 2026-11-05 run is committed in
`docs/design/balance/`. About 15 min per class.

Two bugs found while building it:
- `CharacterBuild.Rebuild` leaves equipment alone, so the gear has to be put on the player
  (the first run measured "typical" gear naked).
- **An even projectile fan left the aim empty.** With Volley, Fireball's two projectiles flew
  at ±6° and both missed a monster at 240 px (0 single-target DPS). Now one projectile always
  flies at the aim (`GenerateProjectileSpawnRequests`).

**What it showed at tier 20.** Ratios are single-target DPS ÷ `ExpectedDps(85)` = 5,500
(1.0 = a normal monster in 0.4 s).

- **Without gear no character comes close.** The best gearless build is Fireball with a full
  tree at 0.09–0.17× (a normal monster in 2–4 s, a rare in 30+ s). The owner's worry
  ("a gearless character clearing tier 20") does not happen.
- **Trees multiplied too much on melee and cooldown skills.** Typical gear, best path ÷ no
  tree: Sunder 6.5×, Cleave 6.5×, Double Strike 5×, Glacial Hammer 4×, Meteor 3.9×, against
  a target of ≤ 1.8×. The stack was Power + a second "more damage" node (10/20/30) +
  Overwhelm (35%) + Flurry or Echo, on top of attack speed or cooldown.
- **Gear matters a lot for attacks** (weapon damage, crit: ×5–50) **and little for spells**
  (Fireball ×1.8–3.9). The outlier was the Samurai rogue with typical gear: Glacial Hammer at
  1.4× expected, packs gone in one swing.
- **With typical gear almost every build is far under the curve** (0.1–0.7×). Monster life
  was calibrated with damage supports in every socket, and supports are parked
  (`Features.SupportGems`). So tier 20 is slow for nearly everyone, not too easy. The owner
  chose to keep monster life for now; recalibrating `ExpectedDpsTable` is the open item.

**Cuts made (`SkillTrees.cs`):**
- **Second damage nodes** (Rend, Fault Line, Split Earth, Heavy Head, Deep Cuts, Charged, Wide
  Fan, Brittle, Attunement): 6 / 10 / 15% (was 10 / 20 / 30).
- **Overwhelm:** 25% more damage, 20% more mana everywhere (was 35 / 25; the owner had already
  set this for Frost Shards and Corruption). Combust and Exsanguinate keep 20%.
- **Flurry and Echo:** 35% less damage per strike or repeat (was 15% and 20%).
- **Firestorm:** the burst deals 75% of the hit (was 100%).
- **Gale:** half its damage bonus.

**After the cuts** (same run; gear is re-rolled on every run, so compare the tree factor, not
the absolute numbers):

| Skill (typical gear) | Tree × before | Tree × after |
|---|---|---|
| Sunder | 4.5–6.5 | 3.4–3.8 |
| Cleave | 4.1–16 | 2.5–3.6 |
| Double Strike | 4.0–5.0 | 2.5–3.2 |
| Glacial Hammer | 4.0–4.1 | 2.4–2.6 |
| Meteor | 3.5–4.8 | 2.8–3.1 |
| Fireball | 2.0–2.8 | 1.9–2.1 |
| Frost Shards | 1.9–2.2 | 1.9–2.0 |

Gearless best builds now top out at 0.09× expected. Still above the 1.8× tree target: the
cooldown skills (Sunder, Meteor), where Patience counts as damage. That is a decision for the
owner: accept up to ~3× there, or trim Patience/Echo further.

**Known gaps in layer A:**
- Ethereal Blades reads 0 on one target: the orbit misses a monster standing inside it. Its
  pack numbers are fine.
- Spark's best path measures below no tree (its spread, conversion to physical against
  armour). The search is heuristic.
- Gear is re-rolled on every run (unseeded), so absolute numbers vary ±50%.
- Auras, curses, totems and detonations are not measured yet (layer B).
