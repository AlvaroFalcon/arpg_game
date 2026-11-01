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
