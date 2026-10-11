# Bosses

Status: **implemented** (two bosses). Owner brief: *"a boss with complicated
mechanics but clearly marked in the game for players — areas that read well so
there is time to move — while still being challenging."*

## The rules every boss follows

These are enforced by the shared base (`Enemies/BossEnemy.cs`) and the telegraph
primitives, not promised per boss:

1. **Every damaging hit is drawn on the floor before it lands.** Circles and donuts
   (`AreaBurst`) and lanes (`LineStrike`) share one visual language: a faint red
   zone shows the full area, a brighter fill grows from the centre (or the origin of
   a lane) and the hit lands **exactly when the fill completes**, then flashes.
   "How long do I have?" is always answerable at a glance. Minimum warning: 0.9 s
   for anything big; the in/out check gets 2 s.
2. **Hostile telegraphs are always red**, whatever the element. Players learn one
   colour for "this will hurt me", not a code per boss. Summon markers (harmless)
   use the boss's own colour.
3. **Every attack is named.** The current attack shows under the boss bar
   (« Seismic Slam »), so a player can learn the fight by name.
4. **Must-react mechanics get a callout**: big centred text ("GET OUT!",
   "GET CLOSE!", "STAND BETWEEN THE BEAMS") plus a warning sound.
5. **Phases are announced and safe**: at each life threshold the boss stops, roars
   (shake + sound), is invulnerable for 2 s and the HUD shows "Phase N — …".
   Phase thresholds are marked on the boss bar.
6. **One attack at a time**, each followed by a recovery gap — the window to deal
   damage is part of the rhythm.
7. **Everything is dodgeable through**: the dodge's invulnerability window beats
   every hit, and boss bullets pass through a dodging player.

Bosses ignore knockback and scale gently (life with the square root of the swarm's
multiplier, damage not at all): their numbers are authored for the minute they appear.

## The Bone Herald — mid boss (3:00)

A skull-crowned lich that keeps its distance and **teaches the vocabulary** the
final fight tests. Base life 1200.

| Attack | Telegraph | What it tests |
|---|---|---|
| **Bone Spiral** | 0.7 s cast glow, then 3 (P2: 5) rotating arms of slow bolts. | Reading gaps in a pattern. |
| **Grave Circles** | 4 (P2: 6) circles dropped on your position one after another, 1.1 s each. | Keep moving. |
| **Raise Dead** | Purple summon markers for 1 s, then bats (no XP). | Target priority. |
| **Blink** | Fades out; red circle where it will reappear (1 s), nova on arrival. | Don't stand where it is going. |

Phase 2 at 50 %: denser, faster.

## The Ember Colossus — final boss (7:00)

A molten stone giant at the centre of the rune circle. The arena is cleared when it
wakes. Base life 7000, 60 armour, immovable. Three phases.

**Phase 1 (100–65 %)**

| Attack | Telegraph | Counterplay |
|---|---|---|
| **Seismic Slam** → **Aftershock** | 170 px circle on itself (1.1 s), then a donut from 170 to 310 px (0.9 s). | Leave the circle — but not just barely, or the Aftershock ring catches you. Or dodge through both. |
| **Molten Rain** | 7 (P2: 10) circles landing on and *slightly ahead of* where you are running, 0.28 s apart. | Change direction. |
| **Charge** | A 620×90 px lane for 0.95 s, then it charges along it (P2: twice). Contact during the charge hurts 2.5×. | Step sideways. |
| **Fire Fan** | Wind-up pose 0.45 s, then a 70° fan of 7 (P2: 9) slow orbs, 3–4 volleys alternating offsets. | Stand in the gaps; volleys alternate. |

**Phase 2 (65–30 %)** adds:

| Attack | Telegraph | Counterplay |
|---|---|---|
| **Inferno Ring** — the in/out check | "GET OUT!" (inner 200 px circle burns) **or** "GET CLOSE!" (the donut from 200 to 720 px burns), 2 s, ~a third of a fresh character's life. | Read the callout, go to the safe side. |
| **Call the Brood** | Orange markers 0.9 s, then volatile imps. | Kill or avoid them before they prime. |

**Phase 3 (below 30 %)** — enrage: every attack is 30 % faster, a ring of 10 orbs
bursts out every 5 s, and it adds:

| Attack | Telegraph | Counterplay |
|---|---|---|
| **Flame Beams** | 4 lanes from its body across the arena, 1.25 s, then again rotated 45°, three waves. | Stand between beams; move one slot each wave. |

## Nereth, the Drowned Reflection — boss of the Drowned Mirror

Added with the endgame (docs/design/endgame.md). A mirror-faced sea spirit that
keeps a middle distance; **cold damage throughout**, so cold resistance matters
for the first time. Phases at 60 % and 30 %.

| Attack | Telegraph | Answer |
|---|---|---|
| **Tidal Lanes** | 7 parallel lanes across your position with **one gap**, 1.2 s; two or three waves, horizontal or vertical. | Find the gap each wave. |
| **Whirlpool** | A ring closes on you (1.0 s, the eye is safe) — then the eye erupts a beat later ("THE EYE ERUPTS"). | Stay in the eye for the ring, step out for the eruption. |
| **Orb Spiral** | 3–4 arms of orbs spinning out for ~2 s. | Walk with the spin through the gaps. |
| **Mirror Images** (phase 2+) | Two translucent reflections appear mirrored around you; each of the three fires a lane at you plus two side lanes, 1.25 s ("WATCH THE REFLECTIONS"). | Read three sources at once; leave the crossing. |
| **Rising Tide** (phase 3) | The whole room washes blue; three gold circles mark shelter, 2.4 s ("FIND THE LIGHT!"). | Stand in a circle — or roll through the wave the instant it lands. |

Rising Tide is the first "safe zone" mechanic (the inverse of a slam): the
danger is everywhere except the marked spots. It is implemented as a check at
impact time, so the dodge's invulnerability still saves a late player.

## Bosses inside realities

The Colossus and the Herald became reality bosses: asleep in their room until you
enter, then the gate closes. Their summons register with the reality (scaled to
its tier) and their target points clamp to walkable cells, since there is no
arena rectangle. Reality boss life = authored life × tier multiplier × a
per-theme factor (Colossus 0.45, Herald 2.6, Nereth 0.5) tuned with the bot.

## Why it is shaped like this

The fight is a *reading* test more than a reflex test: every attack is visible
early, and the challenge comes from overlap — Molten Rain while the Phase 3 orb
ring is out, an Inferno Ring call while you are dodging a charge. That is the
"complicated but clearly marked" brief: the information is always on screen, the
difficulty is acting on all of it at once with two dodge charges.

## Implementation

- `BossEnemy` runs attacks as `async` sequences (`await Wait(seconds)` on physics-time
  timers that pause with the game); every wait re-checks that the boss is alive, so a
  boss that dies mid-attack stops cleanly.
- `Slam` / `Lane` / `Bullet` helpers wrap `AreaBurst`, `LineStrike` and
  `BossBullet`; the player's Frost Nova and Meteor use the same `AreaBurst`.
- `MechanicCalled` and `PhaseChanged` events feed the HUD; the director emits
  `BossSpawned` so the HUD can attach.

## Open questions

- Tune damage after real playtests (the autoplay bot only approximates a player).
- A third boss for a second arena. Candidate mechanic not yet used: a *soak* (stand
  in the circle to stop a wipe) — it would add a "go toward danger" decision.
- Boss-specific loot (a unique item from the Colossus).
