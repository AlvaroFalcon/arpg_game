# Survival Rift — implementation plan by milestone

Status: **plan, nothing built** (2026-11-07). It turns the proposals in
[survival-research.md](survival-research.md) into milestones. Each milestone is meant to
be one branch and one PR, buildable and playable on its own. Owner request (translated):
*"make an implementation plan for each proposal, split by milestone and explained
enough to have the right context."*

Every number in this plan is a **starting value to tune**, not a decision. Anything
marked **Owner** needs the owner's answer before or during the milestone. Read the
research doc first for the *why*; this doc is the *how*.

## Order and dependencies

| Milestone | Proposal(s) | Depends on | Size |
|---|---|---|---|
| **M0 — Foundations** | shared plumbing | — | S |
| **M1 — Pick control** | 5 (Skip, Banish, gold rerolls) + the Luck card from 8 | M0 | S |
| **M2 — Rift chests** | 1 + 8 (Luck on chests) | M0 | M |
| **M3 — Shrines** | 3 | M0, M2 (Challenge drops a chest) | M |
| **M4 — Rift tiers and Fear modifiers** | 4 | M0 | M |
| **M5 — Skill evolutions** | 2 | M2 (delivered by chests) | L |
| **M6 — Climax and endless milestones** | 7 | M2 (milestone chests) | S |
| **M7 — Rift challenges** | 6 | M4, M5, M6 (challenges refer to them) | S–M |
| **M8 — A second arena** | 9 | M4 (tiers per arena) | L |

M1 and M2 can run in parallel after M0, as can M3 and M4. Measure with the autoplay bot
after each milestone (see "Measuring" below) before starting the next one that changes
power or difficulty.

## How the Rift works today (the context each milestone builds on)

- **Scene and flow:** `scenes/run/Run.tscn` with `RunController` (setup, co-op party,
  results) → `RunState` (the clock, kills, XP, pickups, drops: the `IPickupHandler`) →
  `RunDirector` (spawning, swarms, elites, bosses, endless phase, cash-out). Every
  difficulty curve is a **pure static function of time** in `RunDirector`: `Danger(t)`,
  `SpawnRate(t)`, `EnemyCap(t)`, `Scaling(t)` (life and damage), `EliteIntervalAt(t)`,
  `EliteChance(t)`, `SwarmSize(t)`. That is the single place a difficulty multiplier has
  to reach.
- **Build:** `RunBuild` (child of the player) holds the run's skills (max 4, level cap 6),
  supports, passives (power-ups, rarity Common/Magic/Rare) and keystones. It rolls offers
  (`RollOffers`, `TryReroll`) through **`OfferRules`**, the only home of offer numbers. It
  applies a choice through `UpgradeOffer.Apply`, or from a **key string** with
  `ApplyKey("s:spark" / "l:spark" / "g:support:skill" / "p:passive:rarity" / "k:…")`. Keys
  are how co-op replays a client's pick on the host's copy of that player. Every modifier
  carries `RunBuild.Source`, so the run build is stripped in one call before saving.
- **Level-up UI:** `LevelUpScreen` (cards, the 2 per-run rerolls, party rounds).
  **Co-op rounds:** `RiftHost.Rounds` (host pauses everyone, collects one key per player,
  times out to a forced key, validates with `WellFormedKey`).
- **Pickups:** `Pickup` (`PickupKind` enum: Experience, Life, Gold, Shard, Gem,
  Currency, Nuke, Magnet, Jewel), spawned through the `LootQueue`
  (`RunState.Loot.Add(cost, …)`), collected in `RunState.OnPickup`. Drops are
  **instanced per player** in co-op (`RollDropsFor(rp, …)`, `PartyRules.Share`).
  Power-ups are rolled in `OnEnemyKilled`.
- **Rewards:** `RunRewards` (gold, orbs, item cards by time, reward tiers I–X) →
  `ResultsScreen`.
- **HUD:** `RunHud` (danger gauge, skill bar with auto-cast sweeps, `RiftMinimap`,
  loot feed `UI.LootFeed`, toasts and banners through `RunState.ShowToast` and
  `RunDirector.Announced`).
- **Co-op wire:** `RiftProtocol` message ids. Shared ids are 1–16, 18–22 and 26–29; the
  Rift-only ones are 17, 23–25, 30 and 31. Realities use 64–79. **New Rift ids start at
  32**, and every RPC stays on channel 0 (CLAUDE.md).
- **Stats:** `StatId` (explicit numbers, last one used so far `AutoCastSpeed = 39`;
  append new ids at the end, never renumber) and `ActorStats`.
- **Meta:** `Profile` (account: currency, stash, quests, `BestSurvivalTime`) and
  `CharacterData`. Quests are `QuestDef` in `QuestCatalog`, fed by `GameEvents.Raise`.
- **Tests that guard the Rift:** `RunSmokeTest` (timeline, rewards),
  `RiftProgressionTest` (offers, cadence, rerolls, cash-out), `CoopSmokeTest` (party),
  `RiftAutoCastTest`. **A suite must finish within `--quit-after 6000` frames**
  (`run_all_tests.sh`): anything that plays seconds of game time goes in its own suite.
- **Tools:** `scripts/autoplay.sh` (survival bot, `--class`, `--runs`, `--report`),
  `RunCapture --only=rift` / `--only=update` for screenshots, and the playground's DPS
  meter (F3).

## Rules for every milestone

- **Feature flag.** Add `Features.Rift<Name>` (`scripts/Core/Features.cs`, like
  `SupportGems`), on by default once accepted. It lets a half-tuned system be merged and
  switched off.
- **Co-op from the first commit.** The host decides everything random. Clients get
  results as events. Anything shared (difficulty, shrine state) is party-wide; anything
  that is loot (chests) is per player, like today's drops. Extend `CoopSmokeTest` or
  `RiftProgressionTest` with the party case.
- **Saves.** New profile fields get defaults and a `Profile.Validate` clamp. An old save
  must load unchanged.
- **Docs.** Update `run.md` / `progression.md` with an "[IMPLEMENTED date]" section, add a
  journal entry, and capture screenshots with `RunCapture` for the PR (`create-pr`
  skill).
- **No numbers in two places.** Offer numbers live in `OfferRules`, difficulty in the new
  `RiftDifficulty`, reward numbers in `RunRewards`, chest numbers in the new
  `ChestRules`.

## M0 — Foundations

**Goal:** the shared pieces several milestones need, with no visible change to the game.

1. **`RiftDifficulty`** (new, `scripts/Run/RiftDifficulty.cs`): a plain object on
   `RunState` with `Tier` (1), `Greed` (0 stacks), `Modifiers` (empty), plus derived
   multipliers: `Life`, `Damage`, `Spawn`, `Cap`, `Speed`, `Experience`, `Gold`,
   `Rarity` and `ItemLevelBonus`.
   - `RunDirector`'s static curves keep their signatures and get an overload that takes
     a `RiftDifficulty` (`SpawnRate(t, d) = SpawnRate(t) × d.Spawn`; `EnemyCap` is
     multiplied, then clamped to `EnemyCapMax` 400, the frame budget).
   - `Scaling(t, d)` multiplies life and damage. `Danger(t)` stays the displayed level.
   - All of it multiplies 1.0 in M0. A unit check proves the default changes nothing.
2. **`StatId.Luck` (40)**, base value 1.0, like `AutoCastSpeed`. Nothing reads it yet.
3. **Rift `GameEvents` ids**: `RiftChestOpened`, `RiftShrineUsed`, `RiftEvolved`,
   `RiftEndlessMinute`, `RiftCleared` (value = tier), `RiftLevelReached`. Raised from
   `RunState` / `RunDirector`, for M7's challenges.
4. **Reserved `RiftProtocol` ids**: 32 `ChestOpened`, 33 `ShrineSpawn`, 34 `ShrineState`,
   35 `Difficulty`, documented in the id comment block.
5. **Baseline numbers**: run the autoplay bot (`--class=warrior|rogue|arcanist
   --runs=6`, and `--skill=spark --runs=6`), and record level at 7:30, clear rate, death
   time and endless minutes reached in `docs/design/survival-roadmap.md` -> "Measuring".
   Every later milestone compares against these.

**Tests:** `RunSmokeTest` → `DIFFICULTY-DEFAULT-IS-IDENTITY` (curves with and without a
default `RiftDifficulty` are equal at 0, 3, 7 and 15 min).
**Acceptance:** the whole suite is green and the baseline table is filled. No player-facing
change.

## M1 — Pick control (Skip, Banish, gold rerolls, the 4th card)

**Goal:** less "the RNG ruined my run", and a use for run gold before the results
screen.

**Behaviour (starting values):**
- **Skip** (`OfferRules.SkipsPerRun = 3`): closes the row without a pick and gives back
  **30 %** of that level's XP (`RunState.ExperienceToNext(level) × 0.3`). It cannot skip
  a keystone row (owner rule: keystones are offered alone and on purpose).
- **Banish** (`OfferRules.BanishesPerRun = 2`): removes that card's **identity**
  (`UpgradeOffer.Identity`, e.g. `p:damage`, `s:spark`) from the pool for the rest of the
  run, and the slot rerolls for free. A keystone can be banished.
- **Gold rerolls:** once the 2 free per-run rerolls are spent, a reroll costs gold
  (25 × 1.6ⁿ, where n = gold rerolls this run: 25, 40, 64, 102…), taken from the wallet
  the run's gold goes to. The price shows on the button. **Owner:** wallet gold or
  only gold picked up in this run?
- **The 4th card (Luck):** chance of a 4th card on an ordinary row = `clamp(Luck − 1,
  0, 1)`. A "Fortune" power-up (+10 % Luck per pick, max rank 5, weight 0.8) joins the
  pool in `UpgradeCatalog.Passives`. VS does the same: 3 cards, 4 with Luck.

**Code:**
- `RunBuild`: `SkipsLeft`, `BanishesLeft`, `GoldRerolls`, `_banished` (a
  `HashSet<string>`); `Roll(…)` excludes banished identities; `TryBanish(index, …)` and
  `RerollPrice`. Count is `OfferRules.CardsPerOffer + (rng < FourthCardChance ? 1 : 0)`.
- `LevelUpScreen`:
  - a **Skip** button, with a **Banish** button under each card next to the reroll;
  - keys: **S** skips, **Shift + card number** banishes;
  - the reroll button shows the gold price once the free ones are gone;
  - a 4-card layout. 1280×720 fits four cards at the current card width minus 10 % (check
    with `RunCapture --only=rift`).
- **Co-op:**
  - new key forms `x:` (skip) and `b:<identity>` (banish, followed by the reroll the
    host rolls);
  - `RiftHost.WellFormedKey` accepts them;
  - `ForcedKey` never skips or banishes;
  - the gold for a reroll is checked on the host against that player's wallet copy.

**Tests:** `RiftProgressionTest` → `SKIP-REFUNDS-XP`, `SKIP-NOT-ON-KEYSTONE-ROW`,
`BANISH-NEVER-RETURNS` (400 rolls after banishing `p:damage`), `REROLL-GOLD-PRICE`
(sequence and refusal without gold), `FOURTH-CARD-WITH-LUCK` (0 % at Luck 1, ~50 % at
1.5 over 400 rows), plus a co-op round with `x:` and `b:` keys.
**Acceptance:** a player can finish a run never taking a card they dislike more than
twice. The UI fits at 1280×720. A co-op party with mixed skip/banish picks stays in sync.

## M2 — Rift chests

**Goal:** a second power track inside the run: rare, visible, and exciting to open.

**Behaviour (starting values, in a new `ChestRules`):**

| Source | Chance | Size |
|---|---|---|
| Announced elite | 35 % | rolled |
| Ordinary elite (from 2:00) | 8 % | rolled |
| Bone Herald | always | 3 |
| Ember Colossus | always | 5 |
| Endless phase: every boss | always | 5 |

- **Rolled size:** check 5 first (`3 % × Luck`), then 3 (`12 % × Luck`), else 1. This is
  VS's cascade.
- **Opening:** walk over it, and it opens with a burst. **No pause** (co-op and the
  horde both argue against pausing). Each slot is a "silent level-up" drawn from what you
  already own:
  - a level for an owned skill below the cap (weight 3);
  - a rank for an owned power-up, rarity rolled with Luck (weight 2);
  - from M5, an evolution (always first when one is ready).
  - It never gives new skills, supports or keystones: those stay deliberate choices on
    the level-up screen.
  - With nothing to raise, a slot pays gold (60 × minutes, at least 50), like VS's coin
    bags.
- **Feedback:** one loot-feed line per slot ("Chest · Spark → Lv 5", "Chest · Might
  (Magic)") and a short banner for a 5-chest. The minimap shows chests on the floor.
- **On the floor:** a chest never despawns and is pulled by a Lodestone. At most 6 on
  the floor (beyond that the oldest opens itself for its owner).

**Code:**
- `PickupKind.Chest` (appended at the end: the enum is sent as a byte).
- `Pickup.Value` = size, with a drawn chest sprite (EasyPixel `tools/art`, three sizes or
  three tints).
- Drop rolls in `RunState.RollDropsFor` and `OnEnemyKilled`.
- `RunBuild.ChestRewards(size, rng) → List<string> keys`: pure, so it is unit-testable.
  The keys are applied with `ApplyKey`, so the host can replay them. A new key form
  `c:` is not needed: chest rewards reuse `l:` and `p:`.
- **Co-op:** chests are per-player loot like gear: the host rolls the keys on collect,
  applies them on its copy, and sends `ChestOpened` (32) with the keys to that client,
  which applies them and shows the feed.
- `GameEvents.RiftChestOpened(size)`.
- Luck now also nudges power-up rarity: `RollRarity(rng, luck)` (Magic and Rare chances
  × Luck, capped).

**Tests:** new **`RiftChestTest`** suite →
- `CHEST-SIZE-ODDS` (10,000 pure rolls at Luck 1 and 2);
- `CHEST-ONLY-OWNED` (never a new skill, support or keystone);
- `CHEST-FALLBACK-GOLD` (maxed build);
- `CHEST-NO-PAUSE` (the tree is not paused while opening);
- `CHEST-BOSS-SIZES`;
- `CHEST-COOP-KEYS` (host keys replayed on a second build give an identical build).

**Measuring:** autoplay level at 7:30 should stay about the same (chests add power, not
levels). Clear speed and the share of deaths before the Colossus will move: tune the
elite chance so a 7:30 win sees 4–6 chests.
**Acceptance:** a typical win opens about 5 chests including one 5-chest, and the
results screen lists "Chests opened".

## M3 — Shrines in the arena

**Goal:** give an auto-cast player places to go and a risk to choose. This answers the
genre's "I just stand still" complaint.

**Behaviour (starting values):**
- At the start, 3 shrines at random spots at least 500 px from the centre and from each
  other. A new one appears every 2:30 while fewer than 3 stand (never during a boss
  fight). Each is used once and then fades.
- **Greed:** touch to accept, and get +1 Greed stack for the rest of the run, party-wide.
  Each stack gives **+6 % monster life, damage and spawns**, and **+10 % XP and +10 %
  gold and orb drops**. The HUD's danger gauge shows "Greed ×N".
- **Challenge:** touch to summon an announced elite plus 2 champions at the shrine. When
  all three die they drop **one chest of size ≥ 3** (for every player in co-op).
- **Charge:** stand inside for 3 s; leaving resets it, and a ring fills on screen. The
  reward is a random **Rare power-up** among the owned ones (no pause), or a new Magic
  one if none is owned.
- **Magnet:** touch for the Lodestone effect (every gem flies in). The VS/Megabonk trick
  of "clear a loop, then pull" works here.
- Shrines are drawn on the `RiftMinimap` with their own icons, and are visible on screen
  with a label.

**Code:**
- `scripts/Run/Shrine.cs` (a Node2D with a distance check, like `Pickup`; not an
  Area2D).
- Placement in `RunDirector` (a seeded RNG, so co-op is identical).
- Greed writes `RunState.Difficulty.Greed` (M0's multipliers).
- Challenge uses `RunDirector.SpawnAt(…, elite: true)` and tracks its three enemies.
- Charge applies a `p:<id>:2` key.
- **Co-op:** the host owns shrines. `ShrineSpawn` (33) and `ShrineState` (34) carry
  progress, use and fade. Greed goes to everyone in `Difficulty` (35). The first player
  to touch a shrine uses it, and Charge needs only one player inside.
- Autoplay bot: a "go to the nearest shrine when nothing is within 250 px" behaviour,
  behind a flag, so balance runs can include or exclude shrines.

**Tests:** new **`RiftShrineTest`** suite →
- `SHRINE-PLACEMENT` (distance rules over 50 seeds);
- `GREED-STACKS-SCALE` (multipliers);
- `CHALLENGE-DROPS-CHEST`;
- `CHARGE-NEEDS-3S` (leaving resets it);
- `MAGNET-PULLS-ALL`;
- `SHRINE-COOP-ONCE` (two players touching on the same frame use it once).

**Measuring:** bot runs with and without shrines; Greed ×3 runs should die later than
7:30 less than half the time.
**Acceptance:** a player who ignores shrines plays exactly today's Rift. One who uses them
levels faster and takes more risk, and the HUD always says why the run got harder.
**Owner:** should Greed have a cap (Megabonk has none)?

## M4 — Rift tiers and Fear modifiers

**Goal:** a reason to come back after beating the Colossus. This was an open question in
run.md, and "too easy once unlocked" is the most common VS complaint.

**Behaviour (starting values):**
- **Tiers 1–10.** Beating the Colossus on tier *t* unlocks *t+1*. This is account-wide:
  `Profile.RiftTierCleared`, highest cleared, default 0.
- Tier *t* multiplies:
  - monster life by `1 + 0.5(t−1)`, damage by `1 + 0.2(t−1)`, spawns by
    `1 + 0.06(t−1)`;
  - rewards: item level `+6(t−1)`, rarity multiplier `+0.2(t−1)`, gold and orbs
    `+15 %(t−1)`.
  - From tier 4 monsters gain one monster affix (the reality affix list), and from tier
    7 two.
  - **Owner:** does tier 10 match the Mirror's tier 10 for item level, so both endgames
    pay alike?
- **Fear cards** (chosen before the run on the Rift panel, up to 3, each **+20 %** to the
  reward multipliers):
  - Swift (+20 % monster speed);
  - Thick (+40 % monster life);
  - Hordes (+25 % spawns and cap);
  - Volatile (elites explode on death);
  - Famine (no life orbs);
  - Short fuse (swarms twice as often).
  - The genre's Hope side (a buff) is left out on purpose: the persistent character
    already gives power.
- The tier and Fear cards show on the HUD and on the results screen. A clear on a tier
  with 3 Fear cards is recorded too, for M7.

**Code:**
- `RiftDifficulty` gains `Tier` and `Modifiers`; `RunRewards.For(…)` takes it.
  `RunLaunchOptions` gains `Tier` and `Fears`.
- The **Rift panel** in the hideout (where the portal asks to enter) gets a tier
  selector (1…cleared+1) and the Fear cards.
- **Co-op:** the proposer's choice travels in `RiftProposal` and `RunBegin`. The tier is
  capped by the **host's** cleared tier. **Owner:** or by the lowest in the party?
- `GameEvents.RiftCleared(tier)`.
- The bot learns `--rift-tier=N --fear=swift,thick`.

**Tests:** `RunSmokeTest` → `TIER-SCALES-CURVES`, `TIER-REWARDS`, `FEAR-STACKS-REWARD`;
`RiftProgressionTest` → `TIER-UNLOCK-ON-COLOSSUS` (profile field written once, never
lowered), `TIER-SAVE-COMPAT` (old profile = 0); a co-op `TIER-FROM-HOST`.

**Measuring:** the bot's reference characters (`--class`, typical gear from
`MonsterScaling`) should clear tier 1 at 90 %+, tier 3 at about 50 %, and tier 5 only
with good gear. Write the measured table into this doc.
**Acceptance:** after one Colossus kill the panel offers tier 2, and higher tiers visibly
pay more on the results screen.

## M5 — Skill evolutions

**Goal:** the "my build came together" moment: a skill at its cap plus the right partner
becomes something new.

**Behaviour (starting values):**
- An **evolution** needs the skill at **level 6** (the run cap) plus a **named partner**:
  a power-up or support the run build owns. When both are true, the skill bar slot shows
  a pulsing "Evolution ready" mark. The **next chest of size ≥ 3**, or any boss chest,
  turns its first slot into the evolution.
- **First six**, chosen from the most-played Rift skills. **Owner:** the names and effects
  are a design pass with the owner.

  | Base skill | Partner | Evolved |
  |---|---|---|
  | Fireball | Volley | Firestorm: 3 fireballs, larger blast |
  | Spark | a speed power-up | Storm Lattice: sparks chain between each other |
  | Frost Nova | Duration | Permafrost: leaves a freezing field |
  | Cleave | Ferocity | Reaper's Arc: a full circle, pulls in |
  | Throwing Knives | Volley | Fan of Blades: 360° volley |
  | Blades (orbit) | Area | Whirling Wall: a second ring |

- An evolved skill keeps its level, supports and keystone interactions. It gets
  **×1.5 damage** on top of its effect change, a new name and icon tint, and a line in the
  build summary.

**Code:**
- `scripts/Progression/EvolutionCatalog.cs`: `EvolutionDef { SkillId, PartnerIdentity,
  Name, Description, Func<IEnumerable<ISkillUpgrade>> Upgrades, Tint }`.
  - The effect is built from **`ISkillUpgrade` / `CastModifier`** attached to the
    `SkillInstance` (the `Attached` list supports already use), not from new skill
    definitions. Evolutions then compose with everything that already reads a
    `CastContext`.
  - The few effects with no existing modifier (Spark chaining between sparks, Frost Nova
    leaving a field) need one new projectile or area hook each: estimate per skill.
- `SkillInstance` gains `EvolvedName` (display override) and `IsEvolved`.
- `RunBuild.ReadyEvolutions()`, `Evolve(skill, def)`, and the key `e:<skill>`, so co-op
  replays it.
- `M2`'s `ChestRewards` puts a ready evolution first.
- **UI:**
  - the level-up cards of the partner show "Evolves Fireball at Lv 6";
  - the HUD slot shows a mark;
  - a banner and sound on evolving.
- `GameEvents.RiftEvolved(skill)`.

**Tests:** new **`RiftEvolutionTest`** suite →
- `EVOLVE-NEEDS-BOTH`;
- `EVOLVE-FROM-CHEST` (and not from a 1-chest);
- `EVOLVE-KEY-REPLAYS`;
- one `EVOLVED-<SKILL>` per evolution, which checks the effect on a dummy (projectile
  count, field present) and the damage against the base skill (≥ ×1.5);
- `EVOLVED-SURVIVES-SUPPORT` (supports still apply).

**Measuring:** the playground DPS meter, base against evolved, for each of the six.
**Acceptance:** in a normal 7:30 win with a focused build the evolution arrives between
5:00 and the Colossus, and it is visibly different, not only bigger numbers.

## M6 — Climax and endless milestones

**Goal:** an ending that feels like an ending, and reasons to stay in the endless phase
besides loot.

**Behaviour (starting values):**
- **The Surge (6:30–7:00):**
  - a banner "The Rift surges";
  - the spawn rate ×2, with two swarm rings back to back, a mixed roster and no elites;
  - it ends when the Colossus rises at 7:00, as today, and normal enemies burn away.
  - This is the "massive final wave" Megabonk players asked for, without losing our
    boss.
- **Endless milestones** at 5, 10, 20 and 30 minutes of endless time (counted from the
  Colossus kill):
  - each gives a banner, a guaranteed 5-chest per player, and
    `GameEvents.RiftEndlessMinute(n)` (M7 counts it);
  - the HUD's reward panel lists the next milestone next to the next reward tier.
- **Owner:** should a milestone also ease the curve for a few seconds (a breather), or
  only reward?

**Code:**
- `RunDirector.Step`: a `Surge` window (constants in the director's header table) and
  milestone checks after `BeginEndless`.
- Co-op: the existing `Banner` event.
- The milestone chests use M2's per-player chest drop.

**Tests:** `RunSmokeTest` with `State.SkipTo(...)`:
- `SURGE-WINDOW` (spawn rate doubled in the window only);
- `SURGE-ENDS-ON-COLOSSUS`;
- `ENDLESS-MILESTONE-CHESTS` (skip to 5:00 endless: one chest per player).

**Acceptance:** the last 30 seconds before the Colossus are the busiest of the run, and
every milestone is announced and paid.

## M7 — Rift challenges

**Goal:** long-term goals that are about the Rift. They pay with unlocks and options,
**never with % stats** (the genre's "tiny meta steps" complaint).

**Behaviour:**
- A **Rift** tab in the quest log (J) with about 15 account-wide challenges, for example:
  - win the Rift;
  - win on tier 3, 5 and 10;
  - win with 3 Fear cards;
  - reach 10 and 30 endless minutes;
  - evolve any skill, and evolve all six;
  - open a 5-chest;
  - use 10 Greed shrines in one run;
  - win with only melee skills;
  - win in co-op;
  - reach run level 40.
- **Rewards (new kinds):**
  - **Unlock:** a skill joins the Rift start pick, a new keystone joins the pool, or an
    evolution recipe becomes known. Before that, its partner card does not hint at it.
  - **Cosmetic:** a portal tint or a hideout decoration from Ysolde's list.
  - **Currency:** orbs.
- Locked keystones and recipes are simply not in the pool until unlocked. **Owner:**
  confirm that gating content behind challenges is wanted (Megabonk does it and gets
  "dilution" complaints; the Toggler is their fix).

**Code:**
- `QuestCatalog.Rift` (a second list, `QuestScope.Account`) listening to M0's events.
- `RewardKind.Unlock` and `Profile.Unlocks` (a `HashSet<string>`, saved and validated).
- `UpgradeCatalog` entries and `EvolutionCatalog` entries gain `RequiresUnlock`.
- `QuestLog` gets a "Rift" tab.
- Events come from `RunState` with the run's context (tier, fears, class, co-op), via a
  `QuestEvent` key string such as `tier=3;fears=3;melee=1`.

**Tests:** `QuestSmokeTest` →
- `RIFT-CHALLENGE-UNLOCKS` (the event then the reward);
- `UNLOCK-GATES-POOL` (a keystone absent from 400 rolls before the unlock, present after);
- `UNLOCK-SAVE-COMPAT`.

**Acceptance:** after a first win the log shows which challenges came closer, and every
reward changes what a future Rift can offer.

## M8 — A second arena

**Goal:** variety. This was already an open question in run.md; Hell Clock's reviews show
what repetition costs.

**Outline (its own design pass first, with the game-designer agent):**
- A second arena with its own tileset, through the EasyPixel pipeline (`tools/art`,
  voxels for the HD-2D view).
- Content: three or four new enemies (or recoloured variants with one new behaviour
  each), its own mid boss and final boss, and one arena feature. Keep it to one
  feature, and no obstacles (run.md -> "No obstacles").
- Chosen in the Rift panel, with tiers per arena (`Profile.RiftTierCleared` becomes a
  map from arena to tier).
- `Arena` takes an `ArenaDef`; `RunDirector`'s roster table becomes per-arena data.
- Capture with `RunCapture --only=rift`. Tests: a `RunSmokeTest` timeline per arena.

**Acceptance:** both arenas play the full timeline, endless and co-op included, and the
bot clears both at similar rates on the same tier.

## Measuring (fill in during M0 and update after each milestone)

| Build (autoplay) | Level at 7:30 | Colossus kills / runs | Death time (median) | Endless minutes (median) | Chests per win |
|---|---|---|---|---|---|
| warrior (reference) | | | | | — |
| rogue (reference) | | | | | — |
| arcanist (reference) | | | | | — |
| `--skill=spark` | | | | | — |

## Open questions (collected from the milestones)

1. M1: gold rerolls from the wallet, or only from gold picked up in this run?
2. M3: a cap on Greed stacks?
3. M4: do Rift tiers align with the Mirror's tiers for item level? In co-op, is the
   tier capped by the host or by the lowest in the party?
4. M5: which six skills evolve first, with what names and effects.
5. M6: should endless milestones give a breather or only rewards?
6. M7: is gating keystones and recipes behind challenges wanted?
