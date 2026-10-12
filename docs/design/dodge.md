# Dodge

Status: **implemented**. Owner brief: *"the player can dodge with Space with a small
invulnerability window, typical, but with charges — not infinite, recharging over
time."*

## What it is

- **Space** dashes 150 px over 0.16 s in the movement direction (WASD), or toward
  the cursor when standing still.
- **Invulnerability** for the dash plus a 0.14 s tail (0.30 s total). While
  invulnerable, `Health.TakeDamage` ignores hits entirely: no damage, no ailments,
  no thorns. Boss bullets pass *through* a dodging player instead of being consumed.
- **Charges:** the player holds `DodgeCharges` (stat, base **2**). Each spent
  charge refills on its own 2.4 s timer, sped up by `DodgeRecovery` (stat,
  base 1.0). The HUD shows charges as diamonds under the life/mana bars, filling
  while they recharge.
- Afterimages trail the dash; the sprite does not flicker during a dodge (it does
  during other invulnerability, e.g. never at the moment — reserved for a post-hit
  grace window if one is added).

## Why

Charges rather than a cooldown let a player chain two dodges through a boss combo
(Seismic Slam → Aftershock is designed to be dodgeable that way) while making
"dodge every hit" impossible: the escape is a resource you manage, not a reflex you
mash. The window is short enough that dodging *through* a telegraph needs timing,
long enough that it works on a 60 Hz frame budget with input latency.

Both numbers are stats, so they are build levers: Acrobatics (+1 charge, notable),
Light Step (faster recharge), and the Phase Dancer keystone (+2 charges, faster
recharge, every dodge casts a frost nova, but 15 % more damage taken).

## Implementation

`scripts/Dodge.cs` — a sibling component on the player. `Player._PhysicsProcess`
defers to it while `IsDashing`. `TryDodge(direction)` is public so AI, tests and the
autoplay bot can drive it. `Health.GrantInvulnerability(seconds)` is a timer (the
longer of overlapping grants wins), plus a debug-only `IgnoreDamage` flag used by
capture tools.

## Alternatives considered

- **Stamina bar.** More granular but one more bar to read during a boss fight;
  charges read at a glance.
- **Dodge cancels skills / has recovery frames.** Deferred: skills auto-cast and
  have no commitment yet (docs/design/skills.md → movement during use is Free).

## Open questions

- A brief post-hit grace window (e.g. 0.15 s) if swarm contact damage stacks feel
  unfair in playtesting.
- Gear affixes for dodge charges / recovery.
