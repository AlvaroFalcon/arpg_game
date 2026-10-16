# Performance

Status: **second pass done (mass deaths: all four proposals implemented)**. This doc records frame-time problems
we measured, what caused them, what we changed, and the budgets we keep. Every
number here comes from a benchmark you can re-run. Add to it when a new problem
gets measured.

## Mass deaths ("matar muchos enemigos que explotan")

Owner report (2026-10-15): *"al matar muchos enemigos a la vez que explotan, da
una bajada importante de frames"* (killing a lot of exploding enemies at once
gives a big frame drop).

### What explodes on death

- **Volatile** monsters. This is the reality modifier "A quarter of monsters
  explode on death", plus the `volatile` mod on magic and rare monsters. Each
  death spawns an `AreaBurst` (r 90, 1.0 s telegraph). When a pack dies
  together, all of the blasts land on the **same frame** one second later
  (before the second pass, which staggers and merges them).
- **Volatile imps** (`ExploderEnemy`). Imps that prime together also detonate
  together. Their blast kills the imp itself.
- **Player on-kill explosions.** These are Chain Reaction (a Rift keystone or a
  character keystone) and the Warrior's Decay ascendancy (25 %). They are delay-0
  bursts that hit monsters, so they can cascade. A delay-0 burst detonates on its
  first physics step, so in the benchmark **a whole 60-monster cascade resolved
  within two frames** (before the second pass: one hop per frame now).
- Enemy blasts (volatile, imp) only hit the player. They never chain through
  monsters.

No death spawns lights, GPU particles or physics areas of its own. The cost is
everything else that runs per death:

| Per death | Where |
|---|---|
| damage number (a `DamageNumber` scene instance: Node2D + Label) | `Enemy.OnDamaged` |
| loot, XP, sfx, Chain Reaction check | `RealityController.OnMonsterKilled` / `RunState.OnEnemyKilled` |
| voxel crumble (`VoxelDebris`) | `VoxelStage.OnEnemyKilled` (voxel view only) |
| `AreaBurst` (volatile), `DeathPuff`, `QueueFree` of the monster (~8 nodes) | `Enemy.OnDied` |
| a floor puff + up to 4 wall-chip puffs when a blast of r ≥ 55 lands | `HdView.OnImpact` → `VoxelStage.Impact` |

### Benchmark

`scenes/tools/ExplosionBench.tscn` (`scripts/tools/ExplosionBench.cs`) loads a
reality (fixed seed) or the Survival Rift and spawns N monsters in a dense disc
on the player. It runs two warm-up rounds of the same kills first, so JIT and
first-time loads are out of the way, as they would be mid-reality. Then it kills
the whole disc in one frame and records the wall time of every frame for the
next 2.5 s. It also records the time spent inside the kill call itself, node,
draw-call and object counts, and GC counts. Run it with `--fixed-fps 60`: each
frame is then exactly one simulation step and is not throttled, so its wall time
is its cost.

```bash
# headless (CPU / simulation)
"$GODOT_BIN" --headless --path . --fixed-fps 60 res://scenes/tools/ExplosionBench.tscn -- --kind=volatile --count=60
# voxel view (Forward+ on lavapipe under Xvfb; only relative numbers mean anything)
VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run -a -s "-screen 0 1280x720x24" \
  "$GODOT_BIN" --path . --rendering-driver vulkan --fixed-fps 60 res://scenes/tools/ExplosionBench.tscn -- --kind=volatile --count=60 --warmup=1
```

Options are `--mode=reality|rift`, `--kind=volatile|plain|chain|exploder|free`,
`--count`, `--enemy`, `--seconds`, `--seed`, `--warmup` and `--shot=<prefix>`
(windowed only: PNGs of the crumble and of the blasts landing).

- `chain` gives the player Chain Reaction, leaves every monster at 1 life and
  kills the centre one.
- `free` is a control: it removes the monsters without killing them, so it
  measures node teardown alone.

Lines go to `user://bench/explosion.txt`, ending in `RESULT: PASS`.

The kill frame is reported as **#0 + #1**. The kill runs inside frame #0's
process step, and the monsters are torn down at the end of that frame, which the
bench sees in #1.

### Results (60 Slimes, median of 3 runs, ms)

Headless: simulation only, no voxel view.

| Scenario | Kill call before → after | Kill frame (#0+#1) before → after | Worst frame in 2.5 s before → after |
|---|---|---|---|
| Reality, volatile | 22.0 → **12.7** | 36.9 → **26.9** | 25.6 → **14.8** |
| Reality, plain deaths | 19.8 → **10.7** | 32.6 → **20.1** | 22.7 → **15.1** |
| Reality, chain reaction | 3.1 → 3.0 | 23.1 → **19.9** | 35.5 → **24.7** |
| Reality, imps detonating | 0.6 → 0.7 | 12.3 → 14.7 | 22.6 → 23.4 (noise) |
| Rift, volatile | 21.7 → **16.6** | 37.5 → **30.6** | 25.9 → **19.9** |
| Rift, chain reaction | 4.7 → 3.6 | 34.0 → **21.3** | 29.4 → **17.9** |
| Control: 60 monsters freed, no death | — | ~7 | — |

Voxel view: Forward+ on lavapipe (a software GPU), reality, volatile, 1 run.

| | Before | After |
|---|---|---|
| Kill call (CPU) | **123.6 ms** | **18.0 ms** |
| Draw calls, idle → peak in the 2.5 s after | 160 → 343 | 160 → 164 |
| Objects in frame, idle → peak | 1037 → 1220 | 1037 → 1041 |

Lavapipe takes about 620 ms to render one frame of a cavern, so its frame times
hide everything else. Read only the CPU time and the counts from that table. On
a real GPU, the removed readbacks are pipeline stalls (see cause 1), so the gain
there should be larger than the CPU number suggests.

### Causes (measured with temporary Stopwatch probes, 60 deaths)

1. **Voxel crumbles read the mesh back from the GPU on every death.**
   `VoxelDebris.Burst` called `Mesh.SurfaceGetArrays` on the dying monster's
   voxel model each time. In Godot 4.1's RenderingDevice backend that copies the
   vertex buffer back from the GPU and waits for it.
   - It cost ~1.5 ms of CPU per death on lavapipe (92 ms of the 124 ms kill
     call). On a hardware GPU each call flushes the frame's work and stalls.
   - Each death also added its own `MultiMeshInstance3D` casting shadows: +183
     draw calls and objects for 1.6 s. Every cube was uploaded with its own
     `SetInstanceTransform` call each frame (16 calls per crumble per frame, 220
     for a boss).
2. **Damage numbers.** Each one was a fresh scene instance (Node2D + Label with
   a theme override): 0.2–0.3 ms each, 12–19 ms per 60 deaths. This was the
   largest item headless.
3. **Loot in realities.** An item drop costs ~1 ms (instance, affix roll, icon,
   ground label) and a pickup 0.1–0.3 ms. A magic pack drops 3–8 items: 5–10 ms.
   Fixed in the second pass (loot spawn queue).
4. **Node teardown.** Freeing 60 monsters of ~8 nodes each costs ~7 ms (the
   `free` control). This is inherent to freeing them. *Not changed*: see
   rejected options.
5. **Blasts landing together.** A volatile pack's 60 bursts landed on one frame.
   Each one kicked up a floor puff and up to 4 wall-chip puffs, which is up to
   300 new MultiMesh nodes in one frame in the voxel view.
6. **Cascades in one or two frames.** A Chain Reaction cascade puts all of its
   deaths on one or two physics frames (see above).
7. Small: `BuildQuery.Any` (asked on every kill for Chain Reaction) allocated a
   Godot array plus LINQ, ~1.5 ms per 60 kills. `DeathPuff` is ~2 ms per 60
   nodes. The volatile spawn is ~3 ms per 60.

### Fixes, first pass (none change gameplay)

- **Cube shapes cached per mesh** (`VoxelDebris.CubesOf`). A model frame is read
  back once per session, never again.
- **One MultiMesh per frame of crumbles** (`VoxelStage.OnEnemyKilled` →
  `FlushCrumbles` → `VoxelDebris.BurstMany`).
  - A frame's ordinary deaths are gathered and flushed with `CallDeferred` at
    the end of the same frame, into one node and one draw.
  - Each monster keeps exactly the cubes, count (16), colours, motion and
    shadows it had.
  - Bosses keep their own 220-cube burst.
- **Debris uploads the whole buffer in one call** (`RenderingServer.MultimeshSetBuffer`)
  instead of one call per cube.
- **Impact budget** (`VoxelStage.Impact`). At most 6 floor/wall puffs start per
  frame, and a puff is skipped when one already started this frame within half
  its radius. This is purely cosmetic: overlapping blasts in one spot already
  looked like one.
- **Damage numbers are pooled** (`DamageNumber.Spawn`). A finished number hides
  and waits under its scene. It is reused only under the same parent, and at
  most 128 are kept. `Enemy.SpawnDamageNumber` now calls the pool.
- **`BuildQuery.Any` walks the children without allocating.**

### Budgets

- A death's own synchronous cost (handlers and spawns) should stay ≤ 0.25 ms
  headless. It is ~0.2 ms after the fixes, ~0.35 ms before (a 60-pack's kill
  call was 21 ms before and is 11–13 ms after).
- Cosmetic 3D effects started in one frame: ≤ 6 impact puffs and 1 crumble node.
- Loot spawned in one frame: ≤ 2 items or 8 pickups (`LootQueue.FrameBudget`).
- On-kill explosions: one hop per frame. Volatile blasts: one hit per target per
  0.25 s, and one telegraph per cluster.
- Anything that can happen per death must not touch the GPU synchronously
  (no `SurfaceGetArrays`, `Texture.GetImage`, `Viewport.GetTexture().GetImage()`).

### Rejected

- **Turning off shadows on crumbles, or using fewer cubes.** It would look
  different, and batching already removed the extra casters.
- **GPUParticles3D for debris.** It would be a different look and new shader
  compiles, and the batched MultiMesh is already one draw.
- **A cap on damage numbers alive.** It would hide feedback, and pooling already
  made them cheap.
- **Deferring monster teardown** (hide now, free a few per frame). Many systems
  rely on a dead monster being freed (`IsInstanceValid` checks, packs, targets,
  voxel actors). That risk is too high for ~7 ms per 60.
- **Threads.** The cost is spread over Godot node APIs that must run on the main
  thread.

### Second pass: the four proposals (approved by the owner 2026-10-15)

The owner approved all four ("sí, dale con todo para que vaya mejor"). Items 1
and 2 change what the player sees and, for volatile blasts, how much damage they
take. That is intended and is written into the design docs (endgame.md,
progression.md, run.md).

1. **One chain hop per frame** (`AreaBurst.SpawnOnKill`). Chain Reaction (realities
   and the Rift) and the Warrior's Decay spawn a burst that may not detonate until
   the physics frame after the death that spawned it. A cascade spreads as a
   ripple, ~16 ms per hop. Damage, radius and chance are unchanged.
   - Measured: the bench's 60-monster cascade went from two heavy frames (#1, #2)
     to two lighter ones (#2, #3) plus a tail. The disc is dense (14 px apart,
     70 px blasts), so it is only 2–3 hops deep. One hop kills ~25 monsters, and a
     hop's cost is those deaths. A per-frame cap on chain *deaths* would spread it
     further, but it would also delay kills and drops, so it was not done.
2. **Volatile blasts staggered and merged** (`Enemy.OnDied` → `AreaBurst.SpawnMerged`,
   group `"volatile"`).
   - The delay is 1.0 s ± 0.1 s (uniform), so a pack's blasts land over ~12 frames.
   - **Hit window.** A target hit by a volatile blast ignores further volatile
     blasts for 0.25 s (`AreaBurst.GroupHitWindow`). The window is counted in
     physics frames of the current delta, so it holds at any time scale. The
     first blast to land hits, and the others still flash. A pack killed together
     is one hit. Packs killed 0.25 s or more apart still hit separately.
   - Why a window rather than "merge into one blast with the max damage": the
     window keeps every blast's own area and damage, and it needs no cluster
     bookkeeping for damage. Within a pack the damage is the same
     (15 + 3 × monster level), so "first" and "max" are equal in practice.
   - Imps (`ExploderEnemy`) are not in the group. Their blast is their attack,
     and they prime one by one as they reach the player.
3. **Loot spawn queue** (`Items.LootQueue`, owned by `RealityController.Loot` and
   `RunState.Loot`, pumped at the start of each one's `_PhysicsProcess`).
   - Drops are rolled on the kill frame (same chances, positions and amounts) and
     spawned in order under a budget. An item costs 1 and a pickup 0.25, with a
     budget of 2 per frame (2 items or 8 pickups a frame).
   - Queued: reality monster drops (items, gems, shards, gold, crafting orbs, life
     orbs) and Rift kill drops (experience gems, life, gold, orbs, gear).
   - Not queued:
     - boss rewards in realities (one death, and tests and the tutorial expect
       them at once);
     - the tutorial's gift;
     - Rift power-ups (Cataclysm, Lodestone), whose "one on the floor" check must
       see the real floor.
   - Nothing is lost while the scene lives. If the scene is freed within a few
     frames of a kill, the queued drops go with it, as anything on the floor would.
   - `RunSmokeTest` waits 2 frames before checking that Cataclysm's victims drop
     experience.
4. **One telegraph per cluster** (`AreaBurst.SpawnMerged`).
   - A volatile blast that spawns within the radius (90 px) of an open cluster's
     first blast (the leader) joins that cluster.
   - The leader draws one outline that covers every member and fills until the
     last member lands. The outline is centred on the leader, and its radius
     reaches the farthest member's edge.
   - Members draw only their own impact flash.
   - The 60-pack shows ~5 outlines instead of 60 discs
     (`docs/devlog/screenshots/perf-volatile-merged-telegraph.png`).
   - Cosmetic only: the outline slightly overstates the union of the real areas.

`RunSmokeTest` checks item 2's gameplay: 8 volatile Slimes dying on the player
give 8 blasts, 1 hit, landing over several distinct frames.

#### Results, second pass (60 Slimes, headless, median of 3 runs, ms)

- **Doc** is the first pass's *after* column above.
- **Re-run** is the previous commit measured again in the same session as the
  new numbers, back to back. This machine drifts by several ms between sessions,
  so the re-run is the fair comparison.

| Scenario | Kill call: doc / re-run → now | Kill frame #0+#1: doc / re-run → now | Worst frame in 2.5 s: doc / re-run → now |
|---|---|---|---|
| Reality, volatile | 12.7 / 18.9 → **11.1** | 26.9 / 35.6 → **30.4** | 14.8 / 20.9 → **17.9** |
| Reality, plain deaths | 10.7 / 15.3 → **9.7** | 20.1 / 28.5 → **21.0** | 15.1 / 17.3 → **10.6** |
| Reality, chain reaction | 3.0 / 3.9 → 3.6 | 19.9 / 28.2 → **6.7** | 24.7 / 24.9 → **15.8** |
| Reality, imps detonating | 0.7 / 0.6 → 0.7 | 14.7 / 17.3 → 14.2 | 23.4 / 21.9 → 20.0 (not targeted, noise) |
| Rift, volatile | 16.6 / 15.4 → **9.4** | 30.6 / 29.1 → **20.2** | 19.9 / 27.1 → **17.7** |
| Rift, chain reaction | 3.6 / 4.1 → 4.5 | 21.3 / 24.1 → **10.4** | 17.9 / 28.8 → **16.9** |

- **Chain.** The kill frame is now cheap because the cascade starts one frame
  later. The heavy frames are #2 and #3 (~15 + 13 ms) instead of #1 and #2
  (~25 + 18 ms).
- **Volatile.** The blasts landing were already cheap headless. The first pass
  capped the voxel view's impact puffs, and staggering now spreads them too.
- **Loot.** The kill call dropped 5–6 ms wherever loot or experience drops. That
  is the queue.
- **Unexplained.** Spikes later in the Rift runs (#8–#33, ~17 ms) show up both
  before and after. They are not the blasts (those land around #60) and are not
  attributed yet.

### Proposals

None open: the four from the first pass are implemented above.

Possible next steps, measured but not proposed yet:

- A per-frame budget on chain-reaction *deaths* (see item 1).
- `RunState.SpawnExperience` calls `GetNodesInGroup` (which allocates an array)
  for every gem, to check the 300-gem cap. A counter would remove that.

## Co-op (Survival Rift, 4 players) — measured 2026-10-16

Independent check of the numbers in `multiplayer.md` -> "Performance", plus
the first fixes. One 4-core cloud container, headless.

### Method

- **Party bench:** `scenes/tools/CoopBench.tscn` (host plus 3 client processes,
  clock jumped to 350 s, 30 s fill, 20 s measured, then a Cataclysm). It now
  also logs the host's **frame wall time**, **physics steps per frame**, a
  **probe of every physics step's scripts** (one node at priority
  `int.MinValue`, one at `int.MaxValue`) and managed allocations and GCs on
  every process.
- **Pinned run:** host on cores 0–1 (`taskset`), clients re-pinned to cores
  2–3. The host gets its own CPU and the clients can't steal it.
- **Solo bench (offline, no session):** `scenes/tools/RiftBench.tscn`,
  `--fixed-fps 60` (one step per frame, unthrottled, so a frame's wall time is
  its cost). Clock 350 s, 2400 fill frames, 1200 measured, then a Cataclysm. The
  player auto-casts, and the monsters can't die, so the count sits at the cap
  (275) and runs can be compared. It ran on base `e54b12f` (git worktree) and on
  HEAD. Median of 3.
- **Attribution:** `dotnet-trace collect --format speedscope` on the host and
  on one client during the steady window, then summed inclusive time per method.

### The earlier numbers were misread

- `Performance.Monitor.TimePhysicsProcess` (and `TimeProcess`) is **the worst
  step of the last second**, refreshed once a second, not the step just run.
  "82 ms avg" was the average of per-second maxima.
- The real problem was bigger. The host ran **8 physics steps every frame**
  (the `max_physics_steps_per_frame` cap), with 291 ms frames. That is a death
  spiral, so the Rift ran at about 45 % speed. The 20 Hz snapshot clock counts
  game time, so snapshots went out at about 9 Hz. That is why bandwidth read
  ~40 KB/s.

### Cause

Per-frame scans over every monster, multiplied by four players. This was not
the replication code.

| Hot spot (host, 4 players) | Share of main thread |
|---|---|
| `PlayerSkillInput.AutoAimTarget` → `NearestHostile`: every player, every physics frame, a `GetNodeOrNull<Health>("Health")` per monster (a string → `NodePath` each time) | 40 % |
| `Enemy.FindTarget` → `NearestTargetablePlayer`: `GetNodesInGroup` per call. When every player is warded (cards open) it returns null, so **every monster re-scans every frame** | 21 % |
| `Enemy.FindTarget` decoy check: `GetNodesInGroup("decoys")` per monster per frame, plus a string → `StringName` | 8 % |
| `OrbitBlades`: `HostilesInRadius` (full scan plus a new list) **per blade** per frame, also on clients, which can't deal damage | 16 % (after the above) |
| `TargetFrame.PickTarget`: LINQ over the group, enumerated twice | 4.5 % host, 13 % client |
| `RiftHost` (snapshots, events) | 4–7 % |

### Fixes (no gameplay change; the results are the same)

- `CombatQuery`: `HealthOf()` reads the cached `Enemy.Health`/`Player.Health`.
  The group and path names are cached `StringName`/`NodePath`. The player list
  for `NearestTargetablePlayer` and `LocalPlayer` is read once per frame, and
  warded/dead/valid are still checked on every call.
- `Enemy.FindTarget`: `SceneTree.HasGroup("decoys")` comes before listing the
  group (an empty group doesn't exist).
- `PlayerSkillInput.SurvivalCast`: the auto-aim target is looked up only when a
  slot actually fires. Before, it was looked up every frame.
- `OrbitBlades`: one scan per frame within `Radius + HitRadius`, then a
  distance test per blade on that short list. It is the same set by the
  triangle inequality. Clients skip the scan.
- `TargetFrame.PickTarget`: one pass with no LINQ, and the same picks (first
  minimum).

### Results

| | Before | After |
|---|---|---|
| Host frame wall, 4p shared CPU | 291 ms avg, p95 447, max 1393 | **25.7 ms**, p95 61, max 148 |
| Host physics steps per frame | 8.00 (death spiral) | 1.53 |
| Host physics scripts per step | 32.5 ms, p95 46 | **10.3 ms**, p95 15 |
| Host, pinned to 2 cores (clients isolated) | — | frame 22.9 ms, scripts 10.2 ms/step, 1.37 steps/frame |
| Solo through a session (`--clients=0`), pinned | — | frame 16.8 ms, scripts 5.5 ms/step, 1.01 steps/frame (257 monsters) |
| `RiftHost` step | 0.9 ms, p95 3.6 | 0.5 ms, p95 1.8 |
| Snapshots per client, real time | ~33 KB/s (at ~9 Hz) | **~65 KB/s** wire (0.52 Mbit/s), p95 72 |
| Host upload, 3 clients | ~0.8 Mbit/s | **~1.6 Mbit/s** |
| Host managed allocations | 19 MB/s | 6.3 MB/s (no GC in 20 s) |
| Client managed allocations | 3.4 MB/s | 3.7 MB/s (more snapshots now) |
| Cataclysm (300 at once): replication step | ≤0.6 ms | ≤1.2 ms; wire 30 KB/s/client next 2 s |
| **Offline solo** (RiftBench, 275 monsters) base → HEAD → fixed | 20.5 → 18.0 ms avg | **15.2 ms** avg, p95 27.6 → 24.9 → 20.2, p99 36 → 31 → 25 |
| Offline managed allocations base → HEAD → fixed | 297 → 196 KB/frame | **47 KB/frame** |

- **Offline didn't get slower with co-op.** HEAD is already faster than base,
  because the old `FindTarget` built a `NodePath` per monster per frame. The
  fixes take it further.
- **The host's extra cost is co-op gameplay, not networking.** With its own two
  cores, the host's scripts cost 10.2 ms per step against 5.5 solo. That covers
  1.5× the monsters, four players' skills, and four sets of instanced drops
  (~400 pickups on the floor against ~26). Replication is 0.5 ms of that.
  Sharing the CPU with the clients adds frame-time variance (p95 61 against 50),
  not much average.
- **Bandwidth is inside a home budget** (1 Mbit/s down per client, 3 Mbit/s up
  for the host): 52 % and 53 % of it. The snapshot is 8 bytes per monster.
  Nearly every monster is within `NearRadius` (1400 px), so nearly all go at
  20 Hz.

### Budgets

- Host, 4 players, late wave: ≤16.7 ms per physics step on a desktop
  (1 step per frame). Here it is ~1.4 steps per frame on 2 cloud cores, so it
  is not met yet.
- Replication (`RiftHost`): ≤1 ms avg, ≤3 ms p95 per step.
- Per client: ≤0.5 Mbit/s down (50 % of a home budget). Host: ≤2 Mbit/s up for
  3 clients.

### Proposals (not done)

1. **Bandwidth, if it needs margin** (in `scripts/Net`, owner: multiplayer):
   (a) `NearRadius` from the client's reported view rectangle plus a margin
   (~950 px for 1280×720), instead of 1400 px. Off-screen monsters would go at
   5 Hz. (b) Send life and flags only when they change, with a bit in the id.
   Most monsters' life doesn't change between snapshots. That saves ~25 %.
   (c) 12-bit positions relative to the receiver. Together, roughly half.
2. **Remote players' drops on the host:** ~300 hidden `Pickup` nodes with areas
   and `_PhysicsProcess` (6–9 % of the host). The host could keep them as data
   and test the owner's distance in one loop.
3. **Monster AI scans** (`Enemy._PhysicsProcess`, still ~28 % of the host)
   would want a spatial grid or a shared per-frame monster list for
   `NearestHostile`/`HostilesInRadius`. Profile again on a desktop first.
4. **Design question for the owner:** when every player is warded, every
   monster still asks for a target every frame. That is cheap now (a cached
   list of up to 4 players). Holding "no target" until the 0.4–0.7 s retarget
   timer runs out would make it free, but monsters would then find a player
   that much later. That is a gameplay change, so it was not done.
