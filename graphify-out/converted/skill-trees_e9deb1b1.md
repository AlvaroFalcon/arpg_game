<!-- converted from skill-trees.xlsx -->

## Sheet: Habilidades
| Habilidad | Id (no tocar) | Estado | Tipo | Etiquetas | Nivel de personaje para comprarla | Daño base | Tiempo de uso (s) | Cooldown (s) | Maná (nivel 1) | Descripción | Notas / cambios que quieres |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Fireball | fireball | Cambiada | Projectile | Spell, Projectile, Fire | 1 | 30 | 0.6 | 0 | 6 | Hurls a slow ball of fire that bursts where it lands, burning everything nearby. Fire hits can ignite. |  |
| Spark | spark | Cambiada | Projectile | Spell, Projectile, Lightning | 4 | 14 | 0.45 | 0 | 5 | Releases slow, crackling sparks that zigzag like lightning, pass through one enemy and bounce off walls. Lightning hits can shock. |  |
| Frost Nova | frost_nova |  | Area | Spell, Area, Cold | 8 | 20 | 0.35 | 3 | 10 | A ring of ice bursts around you, chilling and pushing enemies back. |  |
| Ethereal Blades | blades |  | Orbit | Spell, Area, Duration | 8 | 14 | 0.3 | 6 | 12 | Summons blades that orbit you, cutting anything they touch. |  |
| Meteor | meteor |  | Area | Spell, Area, Fire | 16 | 50 | 0.5 | 5 | 18 | Calls down a meteor at the target after a short delay. Huge damage, small area. |  |
| Cleave | cleave |  | Melee | Attack, Melee, Area | 1 | 24 | 0.55 | 0 | 3 | A wide swing that hits every enemy in front of you. Scales with weapon damage. |  |
| Ground Slam | ground_slam |  | Area | Attack, Melee, Area | 4 | 34 | 0.8 | 1 | 7 | Smashes the ground just ahead, knocking enemies back. |  |
| Sunder | sunder |  | Melee | Attack, Melee, Area | 12 | 36 | 0.75 | 1.5 | 8 | Splits the ground in a line ahead of you after a brief wind-up. |  |
| Double Strike | double_strike |  | Melee | Attack, Melee | 1 | 15 | 0.42 | 0 | 2 | Two quick stabs in a narrow arc. Fast, precise, crit-friendly. |  |
| Throwing Knives | throwing_knives |  | Projectile | Attack, Projectile | 4 | 8 | 0.5 | 0 | 4 | Throws a fan of knives that pierce one enemy each. |  |
| Frost Shards | frost_shards |  | Projectile | Spell, Projectile, Cold | 6 | 9 | 0.45 | 0 | 6 | Hurls a fan of ice shards. Cold hits chill enemies. |  |
| Storm Strike | storm_strike |  | Melee | Attack, Melee, Lightning | 10 | 20 | 0.4 | 0 | 4 | A lightning-charged thrust in a narrow arc. Lightning hits can shock. |  |
| Glacial Hammer | glacial_hammer |  | Melee | Attack, Melee, Area, Cold | 14 | 30 | 0.7 | 0 | 5 | A heavy overhead blow that shatters frost over a wide arc. Cold hits chill enemies. |  |
| Corruption | corruption | Nueva | Curse | Spell, Duration, Curse, Shadow | 3 | 12 | 0.5 | 0 | 10 | Curses the enemy nearest the cursor with a corruption that eats it away: shadow damage every second for 6 s. No resistance stops it. |  |
| Curse of Elements | curse_elements | Nueva | Curse | Spell, Area, Duration, Curse | 8 | 0 | 0.45 | 4 | 14 | Curses every enemy in an area: their fire, cold and lightning resistances drop by 25 for 6 s. |  |
| Curse of Time | curse_time | Nueva | Curse | Spell, Area, Duration, Curse | 12 | 0 | 0.45 | 6 | 16 | Curses every enemy in an area: they move 35% slower for 5 s. |  |
## Sheet: Mejoras (árboles)
| Habilidad | Rama | Columna (1-3) | Mejora | Tipo | Rangos | Rango 1 | Rango 2 | Rango 3 | Requiere | Puntos necesarios en la anterior | Grupo excluyente (elige una) | Notas / cambios que quieres |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Fireball | Inferno | 1 | Power | Menor | 3 | 8% more damage | 16% more damage | 25% more damage |  |  |  |  |
| Fireball | Inferno | 2 | Wider Blast | Menor | 3 | The blast is 15% larger | The blast is 30% larger | The blast is 50% larger | Power | 2 |  |  |
| Fireball | Inferno | 3 | Firestorm | Notable | 1 | The blast deals the full hit instead of half |  |  | Wider Blast | 2 |  |  |
| Fireball | Wildfire | 1 | Kindle | Menor | 3 | +10% chance to ignite | +20% chance to ignite | +30% chance to ignite |  |  |  |  |
| Fireball | Wildfire | 2 | Searing | Menor | 3 | Ignites deal 20% more damage | Ignites deal 40% more damage | Ignites deal 70% more damage | Kindle | 2 |  |  |
| Fireball | Wildfire | 3 | Detonation | Notable | 1 | Kills burst for 30% of the hit around the body |  |  | Searing | 2 |  |  |
| Fireball | Form | 1 | Velocity | Menor | 3 | 10% faster projectiles | 20% faster projectiles | 35% faster projectiles |  |  |  |  |
| Fireball | Form | 2 | Efficiency | Menor | 3 | 8% less mana cost | 16% less mana cost | 25% less mana cost | Velocity | 2 |  |  |
| Fireball | Form | 3 | Volley | Notable | 1 | +1 projectile, 10% less damage |  |  | Efficiency | 2 | fireball_form |  |
| Fireball | Form | 3 | Frostfire | Notable | 1 | Converts the damage to cold |  |  | Efficiency | 2 | fireball_form |  |
| Spark | Charge | 1 | Power | Menor | 3 | 8% more damage | 16% more damage | 25% more damage |  |  |  |  |
| Spark | Charge | 2 | Static | Menor | 3 | +10% chance to shock | +20% chance to shock | +30% chance to shock | Power | 2 |  |  |
| Spark | Charge | 3 | Ricochet | Notable | 1 | Projectiles chain to 2 more enemies |  |  | Static | 2 | spark_charge |  |
| Spark | Charge | 3 | Arc Blast | Notable | 1 | Sparks burst on hit (40 px, 40% of the hit) |  |  | Static | 2 | spark_charge |  |
| Spark | Storm | 1 | Haste | Menor | 3 | 5% faster to use | 10% faster to use | 15% faster to use |  |  |  |  |
| Spark | Storm | 2 | Efficiency | Menor | 3 | 8% less mana cost | 16% less mana cost | 25% less mana cost | Haste | 2 |  |  |
| Spark | Storm | 3 | Volley | Notable | 1 | +1 projectile, 10% less damage |  |  | Efficiency | 2 | spark_storm |  |
| Spark | Storm | 3 | Piercing | Notable | 1 | Projectiles pass through 2 more enemies |  |  | Efficiency | 2 | spark_storm |  |
| Spark | Path | 1 | Rebound | Menor | 3 | +1 wall bounce | +2 wall bounces | +3 wall bounces |  |  |  |  |
| Spark | Path | 2 | Long Arc | Menor | 3 | Sparks fly 15% farther | Sparks fly 30% farther | Sparks fly 50% farther | Rebound | 2 |  |  |
| Spark | Path | 3 | Grounded | Notable | 1 | Converts the damage to physical: 15% more damage and +25% chance to cause bleeding |  |  | Long Arc | 2 | spark_path |  |
| Spark | Path | 3 | Detonation | Notable | 1 | Kills burst for 30% of the hit around the body |  |  | Long Arc | 2 | spark_path |  |
| Frost Nova | Winter | 1 | Power | Menor | 3 | 8% more damage | 16% more damage | 25% more damage |  |  |  |  |
| Frost Nova | Winter | 2 | Rime | Menor | 3 | +5% chance to freeze | +10% chance to freeze | +15% chance to freeze | Power | 2 |  |  |
| Frost Nova | Winter | 3 | Overwhelm | Notable | 1 | 35% more damage, 25% more mana cost |  |  | Rime | 2 |  |  |
| Frost Nova | Reach | 1 | Reach | Menor | 3 | 10% more area | 20% more area | 30% more area |  |  |  |  |
| Frost Nova | Reach | 2 | Gale | Menor | 3 | 10% more damage | 20% more damage | 30% more damage | Reach | 2 |  |  |
| Frost Nova | Reach | 3 | Shockwave | Notable | 1 | 40% more area, 10% less damage |  |  | Gale | 2 |  |  |
| Frost Nova | Rhythm | 1 | Patience | Menor | 3 | 10% shorter cooldown | 20% shorter cooldown | 30% shorter cooldown |  |  |  |  |
| Frost Nova | Rhythm | 2 | Efficiency | Menor | 3 | 8% less mana cost | 16% less mana cost | 25% less mana cost | Patience | 2 |  |  |
| Frost Nova | Rhythm | 3 | Echo | Notable | 1 | The skill repeats once, 20% less damage |  |  | Efficiency | 2 | nova_rhythm |  |
| Frost Nova | Rhythm | 3 | Static Nova | Notable | 1 | Converts the damage to lightning |  |  | Efficiency | 2 | nova_rhythm |  |
| Ethereal Blades | Steel | 1 | Power | Menor | 3 | 8% more damage | 16% more damage | 25% more damage |  |  |  |  |
| Ethereal Blades | Steel | 2 | Laceration | Menor | 3 | 10% chance to cause bleeding | 20% chance to cause bleeding | 30% chance to cause bleeding | Power | 2 |  |  |
| Ethereal Blades | Steel | 3 | Overwhelm | Notable | 1 | 35% more damage, 25% more mana cost |  |  | Laceration | 2 |  |  |
| Ethereal Blades | Storm | 1 | Reach | Menor | 3 | 10% more area | 20% more area | 30% more area |  |  |  |  |
| Ethereal Blades | Storm | 2 | Blade Count | Menor | 2 | +1 blade | +2 blades |  | Reach | 2 |  |  |
| Ethereal Blades | Storm | 3 | Ember Blades | Notable | 1 | Converts the damage to fire |  |  | Blade Count | 2 | blades_storm |  |
| Ethereal Blades | Storm | 3 | Frost Blades | Notable | 1 | Converts the damage to cold |  |  | Blade Count | 2 | blades_storm |  |
| Ethereal Blades | Time | 1 | Lingering | Menor | 3 | 15% longer duration | 30% longer duration | 50% longer duration |  |  |  |  |
| Ethereal Blades | Time | 2 | Patience | Menor | 3 | 10% shorter cooldown | 20% shorter cooldown | 30% shorter cooldown | Lingering | 2 |  |  |
| Ethereal Blades | Time | 3 | Detonation | Notable | 1 | Kills burst for 30% of the hit around the body |  |  | Patience | 2 |  |  |
| Meteor | Impact | 1 | Power | Menor | 3 | 8% more damage | 16% more damage | 25% more damage |  |  |  |  |
| Meteor | Impact | 2 | Kindle | Menor | 3 | +10% chance to ignite | +20% chance to ignite | +30% chance to ignite | Power | 2 |  |  |
| Meteor | Impact | 3 | Overwhelm | Notable | 1 | 35% more damage, 25% more mana cost |  |  | Kindle | 2 |  |  |
| Meteor | Crater | 1 | Reach | Menor | 3 | 10% more area | 20% more area | 30% more area |  |  |  |  |
| Meteor | Crater | 2 | Searing | Menor | 3 | Ignites deal 20% more damage | Ignites deal 40% more damage | Ignites deal 70% more damage | Reach | 2 |  |  |
| Meteor | Crater | 3 | Detonation | Notable | 1 | Kills burst for 30% of the hit around the body |  |  | Searing | 2 |  |  |
| Meteor | Sky | 1 | Patience | Menor | 3 | 10% shorter cooldown | 20% shorter cooldown | 30% shorter cooldown |  |  |  |  |
| Meteor | Sky | 2 | Efficiency | Menor | 3 | 8% less mana cost | 16% less mana cost | 25% less mana cost | Patience | 2 |  |  |
| Meteor | Sky | 3 | Echo | Notable | 1 | The skill repeats once, 20% less damage |  |  | Efficiency | 2 | meteor_sky |  |
| Meteor | Sky | 3 | Comet | Notable | 1 | Converts the damage to cold |  |  | Efficiency | 2 | meteor_sky |  |
| Cleave | Might | 1 | Power | Menor | 3 | 8% more damage | 16% more damage | 25% more damage |  |  |  |  |
| Cleave | Might | 2 | Haste | Menor | 3 | 5% faster to use | 10% faster to use | 15% faster to use | Power | 2 |  |  |
| Cleave | Might | 3 | Overwhelm | Notable | 1 | 35% more damage, 25% more mana cost |  |  | Haste | 2 |  |  |
| Cleave | Arc | 1 | Reach | Menor | 3 | 10% more area | 20% more area | 30% more area |  |  |  |  |
| Cleave | Arc | 2 | Efficiency | Menor | 3 | 8% less mana cost | 16% less mana cost | 25% less mana cost | Reach | 2 |  |  |
| Cleave | Arc | 3 | Shockwave | Notable | 1 | 40% more area, 10% less damage |  |  | Efficiency | 2 | cleave_arc |  |
| Cleave | Arc | 3 | Flurry | Notable | 1 | Strikes one more time, 15% less damage |  |  | Efficiency | 2 | cleave_arc |  |
| Cleave | Blood | 1 | Laceration | Menor | 3 | 10% chance to cause bleeding | 20% chance to cause bleeding | 30% chance to cause bleeding |  |  |  |  |
| Cleave | Blood | 2 | Rend | Menor | 3 | 10% more damage | 20% more damage | 30% more damage | Laceration | 2 |  |  |
| Cleave | Blood | 3 | Flame Edge | Notable | 1 | Converts the damage to fire |  |  | Rend | 2 | cleave_blood |  |
| Cleave | Blood | 3 | Detonation | Notable | 1 | Kills burst for 30% of the hit around the body |  |  | Rend | 2 | cleave_blood |  |
| Ground Slam | Quake | 1 | Power | Menor | 3 | 8% more damage | 16% more damage | 25% more damage |  |  |  |  |
| Ground Slam | Quake | 2 | Reach | Menor | 3 | 10% more area | 20% more area | 30% more area | Power | 2 |  |  |
| Ground Slam | Quake | 3 | Shockwave | Notable | 1 | 40% more area, 10% less damage |  |  | Reach | 2 |  |  |
| Ground Slam | Fault | 1 | Laceration | Menor | 3 | 10% chance to cause bleeding | 20% chance to cause bleeding | 30% chance to cause bleeding |  |  |  |  |
| Ground Slam | Fault | 2 | Fault Line | Menor | 3 | 10% more damage | 20% more damage | 30% more damage | Laceration | 2 |  |  |
| Ground Slam | Fault | 3 | Magma | Notable | 1 | Converts the damage to fire |  |  | Fault Line | 2 | slam_fault |  |
| Ground Slam | Fault | 3 | Detonation | Notable | 1 | Kills burst for 30% of the hit around the body |  |  | Fault Line | 2 | slam_fault |  |
| Ground Slam | Rhythm | 1 | Patience | Menor | 3 | 10% shorter cooldown | 20% shorter cooldown | 30% shorter cooldown |  |  |  |  |
| Ground Slam | Rhythm | 2 | Efficiency | Menor | 3 | 8% less mana cost | 16% less mana cost | 25% less mana cost | Patience | 2 |  |  |
| Ground Slam | Rhythm | 3 | Echo | Notable | 1 | The skill repeats once, 20% less damage |  |  | Efficiency | 2 |  |  |
| Sunder | Rift | 1 | Power | Menor | 3 | 8% more damage | 16% more damage | 25% more damage |  |  |  |  |
| Sunder | Rift | 2 | Laceration | Menor | 3 | 10% chance to cause bleeding | 20% chance to cause bleeding | 30% chance to cause bleeding | Power | 2 |  |  |
| Sunder | Rift | 3 | Overwhelm | Notable | 1 | 35% more damage, 25% more mana cost |  |  | Laceration | 2 |  |  |
| Sunder | Length | 1 | Reach | Menor | 3 | 10% more area | 20% more area | 30% more area |  |  |  |  |
| Sunder | Length | 2 | Split Earth | Menor | 3 | 10% more damage | 20% more damage | 30% more damage | Reach | 2 |  |  |
| Sunder | Length | 3 | Volcanic Rift | Notable | 1 | Converts the damage to fire |  |  | Split Earth | 2 | sunder_length |  |
| Sunder | Length | 3 | Detonation | Notable | 1 | Kills burst for 30% of the hit around the body |  |  | Split Earth | 2 | sunder_length |  |
| Sunder | Rhythm | 1 | Patience | Menor | 3 | 10% shorter cooldown | 20% shorter cooldown | 30% shorter cooldown |  |  |  |  |
| Sunder | Rhythm | 2 | Efficiency | Menor | 3 | 8% less mana cost | 16% less mana cost | 25% less mana cost | Patience | 2 |  |  |
| Sunder | Rhythm | 3 | Echo | Notable | 1 | The skill repeats once, 20% less damage |  |  | Efficiency | 2 |  |  |
| Double Strike | Precision | 1 | Power | Menor | 3 | 8% more damage | 16% more damage | 25% more damage |  |  |  |  |
| Double Strike | Precision | 2 | Reach | Menor | 3 | 10% more area | 20% more area | 30% more area | Power | 2 |  |  |
| Double Strike | Precision | 3 | Overwhelm | Notable | 1 | 35% more damage, 25% more mana cost |  |  | Reach | 2 |  |  |
| Double Strike | Tempo | 1 | Haste | Menor | 3 | 5% faster to use | 10% faster to use | 15% faster to use |  |  |  |  |
| Double Strike | Tempo | 2 | Efficiency | Menor | 3 | 8% less mana cost | 16% less mana cost | 25% less mana cost | Haste | 2 |  |  |
| Double Strike | Tempo | 3 | Flurry | Notable | 1 | Strikes one more time, 15% less damage |  |  | Efficiency | 2 |  |  |
| Double Strike | Wounds | 1 | Laceration | Menor | 3 | 10% chance to cause bleeding | 20% chance to cause bleeding | 30% chance to cause bleeding |  |  |  |  |
| Double Strike | Wounds | 2 | Deep Cuts | Menor | 3 | 10% more damage | 20% more damage | 30% more damage | Laceration | 2 |  |  |
| Double Strike | Wounds | 3 | Storm Edge | Notable | 1 | Converts the damage to lightning |  |  | Deep Cuts | 2 | ds_wounds |  |
| Double Strike | Wounds | 3 | Detonation | Notable | 1 | Kills burst for 30% of the hit around the body |  |  | Deep Cuts | 2 | ds_wounds |  |
| Throwing Knives | Edge | 1 | Power | Menor | 3 | 8% more damage | 16% more damage | 25% more damage |  |  |  |  |
| Throwing Knives | Edge | 2 | Haste | Menor | 3 | 5% faster to use | 10% faster to use | 15% faster to use | Power | 2 |  |  |
| Throwing Knives | Edge | 3 | Piercing | Notable | 1 | Projectiles pass through 2 more enemies |  |  | Haste | 2 | knives_edge |  |
| Throwing Knives | Edge | 3 | Ricochet | Notable | 1 | Projectiles chain to 2 more enemies |  |  | Haste | 2 | knives_edge |  |
| Throwing Knives | Fan | 1 | Velocity | Menor | 3 | 10% faster projectiles | 20% faster projectiles | 35% faster projectiles |  |  |  |  |
| Throwing Knives | Fan | 2 | Wide Fan | Menor | 3 | 10% more damage | 20% more damage | 30% more damage | Velocity | 2 |  |  |
| Throwing Knives | Fan | 3 | Volley | Notable | 1 | +1 projectile, 10% less damage |  |  | Wide Fan | 2 |  |  |
| Throwing Knives | Venom | 1 | Laceration | Menor | 3 | 10% chance to cause bleeding | 20% chance to cause bleeding | 30% chance to cause bleeding |  |  |  |  |
| Throwing Knives | Venom | 2 | Efficiency | Menor | 3 | 8% less mana cost | 16% less mana cost | 25% less mana cost | Laceration | 2 |  |  |
| Throwing Knives | Venom | 3 | Elemental Tips | Notable | 1 | Converts the damage to lightning |  |  | Efficiency | 2 | knives_venom |  |
| Throwing Knives | Venom | 3 | Detonation | Notable | 1 | Kills burst for 30% of the hit around the body |  |  | Efficiency | 2 | knives_venom |  |
| Frost Shards | Ice | 1 | Power | Menor | 3 | 8% more damage | 16% more damage | 25% more damage |  |  |  |  |
| Frost Shards | Ice | 2 | Rime | Menor | 3 | +5% chance to freeze | +10% chance to freeze | +15% chance to freeze | Power | 2 |  |  |
| Frost Shards | Ice | 3 | Overwhelm | Notable | 1 | 35% more damage, 25% more mana cost |  |  | Rime | 2 |  |  |
| Frost Shards | Volley | 1 | Velocity | Menor | 3 | 10% faster projectiles | 20% faster projectiles | 35% faster projectiles |  |  |  |  |
| Frost Shards | Volley | 2 | Efficiency | Menor | 3 | 8% less mana cost | 16% less mana cost | 25% less mana cost | Velocity | 2 |  |  |
| Frost Shards | Volley | 3 | Volley | Notable | 1 | +1 projectile, 10% less damage |  |  | Efficiency | 2 | shards_volley |  |
| Frost Shards | Volley | 3 | Piercing | Notable | 1 | Projectiles pass through 2 more enemies |  |  | Efficiency | 2 | shards_volley |  |
| Frost Shards | Shatter | 1 | Haste | Menor | 3 | 5% faster to use | 10% faster to use | 15% faster to use |  |  |  |  |
| Frost Shards | Shatter | 2 | Brittle | Menor | 3 | 10% more damage, shards fly 5% slower | 20% more damage, shards fly 10% slower | 30% more damage, shards fly 15% slower | Haste | 2 |  |  |
| Frost Shards | Shatter | 3 | Detonation | Notable | 1 | Kills burst for 30% of the hit around the body |  |  | Brittle | 2 |  |  |
| Storm Strike | Thunder | 1 | Power | Menor | 3 | 8% more damage | 16% more damage | 25% more damage |  |  |  |  |
| Storm Strike | Thunder | 2 | Static | Menor | 3 | +10% chance to shock | +20% chance to shock | +30% chance to shock | Power | 2 |  |  |
| Storm Strike | Thunder | 3 | Overwhelm | Notable | 1 | 35% more damage, 25% more mana cost |  |  | Static | 2 |  |  |
| Storm Strike | Tempo | 1 | Haste | Menor | 3 | 5% faster to use | 10% faster to use | 15% faster to use |  |  |  |  |
| Storm Strike | Tempo | 2 | Efficiency | Menor | 3 | 8% less mana cost | 16% less mana cost | 25% less mana cost | Haste | 2 |  |  |
| Storm Strike | Tempo | 3 | Flurry | Notable | 1 | Strikes one more time, 15% less damage |  |  | Efficiency | 2 |  |  |
| Storm Strike | Conduit | 1 | Reach | Menor | 3 | 10% more area | 20% more area | 30% more area |  |  |  |  |
| Storm Strike | Conduit | 2 | Charged | Menor | 3 | 10% more damage | 20% more damage | 30% more damage | Reach | 2 |  |  |
| Storm Strike | Conduit | 3 | Detonation | Notable | 1 | Kills burst for 30% of the hit around the body |  |  | Charged | 2 | storm_conduit |  |
| Storm Strike | Conduit | 3 | Bare Steel | Notable | 1 | Converts the damage to physical: 15% more damage and +25% chance to cause bleeding |  |  | Charged | 2 | storm_conduit |  |
| Glacial Hammer | Glacier | 1 | Power | Menor | 3 | 8% more damage | 16% more damage | 25% more damage |  |  |  |  |
| Glacial Hammer | Glacier | 2 | Rime | Menor | 3 | +5% chance to freeze | +10% chance to freeze | +15% chance to freeze | Power | 2 |  |  |
| Glacial Hammer | Glacier | 3 | Overwhelm | Notable | 1 | 35% more damage, 25% more mana cost |  |  | Rime | 2 |  |  |
| Glacial Hammer | Weight | 1 | Reach | Menor | 3 | 10% more area | 20% more area | 30% more area |  |  |  |  |
| Glacial Hammer | Weight | 2 | Heavy Head | Menor | 3 | 10% more damage | 20% more damage | 30% more damage | Reach | 2 |  |  |
| Glacial Hammer | Weight | 3 | Detonation | Notable | 1 | Kills burst for 30% of the hit around the body |  |  | Heavy Head | 2 | hammer_weight |  |
| Glacial Hammer | Weight | 3 | Shockwave | Notable | 1 | 40% more area, 10% less damage |  |  | Heavy Head | 2 | hammer_weight |  |
| Glacial Hammer | Rhythm | 1 | Haste | Menor | 3 | 5% faster to use | 10% faster to use | 15% faster to use |  |  |  |  |
| Glacial Hammer | Rhythm | 2 | Efficiency | Menor | 3 | 8% less mana cost | 16% less mana cost | 25% less mana cost | Haste | 2 |  |  |
| Glacial Hammer | Rhythm | 3 | Flurry | Notable | 1 | Strikes one more time, 15% less damage |  |  | Efficiency | 2 |  |  |
| Corruption | Rot | 1 | Power | Menor | 3 | 8% more damage | 16% more damage | 25% more damage |  |  |  |  |
| Corruption | Rot | 2 | Lingering | Menor | 3 | 15% longer duration | 30% longer duration | 50% longer duration | Power | 2 |  |  |
| Corruption | Rot | 3 | Overwhelm | Notable | 1 | 35% more damage, 25% more mana cost |  |  | Lingering | 2 |  |  |
| Corruption | Plague | 1 | Efficiency | Menor | 3 | 8% less mana cost | 16% less mana cost | 25% less mana cost |  |  |  |  |
| Corruption | Plague | 2 | Virulence | Menor | 2 | Spreads to 1 enemy when its bearer dies | Spreads to 2 enemies when its bearer dies |  | Efficiency | 2 |  |  |
| Corruption | Plague | 3 | Pandemic | Notable | 1 | Spreads to 3 enemies, and on from them |  |  | Virulence | 2 |  |  |
| Corruption | Reach | 1 | Haste | Menor | 3 | 5% faster to use | 10% faster to use | 15% faster to use |  |  |  |  |
| Corruption | Reach | 2 | Many Hosts | Menor | 2 | Marks 1 more enemy near the first | Marks 2 more enemies near the first |  | Haste | 2 |  |  |
| Corruption | Reach | 3 | Blight Field | Notable | 1 | Marks every enemy in a wide circle, 20% less damage over time |  |  | Many Hosts | 2 |  |  |
| Curse of Elements | Weakness | 1 | Weakness | Menor | 3 | Resistances drop 5 more | Resistances drop 10 more | Resistances drop 15 more |  |  |  |  |
| Curse of Elements | Weakness | 2 | Lingering | Menor | 3 | 15% longer duration | 30% longer duration | 50% longer duration | Weakness | 2 |  |  |
| Curse of Elements | Weakness | 3 | Exposure | Notable | 1 | Cursed enemies also take 15% more damage of every kind |  |  | Lingering | 2 |  |  |
| Curse of Elements | Reach | 1 | Reach | Menor | 3 | 10% more area | 20% more area | 30% more area |  |  |  |  |
| Curse of Elements | Reach | 2 | Efficiency | Menor | 3 | 8% less mana cost | 16% less mana cost | 25% less mana cost | Reach | 2 |  |  |
| Curse of Elements | Reach | 3 | Contagion | Notable | 1 | Spreads to 2 enemies when its bearer dies |  |  | Efficiency | 2 |  |  |
| Curse of Elements | Ruin | 1 | Patience | Menor | 3 | 10% shorter cooldown | 20% shorter cooldown | 30% shorter cooldown |  |  |  |  |
| Curse of Elements | Ruin | 2 | Haste | Menor | 3 | 5% faster to use | 10% faster to use | 15% faster to use | Patience | 2 |  |  |
| Curse of Elements | Ruin | 3 | Ruin | Notable | 1 | Cursed enemies burst when they die, for 10% of their maximum life |  |  | Haste | 2 |  |  |
| Curse of Time | Stillness | 1 | Drag | Menor | 3 | 5% stronger slow | 10% stronger slow | 15% stronger slow |  |  |  |  |
| Curse of Time | Stillness | 2 | Lingering | Menor | 3 | 15% longer duration | 30% longer duration | 50% longer duration | Drag | 2 |  |  |
| Curse of Time | Stillness | 3 | Stasis | Notable | 1 | Slow +20% more, and the curse is 20% shorter |  |  | Lingering | 2 |  |  |
| Curse of Time | Reach | 1 | Reach | Menor | 3 | 10% more area | 20% more area | 30% more area |  |  |  |  |
| Curse of Time | Reach | 2 | Efficiency | Menor | 3 | 8% less mana cost | 16% less mana cost | 25% less mana cost | Reach | 2 |  |  |
| Curse of Time | Reach | 3 | Ripple | Notable | 1 | Spreads to 2 enemies when its bearer dies |  |  | Efficiency | 2 |  |  |
| Curse of Time | Echo | 1 | Patience | Menor | 3 | 10% shorter cooldown | 20% shorter cooldown | 30% shorter cooldown |  |  |  |  |
| Curse of Time | Echo | 2 | Haste | Menor | 3 | 5% faster to use | 10% faster to use | 15% faster to use | Patience | 2 |  |  |
| Curse of Time | Echo | 3 | Frailty | Notable | 1 | Slowed enemies also take 10% more damage |  |  | Haste | 2 |  |  |
## Sheet: Reglas
| Regla | Valor actual | Notas / cambios que quieres |
| --- | --- | --- |
| Nivel máximo de una habilidad | 20 |  |
| Puntos para el árbol | 1 por cada nivel por encima de 1 (19 a nivel 20) |  |
| Coste en oro de subir de nivel L a L+1 | 30 × 1,45^(L−1), redondeado a 5: nivel 2 = 30, nivel 11 ≈ 1.600, nivel 20 ≈ 24.000; total 1→20 ≈ 77.500 |  |
| Devolver un punto (clic derecho) | 10 oro × nivel de la habilidad |  |
| Requisito para avanzar en una rama | 2 puntos en la mejora anterior |  |
| Grupo excluyente | Solo se puede coger una mejora de cada grupo |  |
| Daño por nivel de habilidad | +12% por nivel |  |
| Coste de maná por nivel de habilidad | +2% por nivel |  |
| Regeneración de maná | 1,5/s base + 0,05 por Inteligencia + 0,15 por nivel de personaje |  |
| Habilidad repetida que cae | Se convierte en oro (un tercio del siguiente nivel, mínimo 25) |  |