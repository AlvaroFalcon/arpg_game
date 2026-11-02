# The playground (2026-11-01) **[IMPLEMENTED]**

Owner's brief: *"a more secret way into a playground where I can spawn a horde of
enemies of mirror tier X, or a boss of any tier, with every skill unlocked and maxed;
Escaldón and the crafting NPC should be there too."*

## What it is

A hidden test area for builds and balance. Not on any menu.

- **Entry:** in the hideout, **Ctrl+Shift+P** (solo only; in a party it says so), or start
  the game with `-- --playground` (straight in once the hideout loads).
- **Place:** *The Proving Ground*, a fixed room layout (`LayoutCatalog.Playground`): a
  camp where you arrive, with **Escaldón** and **the Artificer** standing in it (F to talk:
  the same `SkillTreePanel` and `ArtificerPanel` as in the town), and a wide empty arena
  joined by a broad corridor. No packs, no boss of its own, no gate.
- **Character:** a **sandbox copy** of the active one (`Playground.MakeSandbox`): same
  class, level, passive tree, ascendancy and gear, plus **every skill owned at level 20**
  (trees empty: spend the 19 points at Escaldón), 50,000,000 gold and 999 of every orb.
- **Spawner (F2, open on arrival, left edge):** Mirror tier (1–20), monster count, monster
  type (any or one), rarity (a reality's mix, or all normal/magic/rare/unique), *Spawn
  horde*, a boss (Ember Colossus, Bone Herald, Nereth) and *Spawn boss*, *Clear
  monsters*, character level (rebuilds the character: passive points to spend), *Full
  life, mana and cooldowns*, and *Can't die*. Spawns land in the arena, or ahead of you
  when you are in it. A boss's kill time is shown (useful for the life table).
- **Scaling:** exactly a real reality's at the chosen tier (`SpawnMonster` /
  `SpawnBoss` numbers: `MonsterScaling`, `RealityTiers.DamageMultiplier`, defences),
  with each boss keeping its own reality's life multiplier.
- **No rewards:** kills give no experience and drop nothing; a boss kill opens no portal
  and changes no record.

## Keyboard focus (2026-11-02)

Owner's report: after clicking an option in the spawner, Space (the dodge) pressed it again,
even after casting. Godot gives a clicked button keyboard focus, and Space is its "press".
Now no button, list or checkbox of the spawner or the DPS meter takes focus; a number field
(tier, count, level) still does while typing, and lets go once its value is set, when the
mouse leaves the panel, or on any click in the world (`Playground.KeepKeyboardForTheGame`).
`PANEL-FOCUS` (PlaygroundTest).

## Monsters that hold (2026-11-02)

Owner's request: monsters that stay put, to test more things. Two switches in the
spawner, independent so they combine:

- **Monsters stand still** (`Enemy.HoldStill`): they keep thinking and attacking — a caster
  still casts, a boss still runs its mechanics — but never walk. Knockback still moves them,
  so pushes can be tested; a boss that teleports or leaps as part of an attack still does.
- **Monsters don't attack** (`Enemy.Pacified`): no decisions and no contact damage, so they
  neither attack nor walk: living targets with real life, armour and resistances.

Both are static switches on `Enemy`, reset when the playground closes (`Playground._ExitTree`),
so they never reach a real reality. `MONSTERS-HOLD` (PlaygroundTest) checks that held
monsters don't move, pacified ones don't hurt while touching the player, and both chase again
when switched off; `LEAVES-CLEAN` that leaving resets them.

## DPS meter (2026-11-01)

Owner's follow-up: *"a DPS meter in the playground, to know how much damage characters
do and balance them"*. A panel on the right (F3 hides it), `UI.DpsMeter` over the
node-free `Combat.DpsTracker`:

- **What counts:** everything the player deals to anything that is not a player — hits
  (`Health.AnyHitResolved`, the damage actually taken, so after resistances and armour)
  and damage that was already resolved (`Health.AnyResolvedDamage`, new: bleed, ignite,
  Corruption ticks and Combust/Exsanguinate bursts, each with its label). Totem casts and
  kill bursts count as the player's. Overkill counts (a kill's last hit counts in full).
- **What it shows:** DPS over the **last 5 s** (big number), the **fight's** DPS (from its
  first damage to its last, so walking between packs doesn't dilute it; 5 s without damage
  starts a new fight), total, the **peak** second, hits and crit rate, and a breakdown by
  source (skill name, or Bleed/Ignite/...) with each share and its DPS. *Reset* clears it.
- **Time is game time** (summed physics steps), not the wall clock: on a slow machine the
  game slows down too, and the meter stays right.
- **Training dummy** (spawner): an immortal target with no armour and no resistances that
  heals every hit, for measuring a rotation as long as wanted; it can bleed and burn.
  Monsters of a tier are the other yardstick (their resistances and armour apply).

`DPS-TRACKER` and `DPS-METER` (PlaygroundTest) check the arithmetic, the sources, that
someone else's damage is ignored, and a real cast against the dummy.

## Bug: the playground loaded dead (fixed 2026-11-01)

The owner's first try: a black screen, an empty skill bar and a banner that never faded.
Ctrl+Shift+P is also **P**, and the panel keys matched actions loosely (extra modifiers
ignored), so the passive tree opened — and paused the game — in the same frame the
playground was launched; the playground loaded paused, with a freed panel on the stack.
Fixed three ways: the panel keys now match exactly (`CharacterPanels.PollToggles`,
`exactMatch`), the hideout launches the playground a moment later (closing any panel that
opened), and **every reality now starts unpaused with an empty panel stack**. `NOT-PAUSED`
(PlaygroundTest) launches with a pausing panel open on the way out and fails on the old code.

## Nothing is saved

On the way in, the hideout captures and saves the real character, then turns
`Profile.Persist` off and makes the sandbox the active character. On the way out (Esc →
*Leave the playground*), `Playground.RestoreProfile` reloads the profile **from disk** and
restores `Persist`: the sandbox, its gold, crafted items, tree points and the account's
orbs are thrown away. A crash inside the playground loses nothing either (nothing was
written). `PlaygroundTest` checks the round trip.

Rejected: a debug menu in the normal game (the owner asked for something secret);
spending real gold or orbs (a test area that eats the save would never be used); a
separate scene (the reality already does map building, scaling, HUD, pause and death).

Open: monsters keep the reality's AI (they chase you into the camp); there are no target
dummies (a tier-1 normal horde does the job); co-op playground.
