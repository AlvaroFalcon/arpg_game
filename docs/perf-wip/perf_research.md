# Performance research: the owner's 2D checklist against ARPG Game (research phase)

Date: 2026-10-24. Repo HEAD `1600f27`, Godot 4.1.1 mono, .NET 6. **Nothing in the repo was changed.**
Everything was measured on a copy at
`/tmp/claude-0/-home-user/01f50d60-7603-5568-9cde-d97b2c383e48/scratchpad/proj`. The copy has
temporary probes and `PERF_EXP` experiment switches (`scripts/tools/Perf.cs`, `scripts/tools/PerfBench.cs`,
`scenes/tools/PerfBench.tscn`, plus small edits in Enemy, VoxelActor, VoxelStage, RarityPlate, RealityHud,
HdView, Pickup and the per-frame scripts). It is scratch material, not a patch to merge. Runner scripts:
`run.sh`, `series.sh`, `runwin.sh` and `winseries.sh` in the same scratchpad. Raw logs are in `scratchpad/logs/`.

## 0. TL;DR

1. **Headless, the frame is mostly monsters' `MoveAndSlide`.** It takes 5.2–5.6 ms of a ~14 ms Rift frame
   with 374 monsters, about 14 µs per monster. About 75 % of that is monster-vs-monster body collision (the
   enemy layer is in the enemy mask).
2. **The voxel view adds CPU cost that no headless bench sees.** `VoxelActor._Process` costs 6.4 µs per actor
   and allocates about 614 B per actor per frame (`VoxelLibrary.MeshFor` builds strings every frame). With
   374 actors that is +2.4 ms/frame and **310 KB/frame of garbage**, against 80 KB/frame without the view.
3. **The GPU draws the whole map every frame.** Each terrain piece is one MultiMesh covering the entire level,
   and Godot culls a MultiMesh as a single AABB. The Rift submits **7.46 M primitives per frame** and a cavern
   5.5 M. Splitting the floor into 12 m chunks cut that to 1.24 M (-83 %) and 0.57 M (-90 %). On lavapipe
   (a software GPU) the frame went 638 → 303 ms and 529 → 272 ms.
4. **The long spikes in realities (0.25–2.6 s) are .NET gen2 GCs.** They are amplified by this container,
   whose reported 504 MB L3 cache gives a huge gen0 budget. With a desktop-like 16 MB gen0, the same scene
   pauses 11–31 ms per gen0 GC and 43–50 ms per gen1 GC every few seconds. Cutting allocations is the real
   fix. Two sources are cheap to remove:
   - the VoxelActor strings (item 2);
   - an enumerator boxed in `Stat.ComputeFormula`, about 45 KB/frame.
5. **Small, near-free wins:**
   - Three per-monster child ticks (Health, Ailments, ActorStats `_PhysicsProcess`) cost 1.7–1.9 ms per step.
   - `TargetFrame` and `RunDirector` group scans cost ~0.8 ms per frame.
   - The directional shadow uses 4 PSSM splits under an orthographic camera: draw calls fall 60 % with
     `Orthogonal` (999 → 403).

## 1. Method and caveats

- **Headless benches** (simulation only) ran with `--fixed-fps 60`, so each frame is one unthrottled step
  and its wall time is its cost. The benches were:
  - `RiftBench`, unmodified: clock 350 s, 1800 fill frames, 1200 measured frames, then a Cataclysm.
  - `PerfBench` in the copy, the same Rift setup plus:
    - a step probe (the physics-script time per step);
    - Stopwatch probes with allocated bytes per probe;
    - render monitors;
    - a "reality" mode: Bone Warrens, tier 10, seed 7, 245–291 monsters, which runs a "steady" phase (packs
      asleep) and then an "awake" phase (every pack woken and flooding the player through the flow field).
  - `ExplosionBench`.
  - `AutoplayBot --mode=reality --class=warrior --tier=10`.
- **Windowed runs** used Forward+ on lavapipe under Xvfb at 1280×720, about 0.3–0.7 s per frame. Only the
  counts (draw calls, objects, primitives, memory) and *relative* frame times mean anything. Lavapipe also
  takes CPU from the main thread, so CPU µs measured under Xvfb are inflated.
- **"forcevoxel"** is a copy-only switch that builds the voxel stage under the headless dummy renderer. It
  measures the voxel view's *CPU* cost (VoxelActor/VoxelStage scripts, RenderingServer calls) without
  lavapipe stealing cores.
- **Attribution:**
  - `dotnet-trace` (sample profiler, speedscope) on RiftBench;
  - Stopwatch probes;
  - `GC.GetGCMemoryInfo` (pause durations, generation, heap) logged on every GC.
- **Noise:**
  - The container has 4 vCPUs. The same bench varies by ±1 ms (RiftBench avg 13.3 / 14.0 / 14.2 ms on three
    runs).
  - The monster count also varies from run to run (326–374) despite the fixed seed. **Compare µs-per-call
    figures, not raw totals, when the counts differ.**
  - VBCSCompiler was killed before every measurement.
- **What headless cannot measure:** GPU time, canvas (2D) batching, the voxel view (except via
  forcevoxel), and real-GPU stalls. **CoopBench was not re-run.** Its 2026-10-16 and 2026-10-19 numbers in
  `docs/design/performance.md` stand. The hot spots below (crowd physics, VoxelActor, allocations) apply to
  the host as much as to solo play.

## 2. Baseline

### 2.1 Headless simulation

| Scenario | Monsters | Frame avg / p95 / p99 / max (ms) | Physics scripts per step (ms) | Alloc / frame | GCs in window | Nodes / objects |
|---|---|---|---|---|---|---|
| RiftBench (unmodified), 3 runs | 365–374 | 13.3–14.2 / 15.9–17.1 / 19.8–22.1 / 34–62 | — | 75–81 KB | 0 | 3273–3585 nodes |
| PerfBench Rift | 374 | 14.1–15.0 / 16.4–16.6 / 22 / 32–38 | 9.7–10.4 (p95 11.4) | 80 KB | 0 | 3283 / 9480 |
| PerfBench reality, steady (packs asleep) | 245 | 5.1 / 5.4 / 5.8 / 11 | 2.95 | 42 KB | 0 | 2477 / 8573 |
| PerfBench reality, awake (swarm on player) | 245 | 7.8 / 8.8 / 11.3 / **274** | 4.96 (max 268) | 97 KB | **1 gen2** | 2486 / 6034 |
| AutoplayBot reality T10 (3 realities, 8 min each) | 194–219 | 2.1–4.8 avg, p95 4.6–6.1, **max 797 / 942 / 2648** | — | — | — | — |
| AutoplayBot, 2nd session (spike log) | — | 3.4–4.9 avg, max 159 / 466 | — | — | spikes at gen2 GCs | ~2200 |
| Rift Cataclysm (377–416 killed in one frame) | — | first frame **95–118 ms**, next 2.7–6.6 avg | — | — | — | — |
| ExplosionBench reality volatile 60 | 60 | kill frame 15.3, worst after 8.7 | — | — | 0 | — |
| ExplosionBench Rift volatile 60 | 60 | kill frame 16.7, worst 9.9 | — | — | 0 | — |
| ExplosionBench reality chain 60 | 60 | kill frame 4.2, worst 10.7 | — | — | 0 | — |

Monitors in the Rift (374 monsters):
- Physics2D active objects 378, collision pairs **847**, islands 3.
- In the reality, steady: 246 active, 56 pairs. Awake: 246 active, 337 pairs.

### 2.2 Where the headless Rift frame goes (374 monsters, per frame)

| Probe | ms/frame | µs each | Alloc |
|---|---|---|---|
| `Enemy._PhysicsProcess` (all of it) | 6.0–6.4 | 16 | 16 KB (string → StringName in `IsInGroup(string)`) |
| ↳ `MoveAndSlide` | **5.2–5.6** | **13.7–14.9** | 0 |
| ↳ `FindTarget` | 0.16 | 0.4 | 0 |
| Health + Ailments + ActorStats `_PhysicsProcess`: 3 more script callbacks per monster | **1.7–1.9** (from the `notick` experiment) | ~1.6 per monster | **~45 KB** (boxed enumerator, see 4.5) |
| `TargetFrame._Process` (group scan with interop per monster) | 0.44 | — | 0.3 KB |
| `RunDirector._PhysicsProcess` (`GetNodesInGroup` every frame, leash) | 0.36–0.40 | — | 1.5 KB |
| RarityPlate `_Process` (374, normal ones only hide themselves) + header draws | 0.06 + 0.12 | 0.2 | 0 |
| RunHud, player, skill input, area bursts, projectiles (6–8 alive) | < 0.25 together | — | ~4 KB |

The `dotnet-trace` sample profile agrees: `CharacterBody2D.MoveAndSlide` is 67 % of managed-thread samples.
After it come `FindTarget`/`GetTree` (6.5 %), `GetNodesInGroup` enumeration (2.7 %), RarityPlate (3.6 %) and
TargetFrame (2.8 %).

### 2.3 Voxel view, CPU (forcevoxel headless, Rift, 374 monsters)

| | Plain headless | With voxel stage |
|---|---|---|
| Frame avg | ~14 ms | **18.7–18.9 ms** |
| `VoxelActor._Process` | — | **2.4 ms/frame**, 6.4 µs per actor, of which `MeshFor` is 4.0 µs |
| Managed allocations | 80 KB/frame | **310 KB/frame** (VoxelActor alone 234 KB, ~614 B per actor per frame) |

In a windowed cavern (254 actors), `VoxelActor` costs 2.1 ms/frame and 145 KB/frame. `VoxelStage._Process`
costs 30–70 µs. That cost grows with the number of wall materials, since it writes `player_pos` to each one
every frame: 327 µs once the walls were chunked.

### 2.4 Voxel view, render counts (Forward+, lavapipe, 1280×720)

| Scene | Draw calls | Objects in frame | Primitives in frame | Video mem (tex / buffers) | Lavapipe frame |
|---|---|---|---|---|---|
| Rift, 41–69 monsters | 999 (max 1096) | 1375 | **7.46 M** | 120 MB (63 / 18) | 638 ms |
| Cavern T10, packs asleep (254) | 200 | 920 | **5.54 M** | 134 MB (62 / 21) | 529 ms |
| Cavern T10, awake swarm on screen | 634 | 1494 | 5.60 M | 135 MB | 556 ms |

Voxel meshes have one surface per colour:
- a floor tile is ~700 triangles and **10 surfaces**;
- a Slime frame is ~400 triangles and 5 surfaces;
- a Brute frame is 1192 triangles and 7 surfaces.

Godot 4.1 Forward+ **does** auto-instance consecutive identical surfaces. See
`render_forward_clustered.cpp` at 4.1.1-stable: `element_info.repeat`, where "this element is the same as the
previous one, count repeats to draw it using instancing". So 300 Slimes on the same frame are a few instanced
draws, not 1500. The exception is a MeshInstance3D with a `MaterialOverlay` or `Transparency` (rarity tint,
hit flash), which breaks the batch.

## 3. Experiments (measured)

Run on the copy, toggled with `PERF_EXP`. None of them is in the repo.

| Experiment | What it changes | Result | Visible / gameplay change |
|---|---|---|---|
| `slides2` | `MaxSlides` 4 → 2 on monsters | MoveAndSlide 13.7 → **9.0 µs** (-35 %); frame 12.1–13.2 ms | Monsters give up sliding sooner in tight crowds and corners. Small, needs an eye check. |
| `slides1` | `MaxSlides` → 1 | 5.9–6.1 µs (-57 %); frame 11.0–11.4 ms | Monsters stop against walls and each other instead of sliding. Probably noticeable. |
| `nocrowd` | Enemy layer removed from the enemy mask | 2.6–3.2 µs (-78 %); frame 9.3–11.1 ms; pairs 847 → ~20 | Monsters overlap freely. **A design change.** Would need soft separation to keep the crowd readable. |
| `idle` | Skip `MoveAndSlide` when the velocity is zero (sleeping packs) | Reality steady frame 5.11 → **3.78 ms**; physics active 246 → 1 | None: a zero-motion slide only does depenetration, and a sleeping monster has nothing to depenetrate from. Verify that spawn overlap still resolves when they wake. |
| `notick` | Enemy calls Health/Ailments/Stats ticks directly; their own `_PhysicsProcess` turned off | Scripts 9.73 → 7.87–8.06 ms/step (-18 %); frame 14.1 → 12.0–12.3 | None in principle. The order within a frame changes (ticks before AI instead of after the parent). |
| `actorcache` | VoxelActor resolves `MeshFor` only when SpriteFrames/animation/frame change, and skips rewriting the overlay/transparency while plain | VoxelActor 6.4 → **4.2 µs** (-35 %); alloc 310 → **147–153 KB/frame**; frame 18.8 → 14.7–16.1 | None. |
| `plate` | RarityPlate turns its `_Process` off for normal monsters | 0.09 → 0.01 ms/frame | None (normal plates are already hidden). |
| `chunks` | Floor/liquid-bed MultiMeshes split into 12 m chunks (cave rock not split) | Rift: primitives 7.46 M → **1.24 M**, frame 638 → 303 ms; draw calls 999 → 1205. Cavern: 5.54 M → **0.57 M**, 529 → 272 ms; draws 200 → 390 | None. More draw calls, until surfaces are merged (item C4). |
| `shadow_ortho` | Sun `DirectionalShadowMode` PSSM4 → Orthogonal | Rift draws 999 → **403**, objects 1375 → 584, primitives -8 %. Cavern draws 200 → 166 (asleep), 634 → 562 (awake) | Shadow resolution and bias change near the camera. Needs an eye check. Under an orthographic camera at a fixed distance the 4 splits buy little. |
| `noactorshadow` | Actors don't cast shadows | Rift draws 999 → 359 | **The player loses actor shadows.** Not recommended. It only shows where the shadow cost sits. |
| `nodof` | No DOF camera attributes | Lavapipe frame -5 % | Loses the tilt-shift blur. A candidate for a "low" graphics preset only. |
| `gcload` | `GC.Collect()` ×2 right after the reality has loaded | Collect costs 54–63 ms (would sit behind the loading screen); heap 300 MB → 5 MB; the later in-play gen2 pause fell 274 → 130 ms (one of two runs had no in-window spike at all) | None. |
| `DOTNET_GCgen0size=16MB` (env) | Emulates a desktop-sized gen0 budget | GCs every 200–400 frames: gen0 pauses 11–31 ms, gen1 43–50 ms. Spikes of 52–59 ms | — (diagnostic) |
| `DOTNET_gcConcurrent=0` (env) | Blocking gen2 | 277 ms blocking pause (heap 65 MB, 54 MB promoted) | — (diagnostic) |
| `cull2d` | Mirrored 2D sprites moved to visibility layer 20, which the viewport's `CanvasCullMask` excludes | Not measurable on lavapipe: the monster counts differed between runs | None. It removes ~400 alpha-0 canvas items from cull/batch, so it is CPU-side canvas work only. |

## 4. The owner's list, item by item

| Item | Verdict for this game | Where / why |
|---|---|---|
| Sprite atlases | **Already done, and moot in the voxel view.** | Every sprite is an `AtlasTexture` region of one EasyPixel sheet per sprite. In the voxel view the 2D sprites are invisible. The 3D equivalent is **one material per mesh**: voxel meshes have one surface per colour (10 for a floor tile), see C4. |
| Tilemaps instead of objects | **Already done**, but the 3D version needs chunking. | 2D uses TileMaps (hidden in the voxel view). The voxel terrain is one MultiMesh per piece for the **whole map**, so nothing is ever culled (C3). Collision walls are already run-length-merged rectangles in one StaticBody2D (`LayoutBuilder.BuildScene`, `Arena`, `HideoutTown`). |
| Minimise overdraw of stacked transparent sprites | **Mostly not relevant.** | 2D sprites at alpha 0 are still submitted and drawn: `renderer_canvas_cull.cpp` in 4.1.1 only skips an item whose *modulate* (not self-modulate) is below 0.007. That is fill-rate-trivial but costs CPU cull and batch work (`cull2d`, low priority). In 3D, the transparent pass holds only tinted or flashing monsters (overlay material) and debris. The real GPU cost is triangle count, not overdraw. |
| Cull off-screen objects (rendering) | **Partly done automatically; the big gap is ours.** | Godot frustum-culls each MeshInstance3D, so actors are fine. The terrain MultiMeshes (map-wide AABB) and the cave rock (one mesh) defeat culling: 7.46 M / 5.5 M primitives per frame, -83 % / -90 % with chunks. |
| Cull off-screen objects (updates) | **Applicable, modest gain.** | Every VoxelActor and RarityPlate `_Process` runs for off-screen and sleeping monsters, and sleeping monsters still `MoveAndSlide` every frame (`idle`: -1.3 ms in a sleeping reality). The Rift already leashes stragglers back (`RunDirector.Leash`). |
| Object pooling | **Done where it was measured to matter.** Projectiles next, only if a bench shows it. | Damage numbers (column layer, 24 starts per frame) and the loot spawn queue are done. Projectiles (`SkillUser.ExecuteProjectile` → `Instantiate`, ≤ 8 alive in the benches) and pickups are not pooled, and they did not show up in any profile. The GC pressure comes from **per-frame allocations** (VoxelActor strings, boxed enumerators), not instantiate/free churn. |
| GPU texture compression | **Not worth it.** | Textures are 62–63 MB in total. They are pixel art, which VRAM compression (lossy block formats) would smear. The 3D world is vertex-coloured voxels, not textures. The video-memory headroom is fine. |
| Profile first | **Done** (this report). | Tools: RiftBench, ExplosionBench, CoopBench, AutoplayBot frameMs, plus the PerfBench pattern from the copy. That pattern is worth adding to the repo: probes, alloc per probe, GC pause log, render monitors. |
| Simple collision primitives | **Already done.** | Circles for monsters, the player and projectiles. Merged rectangles for walls and hazards. No polygons on moving things. |
| Spatial partitioning | **Physics: already done by Godot. Our own scans: applicable.** | Godot's 2D physics uses a BVH broadphase. Our C# code still does linear scans over the enemy group: `TargetFrame` (0.44 ms/frame), `RunDirector` (0.36–0.40 ms, every frame), `CombatQuery.NearestHostile` / `HostilesInRadius` (per cast, chain, burst and orbit), `RiftMinimap`, `LineStrike`. Each element marshals across interop. A shared monster registry with a uniform grid would serve all of them (C9), and soft crowd separation if the owner wants it (C1b). |
| Stagger heavy AI, throttle distant units | **Thinking is already cheap and staggered. Movement is the cost.** | Retargeting runs on a 0.4–0.7 s timer, the flow field re-floods only when the player changes cell, and Rift stragglers are leashed. `FindTarget` is 0.4 µs. Staggering AI would save little. Throttling *movement* (MoveAndSlide every other frame off-screen) would change how monsters move, which is a gameplay call, so it is not proposed before C1. |

## 5. Beyond the list: Godot 4.1 + C# specifics

1. **`MoveAndSlide` at zero velocity is not free (4.1.1 source).** `_move_and_slide_floating` always runs at
   least one `move_and_collide` with recovery, whatever the motion. In dense crowds each iteration
   depenetrates against several neighbours, up to `max_slides` (default 4) times. Levers, in order of
   safety: skip it when idle, lower `MaxSlides`, then drop monster-vs-monster bodies.
   - Godot's physics BVH already handles the broadphase.
   - Forum reports agree that CharacterBody is the wrong tool for big crowds and that server-side bodies or
     boids-style separation scale much further.
2. **Auto-instancing in Forward+ (4.1.1).** Identical surfaces drawn consecutively are instanced
   automatically, unless the instance uses a mesh instance or a multimesh flag. Consequences:
   - A MultiMesh per monster type is *not* needed for draw calls.
   - Per-instance `MaterialOverlay` and `Transparency` break the batch.
   - Fewer surfaces per mesh multiplies the gain.
3. **MultiMesh is culled as one unit.** The docs say there is no per-instance culling. A Godot maintainer
   recommends one MultiMesh per chunk (e.g. 16×16 tiles), not one per level (godot#76436). Confirmed here:
   -83 % / -90 % primitives.
4. **Directional shadows.** The 4.1 docs rank Orthogonal < 2 splits < 4 splits in cost, and a lower max
   distance means fewer casters. The game uses PSSM4 (the default) with `DirectionalShadowMaxDistance = 60`
   under an *orthographic* camera at 40 m. OmniLights here have no shadows (70 cave/hazard lights plus the
   player light), which is cheap in clustered Forward+, so leave them alone. Positional shadow atlas settings
   don't matter while no omni or spot light casts shadows.
5. **Visibility ranges (HLOD) are not useful here.** They are camera-distance based, and the orthographic
   camera sits at a constant 40 m. Use chunked frustum culling instead.
6. **Global shader uniforms.** `RenderingServer.GlobalShaderParameterSet`, available in 4.0+. They would
   replace the per-material `SetShaderParameter("player_pos")` loop in `VoxelStage._Process`, which costs
   30–70 µs now and 327 µs with chunked walls.
7. **C# interop costs, seen in the profile:**
   - Every `GlobalPosition`, `IsInsideTree` and `Scale` read crosses into native code. `GetNodesInGroup`
     returns a `Godot.Collections.Array` whose enumeration marshals every element
     (`InteropUtils.UnmanagedGetManaged`, 2.7 % of samples).
   - Implicit `string` → `StringName` conversions allocate: `Target.IsInGroup(CombatQuery.PlayerGroup)` in
     `Enemy.DirectionToTarget` accounts for 16 KB/frame.
   - Cache `StringName`/`NodePath` in `static readonly` fields. `CombatQuery` already does this for some names.
   - Avoid `PhysicsDirectSpaceState2D.IntersectShape`/`IntersectRay` in hot loops: they return Godot
     Dictionaries.
8. **Every `_Process`/`_PhysicsProcess` override costs a native → managed call per node per frame.** Monsters
   have four (Enemy, Health, Ailments, ActorStats) where one would do. `SetPhysicsProcess(false)` plus an
   explicit call from the owner saved 1.7–1.9 ms per step (`notick`).
9. **.NET GC.** Most garbage here is per-frame (strings in `VoxelLibrary.MeshFor`; the `IReadOnlyList<T>`
   foreach in `Stat.ComputeFormula`, which boxes `List<T>.Enumerator`, 40 B per stat read). The gen0 budget
   is sized from the L3 cache: this VM reports 504 MB of L3, so GCs here are rare and huge (heap 130–300 MB),
   while a desktop gets frequent 10–30 ms pauses (the gen0=16 MB run). Either way the lever is the allocation
   rate. `GC.Collect()` behind the loading screen is a cheap safety net. `GCSettings.LatencyMode =
   SustainedLowLatency` during play is worth one measured try; it is not proven here.
10. **Fixed timestep / tick rate.** Keep 60 Hz. Godot 4.1 has **no 2D physics interpolation** (it arrived in
    4.3), so a lower tick rate would visibly stutter movement. `max_physics_steps_per_frame` (8) is what
    turned the 2026-10-16 co-op host into a death spiral. A host-side cap is a separate question for the
    multiplayer owner.
11. **Threads.**
    - `WorkerThreadPool` exists in 4.1 C# (`AddTask`, `AddGroupTask`), and Node `ProcessThreadGroup`
      (sub-thread processing) is new in 4.1.
    - Monsters read the player, flow field and physics, and spawn nodes, so moving them to sub-threads is
      high risk for little gain until C1 has been tried.
    - `FlowField.Flood` is cheap and runs only on a cell change. It could move to a worker if a big cavern
      shows a spike there; none was seen.
12. **PhysicsServer2D/RenderingServer for bullets.** This is the standard bullet-hell move. It is not needed
    at today's counts (≤ 8 player projectiles alive in the benches; `BossBullet` already scans ≤ 4 players
    by hand). Revisit with a high-projectile build bench.
13. **Navigation/flow field.** Measured as negligible: `DirectionToTarget` is inside the 0.4–1 µs AI budget.
    The flood allocates one `Queue` per re-flood, which is trivial.

## 6. Ranked candidates

Impact × effort × risk. "Gain" is from the measurements above. Success means re-running the named bench
before and after on the same machine, back to back.

| # | Candidate | Files | Measured or expected gain | Effort | Risk | How to measure success |
|---|---|---|---|---|---|---|
| **C1a** | Skip `MoveAndSlide` when the monster's velocity is zero (sleeping, standing still) | `scripts/Enemies/Enemy.cs` | Sleeping reality -1.3 ms/frame (5.1 → 3.8); physics active objects 246 → 1 | XS | Low. Check that monsters spawned overlapping a wall still get pushed out when they wake. | PerfBench reality steady, plus RunSmokeTest/EndgameSmokeTest |
| **C2** | VoxelActor: cache the mesh per (SpriteFrames, animation, frame); precompute frame index → Mesh per SpriteFrames (no strings); write overlay/transparency only on change; early-out for off-screen actors | `scripts/View3D/VoxelActor.cs`, `VoxelLibrary.cs` | Prototype: -35 % CPU (6.4 → 4.2 µs per actor), alloc 310 → 150 KB/frame. A full string-free `MeshFor` should take allocation to ~0. | S | Low | forcevoxel Rift (VoxActor ms, KB/frame); windowed screenshot diff |
| **C3** | Chunk the voxel terrain: floor/bed MultiMeshes per 12–16 m chunk, then the cave rock mesh and liquid surface per chunk | `scripts/View3D/VoxelStage.cs` (BuildTerrain, AddLiquidSurface, AddCaveRock), `CaveWalls.cs` | Primitives -83 % (Rift) / -90 % (cavern), lavapipe frame about halved. On real GPUs: much less vertex work in the prepass, colour and shadow passes. | S (floors) to M (rock seams) | Low (floors). Medium (rock: faces at chunk seams must not appear). | Windowed PerfBench: primitives, draw calls; screenshot diff of a cavern and the Rift |
| **C4** | One surface per voxel mesh: vertex colours plus one shared material per sheet (emissive colours via vertex alpha or a second surface); `voxel_wall`/`voxel_rock` shaders read the vertex colour | `tools/art/voxels.ts` (writeGlbFrames), `resources/shaders/voxel_*.gdshader`, `VoxelStage.SeeThroughWall`, `VoxelDebris` (palette) | Draw calls ÷ ~5–10 on terrain and actors (999 → roughly 150–250 expected; ×1.2 more chunks after C3). Fewer state changes. | M | Medium: an art-pipeline change; the wall dither and rock palette must look identical. | Windowed draw calls; side-by-side captures (RunCapture `--only=caves`) |
| **C5** | Allocation-free `Stat.ComputeFormula` (index loop over `List<StatModifier>` / `IReadOnlyList` indexer, no foreach on the interface) | `scripts/Stats/Stat.cs` | About -45 KB/frame (-55 % of headless Rift garbage) | XS | Very low (pure function, unit-testable) | PerfBench alloc KB/frame; StatsSmokeTest |
| **C6** | Monster child ticks: Enemy drives Health/Ailments/ActorStats ticks, or they turn themselves off when idle (no timers, no shield, no regen) | `Enemy.cs`, `Combat/Health.cs`, `Combat/Ailments.cs`, `Stats/ActorStats.cs` | -1.7 to -1.9 ms per step at 374 monsters (-18 % of scripts) | S | Low–medium: the order within a frame changes. Must keep co-op replicas and players untouched. | PerfBench Rift scripts ms/step; full test suite |
| **C1b** | Monster crowd collision: first `MaxSlides = 2` (-35 % slide cost). Only if the owner accepts a different crowd feel: no monster-vs-monster bodies plus grid-based soft separation (-78 % slide cost, ~-4 ms/frame at 374) | `Enemy.cs`, enemy `.tscn` masks, new `MonsterGrid` | 1.5–2 ms (MaxSlides 2) to ~4 ms (separation) per frame | S / M | **Design decision**: how crowds flow around walls and each other | PerfBench Rift MoveSlide µs; owner playtest; captures of a cavern chokepoint |
| **C7** | Sun shadow: `Orthogonal` (or `Parallel2Splits`), and fit `DirectionalShadowMaxDistance` to the orthographic view | `VoxelStage._Ready` | Draw calls -60 % (Rift 999 → 403), objects -58 % | XS | Look: shadow sharpness and acne near the camera. Needs a capture comparison. | Windowed draws; RunCapture screenshots |
| **C8** | GC hygiene: `GC.Collect()` after each scene load behind the loading screen (reality, Rift, hideout); log GC pauses in the benches | `RealityController` loading path / `LoadingScreen`, the benches | In-play gen2 spike 274 → 130 ms or gone here; on desktop it removes the "first GC after load" pause | XS | Very low | PerfBench reality awake max frame, GC log; AutoplayBot max frameMs |
| **C9** | Shared per-frame monster registry (C# list maintained by Enemy enter/exit, positions cached once per step) plus a uniform grid for `NearestHostile`, `HostilesInRadius`, `TargetFrame`, `RunDirector`, minimap | `Combat/CombatQuery.cs`, `UI/TargetFrame.cs`, `Run/RunDirector.cs`, `UI/RiftMinimap.cs`, `Combat/LineStrike.cs` | ~0.8 ms/frame steady (TargetFrame plus Director). More with several casters, orbit blades or co-op (performance.md: 28 % of the host in monster scans). | M | Medium: dead or freed monsters must leave the registry. | PerfBench Rift probes; CoopBench host scripts per step |
| C10 | Mass-kill spreading: Cataclysm (and any "kill all") resolved as a ripple over ~6–10 frames | `Run/RunState.Nuke`, `RunDirector.StartFinalBoss` | First frame 95–118 ms → ≤ 20 ms expected | S | **Design decision**: kills land up to ~0.15 s later | RiftBench "cataclysm" line |
| C11 | `player_pos` as a global shader uniform | `VoxelStage._Process`, `voxel_wall`/`voxel_rock` shaders, project global uniform | 30–70 µs now, needed before C3 multiplies the wall materials (327 µs) | XS | Low | VoxStage probe |
| C12 | Mirrored sprites hidden by `VisibilityLayer` plus the viewport `CanvasCullMask` instead of just alpha 0 (keep the alpha too) | `VoxelActor._Ready`, `VoxelStage._Ready` | Removes ~400 canvas items from 2D cull/batch per frame (not measurable on lavapipe) | XS | Low (`IsVisibleInTree` is unaffected) | Hardware capture: canvas draw count, frame CPU |
| C13 | Small ones: RarityPlate `SetProcess(false)` for normal monsters and redraw only on life change; cache the `IsInGroup` StringName in `Enemy.DirectionToTarget`; cache the reality minimap's explored cells in a texture | `Enemies/RarityPlate.cs`, `Enemy.cs`, `UI/RealityHud.cs` | ~0.1–0.2 ms, -16 KB/frame; the minimap grows with exploration (25–72 µs measured early) | XS | Very low | Probes |

The suggested order is C5, C1a, C2, C8, C11 and C3-floors first: each is small, has no visible change and is
measured. Then C6 and C7 (C7 needs a capture comparison), C4 (art pipeline), C9, and the two design questions
C1b and C10 for the owner.

Not proposed (measured as not worth it now):
- projectile and pickup pooling;
- PhysicsServer2D bullets;
- texture compression;
- visibility ranges;
- threading monsters;
- a lower physics tick;
- turning off actor shadows or DOF (the player loses them; at most a "low" preset).

## 7. What is already good (leave alone)

- **Loot queue, damage-number columns, crumble batching, impact budget, chain hop per frame, volatile
  merging** (performance.md first and second passes). ExplosionBench kill frames are now 4–17 ms for 60
  deaths.
- **`CombatQuery`'s per-frame player cache and cached group/path names; `FindTarget` with `HasGroup`
  first.** AI thinking is 0.4 µs per monster.
- **The flow field.** Re-flooded only on a target cell change, and an O(1) lookup per monster.
- **Collision shapes.** Circles everywhere, and run-length-merged wall rectangles in one StaticBody2D.
- **Pickups as Node2D with a distance check instead of Area2D**, plus the 300-gem cap.
- **The voxel debris design** (one MultiMesh per frame of crumbles, one buffer upload) and **no shadows on
  OmniLights**.
- **Auto-instanced actors.** One MeshInstance3D per actor is fine for draw calls in 4.1 Forward+, so
  **don't** rebuild actors as MultiMeshes.
- **Replication.** `RiftHost` 0.5 ms per step, `SkipFarDormant` (per the co-op measurements).

## 8. Sources

- Godot 4.1.1-stable source, read for this report:
  - [`scene/2d/physics_body_2d.cpp`](https://github.com/godotengine/godot/blob/4.1.1-stable/scene/2d/physics_body_2d.cpp)
    (`_move_and_slide_floating`: one `move_and_collide` with recovery per iteration, even at zero motion);
  - [`servers/rendering/renderer_canvas_cull.cpp`](https://github.com/godotengine/godot/blob/4.1.1-stable/servers/rendering/renderer_canvas_cull.cpp)
    (modulate alpha < 0.007 culls; `self_modulate` does not; `visibility_layer & canvas_cull_mask`);
  - [`renderer_rd/forward_clustered/render_forward_clustered.cpp`](https://github.com/godotengine/godot/blob/4.1.1-stable/servers/rendering/renderer_rd/forward_clustered/render_forward_clustered.cpp)
    (automatic instancing of repeated surfaces).
- Godot 4.1 docs (via the godot-docs 4.1 branch):
  - [Optimization using Servers](https://github.com/godotengine/godot-docs/blob/4.1/tutorials/performance/using_servers.rst)
  - [Lights and shadows](https://github.com/godotengine/godot-docs/blob/4.1/tutorials/3d/lights_and_shadows.rst)
  - [Visibility ranges](https://github.com/godotengine/godot-docs/blob/4.1/tutorials/3d/visibility_ranges.rst)
  - [DirectionalLight3D 4.1 class reference](https://docs.godotengine.org/en/4.1/classes/class_directionallight3d.html)
    (Orthogonal fastest, PSSM4 slowest).
- The 4.1.1 GodotSharp API XML shipped with the editor: `WorkerThreadPool`, `Node.ProcessThreadGroup`,
  `CanvasItem.VisibilityLayer`, `Viewport.CanvasCullMask` and `GeometryInstance3D.VisibilityRange*` all exist
  in 4.1. There are no physics-interpolation members (2D physics interpolation arrived in 4.3).
- MultiMesh culling:
  - [godotengine/godot#76436](https://github.com/godotengine/godot/issues/76436) (a maintainer recommends one
    MultiMesh per chunk);
  - [Using MultiMesh (docs)](https://docs.godotengine.org/en/4.6/tutorials/performance/using_multimesh.html)
    (no per-instance culling);
  - [bugnet: MultiMesh frustum culling](https://bugnet.io/blog/fix-godot-multimesh-frustum-culling-disabled-instances).
- CharacterBody crowds:
  - [Godot forum: optimizing a huge number of enemies](https://forum.godotengine.org/t/how-to-optimize-multiple-pathfinding-optimizing-a-huge-number-of-enemies/50709)
  - [Godot forum: 1k enemies at 15 FPS](https://forum.godotengine.org/t/trying-to-run-atleast-1k-enemies-but-goes-at-15-fps-with-just-50-enemies/125229)
  - [Godot forum: many KinematicBody2D](https://forum.godotengine.org/t/how-to-optimize-performance-with-many-kinematicbody2d/26872)
- Survivor-like crowds:
  - [Enemy Masses: physics and hit-detection architecture](https://arawn-software-publishing.gitbook.io/enemy-masses/basics/physics-and-hit-detection-performance-architecture)
    (spatial hash, no per-agent colliders);
  - [Modulus7 devlog: collision detection with a spatial hash](https://stales.itch.io/modulus7/devlog/971426/collision-detection);
  - [Steam discussion on Vampire Survivors collisions](https://steamcommunity.com/app/1794680/discussions/0/3425564948157515863)
    (community speculation only).
- Bullets without nodes:
  - [HowTo: drawing a metric ton of bullets in Godot](https://worldeater-dev.itch.io/bittersweet-birthday/devlog/210789/howto-drawing-a-metric-ton-of-bullets-in-godot)
- C# interop:
  - [State of GDScript vs C# performance in Godot 4.0](https://forum.godotengine.org/t/state-of-gdscript-vs-c-performance-in-godot-4-0/5875/2)
  - [GodotSharpStringCacher](https://www.nuget.org/packages/GodotSharpStringCacher) (StringName allocation from implicit string conversions)
  - [bugnet: Godot.Collections.Array vs System arrays](https://bugnet.io/blog/fix-godot-c-net-godot-collections-array-vs-system-array-perf)
  - [What's new in C# for Godot 4.0](https://godotengine.org/article/whats-new-in-csharp-for-godot-4-0/)
- Context7 wasn't available in this session. `docs.godotengine.org` didn't resolve for WebFetch, so the 4.1
  docs were read from the godot-docs repository branch instead.
