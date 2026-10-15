# Performance

Status: **first pass done (mass deaths)**. This doc records frame-time problems
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
  together, all of the blasts land on the **same frame** one second later.
- **Volatile imps** (`ExploderEnemy`). Imps that prime together also detonate
  together. Their blast kills the imp itself.
- **Player on-kill explosions.** These are Chain Reaction (a Rift keystone or a
  character keystone) and the Warrior's Decay ascendancy (25 %). They are delay-0
  bursts that hit monsters, so they can cascade. A delay-0 burst detonates on its
  first physics step, so in the benchmark **a whole 60-monster cascade resolved
  within two frames**.
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
   *Not changed*: see proposals.
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

### Fixes (none change gameplay)

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

### Proposals (owner decision needed: they change gameplay or touch shared code)

1. **One chain hop per frame.** On-kill explosions (Chain Reaction, Decay)
   would detonate the frame after the death that spawned them, not in the same
   physics step. A cascade then spreads over several frames and reads as a
   ripple. The timing changes by ~16 ms per hop.
2. **Stagger volatile blasts.** Give each blast a delay of 1.0 s ± 0.1 s, so a
   pack's 60 detonations spread over ~12 frames.
   - Gameplay note: today 60 overlapping blasts are 60 separate hits on the
     player in one frame, which is effectively instant death. Merging
     overlapping volatile blasts (or a per-frame cap on hits from them) would be
     both fairer and cheaper.
3. **Loot spawn queue.** Spawn drops (items, orbs, gold) at most ~2 items per
   frame from a queue, a few frames late. No numbers change, but it edits
   `RealityController`/`RunState`, which were being worked on in parallel, so it
   is left for a coordinated change. It is worth ~5–10 ms on a magic pack's
   death frame.
4. **One telegraph per cluster.** 60 volatile telegraphs draw 60 overlapping
   translucent discs for a second. That overdraw is invisible on lavapipe's
   numbers but real on low-end GPUs. Drawing one merged outline would change
   what the player reads.
