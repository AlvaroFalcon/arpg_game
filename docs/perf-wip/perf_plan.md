# Performance plan: the 2D checklist, C1–C11 (PLAN phase)

Date: 2026-10-24. Base: branch `claude/confident-edison-xy8knx` = origin/main `1b22757` (a merge of `1600f27`, the
research's base; `git diff 1600f27 1b22757` is empty, so every research number and code reference still applies).
Inputs: `perf_research.md` (same scratchpad) and the prototype tools in
`/tmp/claude-0/-home-user/01f50d60-7603-5568-9cde-d97b2c383e48/scratchpad/proj/scripts/tools/{Perf.cs,PerfBench.cs}`.

Executed by **two implementer agents in sequence**. The split point is marked `=== SPLIT ===` below (after I8).
Each increment is **one commit**, on the session branch, with its numbers in `docs/design/performance.md`. Don't
batch increments into one commit: every one must be revertable on its own.

---

## 0. Ground rules for both agents

### Toolchain (from CLAUDE.md; don't skip any of these)

- `GODOT_BIN=/opt/godot/Godot_v4.1.1-stable_mono_linux_x86_64/Godot_v4.1.1-stable_mono_linux.x86_64`. Set
  `DOTNET_ROOT=~/.dotnet` and put `~/.dotnet` first on `PATH` for every Godot or dotnet call.
- Build with `dotnet build ArpgGame.csproj` before **every** headless run. A headless scene runs the last DLL.
- If a new C# script is referenced by a scene for the first time (PerfBench.tscn, I0), run the import
  `"$GODOT_BIN" --headless --path . --editor --quit`. Run it in its **own** shell call, never chained with
  `run_all_tests.sh`.
- Kill leftover processes with `pkill -9 -f "[G]odot_v4.1.1"` (bracket trick) and `pkill -9 -f "[X]vfb"`. Do this
  between runs, and kill `VBCSCompiler` (`dotnet build-server shutdown`) **before every measurement**.
- Judge every run by its `RESULT: PASS` line in a file, never by the exit code or by an absence of FAIL.
- Pass scenes as `res://...`, and add `--quit-after` as a safety net where a scene might not quit.
- Windowed captures: `VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run -a -s "-screen 0 1280x720x24"
  "$GODOT_BIN" --path . --rendering-driver vulkan --resolution 1280x720 ...`. Forward+ on lavapipe gives counts
  (draws, objects, primitives, video memory) that are exact. Its **timings are relative only**, and it steals
  CPU from the main thread, so CPU µs under Xvfb are inflated.

### Measurement discipline

- **A/B on the same machine, back to back, interleaved:** `git stash` (or check out the parent commit), build, run
  ×N; restore, build, run ×N. The container has 4 vCPUs and ±1 ms noise on a 14 ms frame. The monster count
  varies by run (326–374 in the Rift), so **compare µs per call and per monster, not raw totals**, when the counts
  differ.
- Headless reps: 3 per side (Rift, reality). forcevoxel: 2. Windowed: 1–2 (each takes minutes). Report the
  median, and the min–max range.
- Every increment's performance.md section has a before → after table with: frame avg / p95 / p99 / max,
  physics scripts ms/step, the increment's own probe in µs per call, alloc KB/frame, GCs in the window, monsters,
  physics pairs, and, for the visual items, draws / objects / primitives.

### Tests

- `scripts/run_all_tests.sh` (18 suites; CoopSmokeTest, CoopRealityTest and HideoutCoopTest spawn processes and
  need `TEST_TIMEOUT_S` 180 s) must be **18/18 PASS before each commit**. Run it in its own shell call.
- New checks go into **existing** suites (keep the count at 18), named `PERF-*` or after the feature as below.

---

## I0. Tooling: Perf probes, PerfBench, perf_bench.sh, baseline (no gameplay change)

The research recommends adding the PerfBench pattern, so every increment is measured the same way.

**Files**

- **`scripts/tools/Perf.cs`** (new, namespace `ArpgGame.Tools`). A cleaned-up version of the prototype:
  - `public static bool Enabled` (false by default; only PerfBench sets it).
  - `long Begin()` returns 0 when disabled, else `Stopwatch.GetTimestamp()`.
  - `void End(K k, long t0)` returns at once when disabled.
  - `long BeginAlloc()` / `EndAlloc(K, t0, b0)` with `GC.GetAllocatedBytesForCurrentThread()`.
  - The counters `Ticks` / `Calls` / `Bytes` per `K`, plus `Reset()` and `Ms(K)`.
  - **Drop** the `PERF_EXP` env experiment set. The repo does A/B by commit, not by switches. The one exception
    is the bench flags below.
  - Keep `K` to the probes actually wired: `EnemyPhys, MoveSlide, FindTarget, EnemyTicks, Crowd, VoxActor,
    VoxMeshFor, VoxStage, TargetFrame, Director, Nuke`. Add entries when an increment adds a probe.
- **Probe call sites** (two static calls each; they cost about a branch when `Enabled` is false):
  - `Enemy._PhysicsProcess` (whole body, with alloc) and around each `MoveAndSlide` (a private `Slide()`
    helper, as in the prototype);
  - `Enemy.FindTarget`;
  - `VoxelActor._Process` (with alloc) and around `VoxelLibrary.MeshFor`;
  - `VoxelStage._Process`;
  - `TargetFrame._Process` and `RunDirector._PhysicsProcess`.
  - The prototype's probes in Player, Pickup, Projectile, RunHud and the rest showed < 0.25 ms together. **Don't
    add those.**
- **`scripts/tools/PerfBench.cs` + `scenes/tools/PerfBench.tscn`** (new). The prototype, cleaned up:
  - It sets `Perf.Enabled = true`, and logs to `user://bench/perf_<mode><tag>.txt`.
  - Args: `--mode=rift|reality`, `--run-time=350`, `--fill=1800`, `--frames=1200`, `--tier=10`,
    `--layout=bone_warrens`, `--seed=7`, `--tag=`.
  - Flag `--forcevoxel`: sets a new `HdView.ForceInHeadless` static (one line in `HdView.Enabled`:
    `(DisplayServer.GetName() != "headless" || ForceInHeadless) && ...`). It measures the voxel view's CPU cost
    without lavapipe.
  - Flag `--crowd=bodies`: only after I12 (see there).
  - Flag `--shots=<dir>`: the screenshot set of section 3, saved before the fill.
  - Flag `--lineup`: spawns every enemy scene and the player classes dormant in a row for the shots.
  - Keep the step probe (reuse `StepProbe` from `CoopBench.cs`), the GC log (`GC.GetGCMemoryInfo` on every new GC
    index), the spike log (> 50 ms), the render monitors and the per-probe summary.
  - Add one machine-readable line per phase, so tables are pasted, not retyped:
    `PERF SUMMARY phase=steady monsters=… avg=… p95=… p99=… max=… scripts=… allocKB=… gc0=… gc1=… gc2=… pairs=…
    active=… draws=… objects=… prims=… <probe>_us=…`
  - Reality mode keeps "steady" (packs asleep), then wakes every non-boss pack and measures "awake".
  - It replaces `LINQ` in `Stats` only if that is trivial. It runs after measurement, so it doesn't matter.
- **`scripts/perf_bench.sh`** (new). Usage:
  `perf_bench.sh --mode=rift|reality [--reps=3] [--window] [--forcevoxel] [-- extra bench args]`.
  - It builds, shuts down the build server, kills leftovers and runs PerfBench.
  - Headless: `--headless --fixed-fps 60`. With `--window`: Xvfb + lavapipe + `--fixed-fps 60`, and the defaults
    `--fill=120 --frames=60`.
  - It polls the result file for `RESULT:`, kills the process, then prints every `PERF SUMMARY` line and copies
    the result files to `user://bench/history/<timestamp>_<mode>.txt`.
  - Model it on the scratchpad's `run.sh` / `runwin.sh`.
- **`RiftBench.cs`**: add the first ten frame times after the Cataclysm to its `cataclysm` line, needed for I9.
  Today it prints only "first two".

**Baseline to record (commit it in the I0 performance.md section)**

| Bench | Command | Reps |
|---|---|---|
| PerfBench Rift headless | `perf_bench.sh --mode=rift --reps=3` | 3 |
| PerfBench reality headless (steady + awake) | `perf_bench.sh --mode=reality --reps=3` | 3 |
| PerfBench Rift forcevoxel | `perf_bench.sh --mode=rift --forcevoxel --reps=2` | 2 |
| PerfBench Rift windowed | `perf_bench.sh --mode=rift --window` | 2 |
| PerfBench reality windowed | `perf_bench.sh --mode=reality --window` | 2 |
| RiftBench (cataclysm line) | `--headless --fixed-fps 60 res://scenes/tools/RiftBench.tscn` | 3 |
| ExplosionBench | `-- --kind=volatile --count=60` and `--kind=chain --count=60`, reality and Rift | 3 |
| AutoplayBot survival | `autoplay.sh --runs=6 --class=warrior` and `--class=arcanist` | 1 |
| AutoplayBot reality | `autoplay.sh --mode=reality --class=warrior --tier=10 --runs=6` (ttk, dmgTaken, frameMs) | 1 |
| CoopBench Rift | `CoopBench.tscn -- --clients=3 --run-time=350` | 1 |
| CoopBench reality | `-- --clients=3 --mode=reality --tier=10` | 1 |
| Screenshot set "base" | section 3 | 1 |

- The research numbers (perf_research.md §2) are the reference. If the I0 baseline disagrees by more than
  ±15 %, write it down and use the I0 numbers.
- **Acceptance:**
  - `Perf.Enabled = false` costs nothing measurable: PerfBench is not running, so use RiftBench avg before and
    after I0, within noise (±0.7 ms).
  - All 18 suites pass.
- **Risk:** probes left enabled in shipped code. Mitigated by the static `Enabled` gate, false unless PerfBench
  sets it.
- **Rollback:** revert the commit. Nothing else depends on it, except later increments' probes, which are small.

---

## Zero-visual-change items (agent 1)

### I1. C5: allocation-free `Stat.ComputeFormula` (+ optional C13a)

- **Change:** `scripts/Stats/Stat.cs`, `Stat.ComputeFormula(float, IReadOnlyList<StatModifier>, float)`.
  - Replace the `foreach` over the interface, which boxes `List<T>.Enumerator`, 40 B per call, with
    `if (modifiers is List<StatModifier> list) { for (int i = 0; i < list.Count; i++) ... } else { for (...)
    modifiers[i] ... }`.
  - Also check that `ActorStats.GetModifiers(ModifierQuery)` doesn't allocate a new list per call. If it does,
    note it as a follow-up and don't change it here: it's a different hot path. Callers:
    `SkillUser` line 443, `RunController`, `RealityController`, `AscendancyEffects`.
- **Optional (C13a, owner's scope lists C1–C11, so mark it in the commit and doc as a one-line extra):**
  - `Enemy.DirectionToTarget` calls `Target.IsInGroup(CombatQuery.PlayerGroup)`, an implicit string →
    StringName, 16 KB/frame.
  - Use a cached `static readonly StringName`, or `Target is Player`, which is equivalent: only players are in
    that group. Check this against the group's members.
- **Measure:** PerfBench Rift headless ×3.
  - **Accept:** managed alloc ≤ 40 KB/frame (from ~80). With C13a: ≤ 25.
  - Frame avg not worse than baseline + 0.5 ms.
- **New check** (StatsSmokeTest) `PERF-STAT-FORMULA-NO-ALLOC`:
  - 10 000 calls of `ComputeFormula` over a 6-modifier `List` allocate 0 bytes
    (`GC.GetAllocatedBytesForCurrentThread` delta, after one warm-up call).
  - The results equal the old formula for Flat / Increased / More mixes, including the `flatEffectiveness` cases
    already in EndgameSmokeTest.
- **Risk:** very low (a pure function). **Rollback:** revert.

### I2. C1a: skip `MoveAndSlide` at zero velocity

- **Change:** `scripts/Enemies/Enemy.cs`.
  - In `_PhysicsProcess`, route the three `MoveAndSlide()` calls (Dormant branch, Frozen branch, main) through
    `private void Slide() { if (Velocity == Vector2.Zero) return; MoveAndSlide(); }`.
  - `Velocity` stays as set, so `UpdateVisual`'s facing is unchanged.
- **Why it's safe:** a zero-motion slide in 4.1.1 only depenetrates (`_move_and_slide_floating`). A sleeping or
  standing monster has nothing to depenetrate from, except:
  - (a) a spawn that overlaps a wall;
  - (b) monster-on-monster overlap while both stand. That one is cosmetic, and it goes away entirely with I12.
- **Measure:** PerfBench reality ×3 and Rift ×3.
  - **Accept (steady):** reality steady avg ≤ 4.2 ms (from 5.1). Physics2D active objects ≤ 10 (from 246).
  - **Accept (other phases):** reality awake and Rift within noise of baseline.
- **New check** (EndgameSmokeTest) `PERF-IDLE-SLIDE-WAKE-DEPENETRATES`:
  - Spawn a dormant Slime so its circle overlaps a wall rectangle of a built layout by 6 px, and run 10 frames.
    It stays dormant. That is allowed.
  - Wake it (`Wake()`) and run 30 frames. Its circle no longer overlaps the wall.
  - Also assert that a frozen monster with knockback still drifts: the knockback velocity is non-zero, so it
    slides.
  - Re-check `MovementSmokeTest` and `RunSmokeTest` pack placement. Packs are placed on floor cells, so (a)
    should never happen in real layouts. Assert in the same check that every monster of a generated T10
    cavern (seed 7) spawns outside walls.
- **Co-op:** replicas never call `MoveAndSlide` (they return early). The host only gets cheaper. Covered by
  CoopRealityTest in the full suite.
- **Risk:** low. **Rollback:** revert (one helper).

### I3. C2: VoxelActor mesh cache, no string building

- **Change:**
  - **`scripts/View3D/VoxelLibrary.cs`.** Add a per-`SpriteFrames` frame table built once:
    `Dictionary<ulong /*SpriteFrames instance id*/, FrameTable>`.
    - `FrameTable` maps each animation `StringName` to a `Mesh?[]` indexed by frame. It is built with today's
      exact rule: atlas region → cell index → `f{index}`, falling back to `f0`.
    - New overload `MeshFor(SpriteFrames frames, StringName anim, int frame)` does no string work and no
      `ModelPathFor` per call.
    - Keep `MeshFor(AnimatedSprite2D)` as a wrapper. `VoxelStage.OnEnemyKilled` uses it, once per death, which
      is fine.
  - **`scripts/View3D/VoxelActor.cs`.**
    - Event-driven mesh: connect to the sprite's `SpriteFramesChanged`, `AnimationChanged` and `FrameChanged`
      signals (all exist on `AnimatedSprite2D` in 4.1). They set `_meshDirty`. Resolve the mesh only when
      dirty. Reading `Sprite.Animation` every frame allocates a managed `StringName`, so **don't poll it**.
      Disconnect in `_ExitTree`.
    - Cache `parent`, `anchor`, `_flat` and `lying`'s player reference in `_Ready`. Only the `Player` cast is
      per-frame; `ShowsDowned` is a property.
    - `ApplyModulate`: keep a `_plain` flag and skip the writes when it is plain and was plain last frame (as in
      the prototype). When not plain, only write `_overlay.AlbedoColor` and `Transparency` if they changed.
  - Not done, and written in the doc as rejected: an off-screen early-out. Off-screen actors cast shadows into
    view, and their transform must be current when they enter.
- **Measure:** PerfBench Rift `--forcevoxel` ×2, and the Rift windowed ×1 for counts.
  - **Accept (CPU):** `VoxActor` ≤ 3.5 µs per actor (from 6.4). VoxelActor alloc ≤ 5 KB/frame (from ~234).
  - **Accept (whole frame):** forcevoxel alloc ≤ 80 KB/frame (with I1). Forcevoxel frame avg −2 ms or better.
  - **Accept (render):** draws and objects unchanged within ±2 %.
- **Visual:** the `--lineup` shots plus one Rift shot, compared with the base set (section 3). Accept: identical
  (AE ≤ 0.5 % at fuzz 2 %).
- **New check** (EndgameSmokeTest, headless; loading .glb works without the 3D stage)
  `VOXEL-FRAME-TABLE-MATCHES`: for every sprite with a voxel model (player classes, enemies, bosses), every
  animation and every frame, the table's mesh is the same instance the old string path returns. Keep the old
  path as a private test helper, or compute the expected `f{index}` inline in the test.
- **Risk:** low. A sprite whose `SpriteFrames` is swapped at runtime (class change, co-op look) must re-resolve,
  and the `SpriteFramesChanged` signal covers that. **Rollback:** revert.

### I4. C8: `GC.Collect()` behind the loading screen

- **Change:** `scripts/UI/LoadingScreen.cs` `Run(string)`.
  - After the 4 settle frames and before the minimum-time wait: `GC.Collect(); GC.WaitForPendingFinalizers();
    GC.Collect();`, timed with a `Stopwatch` and logged with `GD.Print` only under `OS.IsDebugBuild()`.
  - This covers the reality, the Rift and the hideout: everything that goes through `Core.Scenes.Go`.
  - Also try `GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency`, set once in a scene's `_Ready`, as
    a **separate measured A/B**. Commit it only if the reality-awake max frame improves on top of the collect
    without raising alloc or heap. The research didn't prove it.
- **PerfBench:** add `--viascreen` so reality mode enters through `UI.LoadingScreen.Go`, the real path, instead of
  `ChangeSceneToFile`.
- **Measure:** PerfBench reality `--viascreen` ×3, and AutoplayBot reality T10 `--runs=6` (frameMs max) ×1.
  - **Accept (spikes):** in the reality-awake window, no gen2 GC in ≥ 2 of 3 runs, and max frame ≤ 150 ms (from
    274).
  - **Accept (cost):** the collect itself ≤ 100 ms on this container (the research saw 54–63 ms). It is hidden by
    the screen: the minimum is 0.45 s and the load takes longer.
  - Report the AutoplayBot max frameMs before → after. It is noisy, so no hard threshold.
- **Risk:** very low. `WaitForPendingFinalizers` on the main thread was exercised in the research (`gcload`).
  Watch for a hang at the loading screen in CoopRealityTest and HideoutCoopTest, which go through it.
- **Rollback:** revert.

### I5. C6: Enemy drives its Health, Ailments and ActorStats ticks

- **Change:**
  - **The three components.** `scripts/Combat/Health.cs`, `scripts/Combat/Ailments.cs` and
    `scripts/Stats/ActorStats.cs` each move the body of `_PhysicsProcess` into
    `public void Tick(double delta)`, and `_PhysicsProcess` calls `Tick`. Players, dummies and everything else
    behave exactly as before.
  - **`scripts/Enemies/Enemy.cs`.**
    - In `_Ready`, `if (!IsReplica)` (it is set before the node enters the tree) call `SetPhysicsProcess(false)`
      on the three children.
    - At the top of the non-replica path of `_PhysicsProcess`, after `if (IsReplica)` and **before** the
      `Health.IsDead` check, call `Stats.Tick(delta); Health.Tick(delta); Ailments?.Tick(delta);`. The order
      matches the tree order: Stats, Health, Ailments are children in that order. Confirm this against the
      enemy `.tscn` files, and keep that order.
    - Ticking before the dead check means an ignite that kills during the tick is seen in the same frame.
  - **Order change to write in the doc:** the children used to tick *after* the parent's AI in the same
    physics frame. Now they tick before it. Expected effect: none visible (one frame earlier for timers).
  - Replicas keep their own child processes, unchanged.
- **Measure:** PerfBench Rift ×3.
  - **Accept (cost):** physics scripts ms/step down ≥ 1.4 ms at the same monster count (from 9.7–10.4), i.e.
    ≥ 3.5 µs per monster less.
  - **Accept (no regression):** reality steady and awake not worse.
- **New check** (StatsSmokeTest) `ENEMY-TICKS-DRIVEN-BY-OWNER`. On a spawned Slime:
  - its three children report `IsPhysicsProcessing() == false`;
  - a 2 s chill applied to it expires within 2 s ± 1 frame, and its movement speed recovers;
  - an ignite DoT ticks its life down at the expected rate (± 1 frame);
  - a `regenerating` rare still heals.
  - On a co-op replica (an `Enemy` with `IsReplica = true` added to the tree), the children are still processing.
- **Co-op:** CoopSmokeTest and CoopRealityTest (in the suite). Replicas aren't touched, and the host monsters
  tick as before.
- **Risk:** low–medium, through the order of operations. A missed tick path: the Dormant and Frozen branches
  return early, so the ticks **must** come before them. **Rollback:** revert.

### I6. C11: `player_pos` as a global shader uniform (before C3)

- **Change:**
  - **`project.godot`.** Add a `[shader_globals]` section with
    `player_pos={"type":"vec3","value":Vector3(0, -100, 0)}`.
  - **`resources/shaders/voxel_wall.gdshader` and `voxel_rock.gdshader`.** Replace `uniform vec3 player_pos`
    with `global uniform vec3 player_pos;`. Keep `fade_radii` and `chest_height` as material uniforms.
  - **`scripts/View3D/VoxelStage.cs` `_Process`.**
    - Replace the loop over `_wallMaterials` with one
      `RenderingServer.GlobalShaderParameterSet(PlayerPosName, feet)`, using a `static readonly StringName
      PlayerPosName = "player_pos"`.
    - In `_ExitTree`, reset it to `(0, -100, 0)`.
    - `_wallMaterials` is still needed by `RebuildTerrain`. Keep the list, but don't iterate it per frame.
  - Fallback if `[shader_globals]` isn't read by exported builds or tests: in `VoxelStage._Ready`, call
    `RenderingServer.GlobalShaderParameterAdd` when the global is missing (check with
    `GlobalShaderParameterGetList`).
  - Verify on a Windows/macOS **export** that the global exists. The research saw it only in-editor/run.
- **Measure:** Rift windowed ×1 and reality windowed ×1.
  - **Accept:** `VoxStage` ≤ 30 µs/frame (from 30–70), and it no longer grows with the material count. Counts
    unchanged.
- **Visual:** shot `reality-behind-wall` (section 3: the player standing behind a wall, so the dither shows),
  and the hideout canal and plaza shot. **Accept: identical** (AE ≤ 0.5 % at fuzz 2 %).
- **Risk:** low. A shader that fails to compile shows as magenta walls, so the shots catch it.
  **Rollback:** revert.

## Terrain chunking (agent 1)

### I7. C3a: chunk the floor, liquid-bed, wall and liquid-surface MultiMeshes

- **Change:** `scripts/View3D/VoxelStage.cs` `BuildTerrain(TileMap)` and `AddLiquidSurface(...)`.
  - Key `byPiece` by `(string piece, Vector2I chunk)` with `chunk = (floor(at.X / ChunkMeters), floor(at.Z /
    ChunkMeters))` and `const float ChunkMeters = 12f`. Use a struct key, not the prototype's string
    concatenation and `Split('@')`.
  - One `MultiMeshInstance3D` per (piece, chunk), named `Terrain_{piece}_{cx}_{cz}`.
  - The wall piece's `SeeThroughWall(mesh)` copy must be made **once per tileset**, not once per chunk, and
    shared by all wall chunks. Otherwise the wall materials multiply.
  - `AddLiquidSurface`: keep one shore-mask texture and one `ShaderMaterial`, and split `placed` into one
    MultiMesh per chunk that shares the quad mesh.
  - `RebuildTerrain` (hideout repaving): `created` holds every chunk node, so removal still works. Make sure the
    material-removal loop doesn't remove the shared wall materials more than once.
  - Try 16 m chunks once, as an A/B, and keep whichever gives the lower primitives at about equal draws. Default:
    12 m.
- **Measure:** Rift windowed ×2 and reality windowed ×2 (packs asleep, plus awake).
  - **Accept (primitives):** Rift ≤ 1.5 M (from 7.46 M). Cavern ≤ 0.8 M (from 5.54 M).
  - **Accept (frame):** lavapipe frame −40 % or better.
  - **Accept (draws):** draw calls may rise, by ≤ 30 %. I13 and I10 bring them down.
  - **Accept (CPU):** `VoxStage` unchanged (thanks to I6).
  - Build time of `BuildTerrain`: log it. It must not grow by more than 20 % (the research didn't measure it).
- **Visual:** the whole section-3 set (reality and Rift positions, hideout). **Accept: identical** (AE ≤ 0.5 % at
  fuzz 2 %).
- **Risk:** low.
  - Liquid shore seams: the mask is global, so none are expected.
  - Floating-point chunk boundaries: a cell exactly on a boundary goes to one chunk only, because `floor` is
    used once per cell.
- **Rollback:** revert. `ChunkMeters = 1e6` reproduces today's behaviour for a quick A/B, but don't ship it as a
  setting.

### I8. C3b: chunk the cave rock mesh

- **Change:**
  - **`scripts/View3D/CaveWalls.cs`.** Add an optional `Rect2I? squares` parameter to
    `Build(HashSet<Vector2I> walls, Func<Vector2, Vector3> ground, Func<Vector2, float> height, Rect2I? squares
    = null)`.
    - The marching-squares loop only emits squares whose origin lies in `squares`.
    - Neighbour lookups still use the full `walls` set, so each chunk's geometry is exactly the subset of
      today's triangles: same positions, same flat normals.
    - Also fix the `Tri` helper's `new[] { a, b, c }` per triangle (an allocation; load time only, minor).
  - **`scripts/View3D/VoxelStage.cs` `AddCaveRock`.** Loop over chunk rects (`ChunkMeters` converted to cells via
    `tileMeters`) and call `CaveWalls.Build` per chunk.
    - One shared rock `ShaderMaterial` on all chunks, so `_wallMaterials` gains one material, not N.
    - One `MeshInstance3D` per non-empty chunk (`Terrain_rock_{cx}_{cz}`).
    - `AddCaveRock` returns a `List<Node>` now. Update the `created.Add` call.
- **Measure:** reality windowed ×2 (cavern), plus one `--layout=` of each cave style (ashen, crypt, grotto) ×1.
  - **Accept:** cavern primitives ≤ 0.5 M (I7 value minus the rock's off-screen share). Report draws and the
    `BuildTerrain` time (≤ +20 %).
- **Visual:** RunCapture `--only=caves` (every cavern style: entrance, liquid, arena entrance, overview) and
  reality positions. **Accept:** identical, **with special attention to chunk seams on the rock tops and sides**
  (AE ≤ 0.5 % at fuzz 2 %; inspect the diff image along chunk lines). The rock shader colours in world space, so
  no colour seams are expected. Geometric T-junction cracks are possible only if the vertex positions differ,
  and they don't (same `ground()`/`height()` calls).
- **Risk:** medium (seams). **Rollback:** revert. Floors (I7) are independent.

`=== SPLIT === Agent 1 stops here.` Hand-off contents:
- the I0 baseline section and the I1–I8 sections in performance.md;
- the post-I8 numbers, which become agent 2's "before";
- the screenshot set `base` and `post-I8` in `user://bench/shots/`, plus the copies that matter in
  `docs/devlog/screenshots/perf-*.png`;
- a journal entry;
- 18/18 PASS.

Agent 2 re-runs the PerfBench Rift and reality, headless ×3 and windowed ×1, as its own "before". The machine may
differ.

---

## Agent 2

### Pre-flight (do this first; it gates I11)

- `cd tools/art && npm install && npm run setup`. `setup` clones EasyPixel from GitHub at a pinned commit, through
  the proxy. If the clone fails, try `gh repo clone AlvaroFalcon/EasyPixel` at that commit, and read
  `/root/.ccr/README.md`.
- Then run `npx tsx voxels.ts` **unchanged** and check that `git status assets/voxels` is clean (byte-identical
  output). If it isn't, the art sources moved since the last export. Stop and report: C4 must not ship unrelated
  geometry changes.

### I9. C10: the Cataclysm as a ripple over 8 physics frames (owner decision: kills ≤ ~0.15 s later)

- **Change:** `scripts/Run/RunState.cs` `Nuke(Player)`.
  - It snapshots the targets at once: the same `ViewOf(player)` rect, the same non-boss filter, and the same
    boss rule (10 % life to bosses on screen at once, on the pickup frame).
  - Sort the victims by distance to the player, so the ripple runs outward. That reads as a shockwave.
  - Push them into a `_nukeQueue` (`List<Enemy>`, reused) and return the **victim count** at once. The toast
    "Cataclysm — N slain", the co-op pickup `detail` and `RiftClient`'s toast keep working unchanged.
  - New `RunState._PhysicsProcess` work (or the existing per-frame update) kills
    `perFrame = ceil(victims / 8)` per physics frame (minimum 1). Each batch runs inside `_nuking = true …
    false`, so the victims still never drop power-ups.
  - Skip victims that are `!IsInstanceValid`, already dead or queued for deletion.
  - Clear the queue in `_ExitTree`. When `RunDirector.StartFinalBoss` runs, it kills everything anyway, so the
    queue empties itself.
  - A second Nuke during a ripple (co-op: two players) appends. Already-queued victims are skipped by the
    `IsDead` check.
  - **Not changed:** `RunDirector.StartFinalBoss`'s clear-all. The owner only decided the Cataclysm. Write the
    same ripple for the boss clear in performance.md as a **proposal**: it is a kill-all on the frame the
    Colossus spawns.
- **Tests:**
  - **Existing.** `RunSmokeTest` `NUKE-KILLS-ON-SCREEN` must `await Frames(10)` before asserting `near` is dead.
    It still asserts `far` alive and `killed >= 1`. `NUKE-GIVES-EXPERIENCE` awaits 2 more frames after that.
  - **New check** (RunSmokeTest) `NUKE-RIPPLE-SPREAD`:
    - Spawn 120 Slimes on screen and call `Nuke`. The return value is 120.
    - At most `ceil(120/8) + 1` die in any one frame, all are dead within 10 physics frames, and no power-up
      pickup comes from the victims.
    - A boss on screen loses 10 % on the pickup frame.
  - **Updates.** `RiftBench` prints the max frame over the ripple. `CoopBench`'s "killed at once" line: reword it
    to "victims".
- **Measure:** RiftBench ×3 (cataclysm line), and CoopBench Rift ×1 (host frames after the Cataclysm).
  - **Accept:** the max single frame during the ripple ≤ 25 ms headless (from 95–118). All victims dead ≤ 10
    frames after the pickup. The kill count equals the snapshot.
- **Co-op:** CoopSmokeTest (the pickup toast and detail path). The host's despawn events now spread over 8
  snapshots instead of one, which also smooths the client-side spike. Check the client log for no errors in
  CoopBench.
- **Risk:** low.
  - The player sees the kills land over about 0.13 s.
  - A victim that walks off screen during the ripple still dies (the snapshot rule). That is intended.
  - **Flag for the owner in run.md:** "Cataclysm resolves as an outward ripple over 8 frames".
- **Rollback:** revert.

### I10. C7: the sun shadow in `Orthogonal` mode (visual change: before/after captures required)

- **Change:** `scripts/View3D/VoxelStage.cs` `_Ready`, the `_sun` initializer.
  - Set `DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal`.
  - Fit `DirectionalShadowMaxDistance` to the orthographic view. The camera sits 40 m from its target, and the
    view's depth extent is about ±(Camera.Size/2)/tan(pitch). Try 52 m and 60 m, as an A/B. Keep 60 if 52 clips
    shadows at the screen's top edge.
  - If acne or peter-panning appears, A/B `ShadowBias` between 0.03 and 0.05, and `ShadowNormalBias` from the
    default 1.0 to 2.0.
  - Fallback if Orthogonal looks too soft: `Parallel2Splits`. Measure it too, and give the owner both.
- **Measure:** Rift windowed ×2 and reality windowed ×2.
  - **Accept (Rift):** draw calls ≤ 50 % of the post-I8 value (the research went 999 → 403 before chunking).
  - **Accept (cavern):** objects down ≥ 40 %, and draws down. Report the lavapipe frame.
- **Visual, required:**
  - The full section-3 set, plus RunCapture `--only=caves` and `--only=town`, with the post-I8 state as "before".
  - Produce side-by-side images (`convert before.png after.png +append`) for 4 representative shots:
    - Rift centre with the lineup;
    - reality entrance;
    - reality behind a wall;
    - the hideout plaza.
  - Commit them to `docs/devlog/screenshots/perf-shadow-{before,after}-*.png`.
  - **Acceptance is the owner's eye.** This agent reports what changed: softer or sharper edges, the shadow
    length near the screen edges, any acne. Write in performance.md what the player loses.
- **Risk:** look only: shadow resolution, bias, the edge of the max distance. **Rollback:** revert one line;
  `Parallel2Splits` is the middle ground.

### I11. C4: one surface and one material per voxel mesh (art pipeline + import)

- **Pipeline:** `tools/art/voxels.ts` `writeGlbFrames(...)`.
  - **One primitive per mesh**, with a `COLOR_0` attribute: `VEC4`, `UNSIGNED_BYTE`, normalized, written as
    **linear** RGB, plus alpha.
  - Alpha 255 = plain. **128 = emissive**: tileset liquids keep their `emissive` predicate. The hazard slabs are
    in fact never drawn by `BuildTerrain`, but keep the flag for correctness.
  - Use `Uint32` indices when the vertex count is above 65535.
  - **One material** per file: white `baseColorFactor`, roughness 1, metallic 0.
  - The empty-frame degenerate triangle keeps its single primitive.
  - Update the file's doc comment and `docs/design/art-pipeline.md` -> "Voxels".
- **Game side:**
  - **New `resources/shaders/voxel_vertex.gdshader`.**
    - `ALBEDO = COLOR.rgb; ROUGHNESS = 1.0; METALLIC = 0.0;`.
    - `EMISSION = COLOR.rgb * emission_energy * step(COLOR.a, 0.75)`.
    - `render_mode diffuse_lambert, specular_disabled`, matching what the StandardMaterial look gives today.
      Confirm by capture.
    - If the result differs, use a `StandardMaterial3D { VertexColorUseAsAlbedo = true, VertexColorIsSrgb =
      false, Roughness = 1 }` instead, and drop emission. Hazard slabs aren't drawn.
  - **`VoxelLibrary.Collect`.** Assign **one shared material instance** to surface 0 of every loaded mesh, so the
    importer's material choice doesn't matter and every actor shares one material. Auto-instancing then batches
    identical meshes across the whole crowd.
  - **`VoxelStage.SeeThroughWall`.** One surface now. `voxel_wall.gdshader` uses `ALBEDO = albedo.rgb *
    COLOR.rgb` with `albedo` defaulting to white. Keep the dither.
  - **`_wallColor`.** Today it is read from surface 0's material albedo. Compute it from the wall mesh's vertex
    colours instead: the median luminance of the distinct colours, through a helper
    `VoxelLibrary.PaletteOf(Mesh)` that reads `SurfaceGetArrays(0)[Color]` once and caches it.
  - **`AddCaveRock` palette.** Same helper: the distinct colours sorted by luminance. It must yield the same
    three tones as today. Test below.
  - **`VoxelDebris.CubesOf`.** Read the per-vertex colour (`arrays[(int)Mesh.ArrayType.Color]`), not the
    surface material. Keep the 4-vertices-per-quad assumption, and verify it after import: the importer must not
    weld the quads' vertices. With a colour attribute per quad it shouldn't.
  - **sRGB / linear:** glTF `COLOR_0` is linear. Check one known colour end to end, for example the slime's main
    green, against today's `StandardMaterial3D.AlbedoColor` of that surface. Godot's albedo colour is sRGB, and
    the shader's `COLOR` is what the importer stored. Fix the conversion on the exporter side if they differ.
- **Import settings:** today's `.glb.import` files have `meshes/generate_lods=true`. Godot's LOD simplifier may
  collapse vertices across colour borders once the colours live in one surface, so colours could bleed at LOD.
  - A/B `generate_lods=false` for the voxel files. Compare primitives and the look at the default zoom and the
    farthest zoom.
  - Keep LODs only if the captures are clean.
  - Edit the `.import` params (or the importer defaults), then re-run the import in its own shell call.
- **Regenerate:** `cd tools/art && npx tsx voxels.ts` → the import (`--editor --quit`) → `dotnet build`. Report
  the size of `assets/voxels` before → after. Today it is 61 MB, and normalized-ubyte colours add about
  4 B/vertex. The repo grows by one regeneration's worth: note it.
- **Tests:**
  - **New check** (EndgameSmokeTest) `VOXEL-ONE-SURFACE`: every loaded voxel mesh has `GetSurfaceCount() == 1`
    and a `Color` array.
  - **New check** `VOXEL-PALETTE-STABLE`: for each tileset wall, `PaletteOf` returns the same darkest, middle and
    lightest colours as a table recorded **before** the change. Record that table in agent 2's first commit of
    I11, as the test's expected values, from today's per-surface materials.
  - **New check** `VOXEL-DEBRIS-COLOURS`: `CubesOf(slime f0)` has the same count of points and the same set of
    colours (± 1/255) as before (also recorded before).
  - `VOXEL-FRAME-TABLE-MATCHES` (I3) keeps passing.
- **Measure:** Rift windowed ×2, reality windowed ×2, and forcevoxel Rift ×1 (the VoxActor CPU must not regress).
  - **Accept (draws):** draw calls ÷ ≥ 3 against post-I10 in the Rift, and ≥ 2 in the cavern. The research
    expected ÷ 5–10 on terrain and actors.
  - **Accept (cost):** buffer memory not more than +25 %. Lavapipe frame not worse.
- **Visual, required:** the full section-3 set, plus the `--lineup` (every enemy, boss and class), RunCapture
  `--only=caves` and `--only=town`, against post-I10.
  - **Accept:** AE ≤ 1 % at fuzz 3 % per shot, with DOF and dither regions inspected by eye. Side-by-sides of
    the lineup and one cavern go to `docs/devlog/screenshots/perf-voxel-colour-{before,after}-*.png`.
  - Any visible colour shift is a stop. Fix the conversion; don't ship.
- **Risk:** medium.
  - Colour space.
  - LOD bleeding.
  - Debris and palette code that assumed per-surface materials. Found in three places: `VoxelStage`
    (`_wallColor`, `AddCaveRock`) and `VoxelDebris.CubesOf`. Grep again for `SurfaceGetMaterial` before
    committing.
- **Rollback:** revert the commit, which restores the old `.glb` files, then re-run the import.

### I12. C9a + C1b: monster registry and grid, soft crowd separation (owner decision; changes the feel)

**Owner decision:** remove monster-to-monster physics collision and replace it with a cheap soft separation force.
Monsters may overlap slightly. **Facts checked in the code:**
- The enemy `.tscn` files have `collision_layer = 4` (EnemyFaction). Normal monsters have `collision_mask = 21`
  (World | EnemyFaction | Hazard). Bosses (EmberColossus, Nereth, BoneHerald) have `mask = 17` (World | Hazard).
- Player: `layer = 2`, `mask = 17`. **There is no player-vs-monster body collision today.** Monsters pass through
  players by design (enemies.md). Player-vs-monster interaction is the distance-based contact damage
  (`Enemy.IsTouching`, `ContactRadius × Scale`).
  - "Player-vs-monster collision stays" therefore means: **contact damage, the players' own layers and masks,
    and the monsters' World | Hazard collision are untouched.** Don't add player-vs-monster bodies.
- Radii (`CircleShape2D`): Bat 8, Imp 9, Skeleton 9, Slime 10, Caster 10, Siren 10, Thrall 13, Brute 15, bosses 22
  to 36. Rarity scale: ×1.1 / 1.25 / 1.45.

**C9a: `scripts/Enemies/MonsterCrowd.cs` (new, static, no autoload; tests run scenes directly)**

- **Registry.**
  - `Enemy._EnterTree` adds `this` to a static `List<Enemy>` and stores its index; `_ExitTree` swap-removes it.
    No `GetNodesInGroup`.
  - Replicas register, with a flag, so later C9b users can count them, but they **don't take part** in
    separation.
  - Cached per monster: `CrowdRadius` = shape radius × |Scale.X|. Read the `CollisionShape2D` once in `_Ready`,
    and refresh it in `ApplyRarity` and `ApplyReplicaLook` and wherever `Scale` changes (the splitter child
    sets 0.75 before entering the tree).
- **Per-frame solve, once per physics frame.** The first non-replica `Enemy._PhysicsProcess` of a frame calls
  `MonsterCrowd.Solve()` (guard `Engine.GetPhysicsFrames() != _lastFrame`). No new node is needed.
  1. Read `GlobalPosition` once per participating monster into a `Vector2[]` (pooled, grown as needed). This is
     the only per-monster interop in the solve.
  2. Insert into a **uniform grid**, cell **48 px** (≥ the largest normal contact distance, 2 × 15 × 1.45 ≈
     43.5).
     - Use a flat hash: `bucket = (cx * 73856093 ^ cy * 19349663) & (Buckets - 1)`, with `Buckets = 4096`,
       `int[] head` and `int[] next`. Zero allocations per frame.
     - A body with a radius above 24 px (bosses, big uniques) is inserted in every cell its AABB overlaps.
  3. For each monster i, scan its 3 × 3 cells. For each j > i (dedupe big bodies with a per-frame stamp), with
     `D = (ri + rj) * ContactFactor` and `d = |pi − pj|`: if `d < D`, take
     - the overlap fraction `f = (D − d) / D`, ignored when `f < DeadZone`;
     - the normal `n = (pi − pj) / d` (if `d < 0.01`, a deterministic fallback from the index parity);
     - the mass shares `si = mj / (mi + mj)` and `sj = mi / (mi + mj)`;
     - `push_i += n · f · SepSpeed · si` and `push_j −= n · f · SepSpeed · sj`;
     - and, for **blocking**, `block_i += n · f` and `block_j −= n · f`.
  4. Store `CrowdPush` (px/s, clamped to `MaxPushFactor × Speed + 20`) and `CrowdBlock` on each Enemy.
- **Mass `m`:** `r² × (1 + 3 × KnockbackResistance)`. **Immovable** (infinite mass: pushes others, gets no push):
  bosses (`IsBoss`), frozen monsters, and a `ChargerEnemy` while `IsCharging`. It plows through the pack, and
  monsters in the lane get pushed aside with the full share. Dead monsters (`Health.IsDead`) and replicas don't
  take part.
- **Starting constants** (tune only with the metrics below):
  - `ContactFactor = 0.9`: 10 % overlap is free. That is the "may overlap slightly".
  - `DeadZone = 0.04`, so a pack at rest reaches exactly zero velocity and I2's skip applies.
  - `SepSpeed = 160 px/s` at full overlap.
  - `MaxPushFactor = 1.0`.
- **Use in `Enemy._PhysicsProcess`.**
  - Main branch:
    1. `v = DesiredVelocity`.
    2. **Queueing:** if `CrowdBlock` is non-zero, let `b = CrowdBlock.Normalized()`, `k = clamp(|CrowdBlock|, 0,
       1)`. If `v · b < 0` (walking into the crowd), `v −= b · (v · b) · k`. Monsters stop pressing into the one
       in front instead of compressing the crowd. Without this, a crowd converging on a player compresses until
       push = walk speed (overlap ≈ speed × τ, about 16 px for a Slime: too much).
    3. `Velocity = v + _knockback + CrowdPush`.
  - Dormant and Frozen branches: `Velocity = _knockback + CrowdPush` for Dormant (sleeping packs settle
    overlaps, then go idle), and `_knockback` only for Frozen (immovable).
  - Separation never wakes a pack.
  - `IsTouching` and contact damage are unchanged.
- **Physics masks.** In `Enemy._Ready`, clear the EnemyFaction bit for non-boss monsters
  (`CollisionMask &= ~CollisionLayers.EnemyFaction`), behind `MonsterCrowd.SoftSeparation` (static, default
  true).
  - Do it in code rather than editing 8 `.tscn` files, so the bench A/B flag can restore bodies.
  - **Keep the `.tscn` masks as they are**, so a revert is one commit.
  - Bosses already have no EnemyFaction bit.
- **Walls and hazards:** untouched. Separation is a velocity that goes through `MoveAndSlide` against World |
  Hazard, so it can't push a monster through a wall.
  - In a corridor, the wall absorbs the push and the queueing term stops the press. Chokepoints form queues, as
    today, with slight overlap.
  - The flow field is untouched: `DirectionToTarget` still decides where each monster wants to go, and
    separation only bends it.
- **Bench flag:** PerfBench `--crowd=bodies` sets `SoftSeparation = false`. That restores the mask bit and skips
  the solve, for an A/B **in one build**: feel metrics and captures side by side. It is kept as a bench and debug
  switch only, not a player setting.
- **Co-op:** host-authoritative. Determinism doesn't matter, and clients only see snapshot positions. Host CPU is
  what matters: the solve is O(n) with a constant ~9 cells. Replicas skip it.

**Probes:** `Perf.K.Crowd` around `Solve()`.

**Measure (performance)**
- PerfBench Rift ×3 (soft) against `--crowd=bodies` ×3, same build, interleaved:
  - `MoveSlide` ≤ 4 µs per call (from 13.7–14.9);
  - Rift frame avg −3 ms or better;
  - physics pairs ≤ 50 (from 847);
  - `Crowd` solve ≤ 0.3 ms/frame at about 374 monsters.
- PerfBench reality ×3: awake avg −1.5 ms or better; steady not worse (packs at rest must reach zero velocity:
  physics active objects in the steady phase stay ≤ 10).
- CoopBench Rift and reality ×1 each: the host's physics step down ≥ 25 % against the agent-2 "before".
  Bandwidth within ±10 %: snapshots don't change, positions do.

**Measure (feel): its own validation**
- **New PerfBench mode `--mode=crowd`** (headless).
  - The Rift at run-time 350 with the player standing (IgnoreDamage). Then a cavern T10 seed 7 with every pack
    woken (the awake phase), run with soft and with `--crowd=bodies`.
  - Every 30 frames it records:
    - (a) the median and p95 of each monster's nearest-neighbour distance divided by its contact distance
      `ri + rj`;
    - (b) the share of pairs overlapping by more than 25 % of `ri + rj`;
    - (c) the monsters inside contact radius of the player (`IsTouching`) per sample;
    - (d) the monsters within 120 px of the player.
  - Also in the cavern: the time from waking to the first contact, and to 20 contacts (flow through
    chokepoints).
- **Accept bands (soft against bodies):**
  - (a) median ≥ 0.8;
  - (b) ≤ 10 %;
  - (c) mean ≤ +20 %, which drives contact damage taken;
  - (d) ≤ +25 %;
  - the time to the first contact within ±15 %.
  - Outside a band: tune `ContactFactor`, `SepSpeed` and the queueing first; don't loosen the bands.
- **AutoplayBot, before (bodies build, or the flag) and after:**
  - Survival: `autoplay.sh --runs=6 --class=warrior` and `--class=arcanist`. Compare the survival time, kills,
    `dmgTaken` and the contact share of "damage by source": medians within ±15 %.
  - Reality: `autoplay.sh --mode=reality --class=warrior --tier=10 --runs=6`, and `--class=rogue`. The `ttk`
    per rarity stays within ±10 %, `dmgTaken` within ±15 %, the clears are the same.
  - AoE skills hit more monsters when they overlap more, so a ttk *drop* for area builds is expected and
    reported, not hidden.
- **Density captures** (windowed, Xvfb/lavapipe), soft against bodies, same build and flag. Save them to
  `docs/devlog/screenshots/perf-crowd-{bodies,soft}-*.png`:
  1. the Rift at run-time 350 after a 1800-frame fill, camera on the player (the ring of monsters);
  2. a cavern chokepoint: the reality seed 7, the player placed 6 tiles past the narrowest corridor cell on the
     flow-field path from the largest pack (the bench picks it from the layout: the minimum
     `FlowField`-reachable corridor width). Wake the pack and shoot at +2 s and +4 s;
  3. a Brute charging through a Slime pack, captured mid-charge (the lineup scene with a charger forced into
     `Charge`).
  - Check by eye that the overlapping voxel models don't visibly z-fight or merge into a blob at the default
    zoom: the models are wider than their collision circles.
- **Tests:**
  - The full suite. Pay attention to `MovementSmokeTest`, `RunSmokeTest`, `EndgameSmokeTest` (monster movement,
    packs, gates), `CoopSmokeTest` and `CoopRealityTest`.
  - **New check** (RunSmokeTest) `CROWD-SOFT-SEPARATION`:
    - 30 Slimes spawned on one point spread out to a median nearest-neighbour distance ≥ 0.8 × contact within
      60 frames;
    - none ends inside a wall of the arena;
    - a boss in the middle is never displaced by them;
    - with the player standing still, the Slimes at rest reach `Velocity == Vector2.Zero` within 180 frames.
  - **New check** `CROWD-REGISTRY-CLEAN`: after a pack wipe and `QueueFree`, the registry count equals the live
    `Enemy` count in the tree, and no entry is invalid.
- **Risk: high, design-wise.**
  - Crowds flow differently around corners and at chokepoints.
  - Denser rings around the player mean more contact damage and more AoE hits: a balance shift.
  - Splitter children and edge spawns that start overlapped now separate over a few frames instead of
    snapping.
  - Owner playtest required. The bench flag keeps the A/B one switch away.
- **Rollback:** revert the commit. The masks were never edited in the scenes.

### I13. C9b: registry users (TargetFrame, RunDirector, RiftMinimap)

- **Change:** replace the per-frame `GetNodesInGroup(EnemyGroup)` scans with `MonsterCrowd.All` (a
  `ReadOnlySpan` or indexer over the list):
  - `scripts/UI/TargetFrame.cs` `_Process`;
  - `scripts/Run/RunDirector.cs` `_PhysicsProcess` (the `alive`/`_rangedAlive` count) and `Leash`;
  - `scripts/UI/RiftMinimap.cs`.
  - The `alive` count must keep today's meaning. The group also contains `TargetDummy`, which is never in the
    Rift: assert that, or count dummies separately.
  - When needed, TargetFrame uses the positions cached by the last `Solve`.
  - **Not changed (proposal only):** `CombatQuery.NearestHostile` and `HostilesInRadius` through the grid. Those
    groups include `TargetDummy` and the players, so the registry would have to cover them. It's a separate,
    medium-risk step, and it's what performance.md's co-op section asks for. Write it as the next proposal, with
    the CoopBench numbers that motivate it.
- **Measure:** PerfBench Rift ×3. **Accept:** `TargetFrame` + `Director` ≤ 0.2 ms/frame together (from
  ~0.8).
- **Tests:** the full suite. `RiftProgressionTest` and `RunSmokeTest` exercise the director's caps.
- **Risk:** medium-low: freed monsters must leave the list, and `CROWD-REGISTRY-CLEAN` covers that.
  **Rollback:** revert.

### I14. Wrap-up

- Re-run the whole I0 baseline table at HEAD, and fill the "expected total" table in section 4 with the measured
  values.
- Write the docs (section 5) and a journal entry. Propose the CLAUDE.md lines, but don't edit CLAUDE.md.
- 18/18 PASS.

---

## 3. Visual comparison protocol (C3, C4, C7, C11, crowd)

- **Renderer:** Forward+ on lavapipe:
  `VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run -a -s "-screen 0 1280x720x24" "$GODOT_BIN"
  --path . --rendering-driver vulkan --resolution 1280x720 --fixed-fps 60 res://scenes/tools/PerfBench.tscn --
  --shots=<dir> ...`.
  - Compatibility/opengl3 has no shadows, glow or DOF in 4.1, so **never** use it for C7 or C4.
- **PerfBench `--shots` set.** Deterministic: fixed seed and tier, packs asleep, no deaths, no debris. Wait 45
  frames after each teleport so the DOF and lights settle.
  - **Reality `--layout=bone_warrens --seed=7 --tier=10`:**
    1. `reality-entrance`: the player at the spawn.
    2. `reality-behind-wall`: the player on the first floor cell directly north (screen-up) of a wall run of
       ≥ 3 cells, so the wall sits between the camera and the player. This tests the dither and `player_pos`.
    3. `reality-liquid`: the camera on the liquid cell nearest the spawn, if the layout has liquid. Otherwise use
       RunCapture `--only=caves` for the Drowned Grotto.
    4. `reality-arena`: the boss arena entrance (the gate).
    5. `reality-overview`: Camera2D zoom 0.5 at the map centre (the most terrain and chunk seams on screen).
  - **Rift `--mode=rift`:**
    6. `rift-centre`: `Arena.Center` before any monster spawns.
    7. `rift-edge`: the arena's south wall in view.
    8. `rift-lineup`: `--lineup`. Every enemy scene and every player class, dormant, in a row 48 px apart, at the
       centre.
- **Plus RunCapture:** `--only=caves` (each cavern style: entrance, liquid, boss arena entrance, overview) and
  `--only=town` (the hideout quarters and overview). Their seeds are deterministic.
- **Compare:** `compare -metric AE -fuzz 2% before.png after.png diff.png` (ImageMagick is installed; so are
  PIL 12.3 and numpy 2.5 for a mean-ΔE script if needed).
  - **"Identical"** means AE ≤ 0.5 % of pixels at fuzz 2 % (lavapipe is deterministic, but the flicker lights use
    `_time`, so a small budget is needed).
  - For C7 and C4 the result is a **side-by-side for the owner**, not a pass/fail.
- **Storage:** shots live in `user://bench/shots/<label>/`. Only the side-by-sides the docs cite go to
  `docs/devlog/screenshots/` (keep the repo small).

## 4. Expected total (baseline → target at the end of I13)

Baseline from the research (medians). The targets are the sum of the measured or prototyped gains, discounted for
overlap. Agent 2 fills the "measured" column in I14.

| Scene / metric | Baseline | Target | Main contributors |
|---|---|---|---|
| Rift headless, ~374 monsters: frame avg / p99 | 14.1–15.0 / 22 ms | ≤ 8.5 / ≤ 13 ms | C1b −4, C6 −1.8, C9b −0.6 |
| Rift headless: physics scripts / step | 9.7–10.4 ms | ≤ 4.5 ms | C1b, C6 |
| Rift headless: MoveAndSlide per monster | 13.7–14.9 µs | ≤ 4 µs | C1b |
| Rift headless: managed alloc | 80 KB/frame | ≤ 35 (≤ 20 with C13a) | C5 |
| Rift forcevoxel (voxel CPU): frame avg | 18.7–18.9 ms | ≤ 10 ms | the above + C2 |
| Rift forcevoxel: alloc | 310 KB/frame | ≤ 40 KB/frame | C2, C5 |
| Reality steady (245, asleep): frame avg | 5.1 ms | ≤ 3.3 ms | C1a −1.3, C6 −1.1 |
| Reality awake (swarm): frame avg / max | 7.8 / 274 ms | ≤ 5.0 / ≤ 120 ms | C1b, C6, C8, C5/C2 alloc |
| AutoplayBot reality T10: max frameMs | 797–2648 ms | ≤ 200 ms (container GC caveat) | C8, C2, C5 |
| Cataclysm (377–416 victims): worst frame | 95–118 ms | ≤ 25 ms, over ≤ 10 frames | C10 |
| Rift windowed: primitives | 7.46 M | ≤ 1.3 M | C3 |
| Rift windowed: draw calls / objects | 999 / 1375 | ≤ 250 / ≤ 650 | C7, C4 (C3 adds chunks) |
| Rift windowed: lavapipe frame (relative) | 638 ms | ≤ 280 ms | C3, C7, C4 |
| Cavern T10 windowed: primitives | 5.54 M | ≤ 0.5 M | C3a + C3b |
| Cavern T10 windowed: draw calls (asleep / awake) | 200 / 634 | ≤ 120 / ≤ 300 | C4, C7 |
| Cavern T10 windowed: lavapipe frame | 529 ms | ≤ 260 ms | C3 |
| VoxelStage `_Process` | 30–70 µs (327 µs if chunked naively) | ≤ 30 µs | C11 |
| CoopBench Rift host (party of 4): physics step | the I0 value | −25 % or better | C1b, C6, C9b |

## 5. Docs to update

- **`docs/design/performance.md`:**
  - New top-level section `## Frame budget pass (2026-10-xx): the 2D checklist`.
    - The method: PerfBench and perf_bench.sh, A/B rules, lavapipe caveats.
    - The I0 baseline.
    - **One subsection per increment:** scenario, before/after table, cause, fix, what was rejected (the
      off-screen VoxelActor early-out, `MaxSlides` 2 as superseded by C1b, the PSSM splits, texture compression,
      pooling, threading; the list is in research §6).
  - New **budgets**:
    - Rift 374 monsters ≤ 8.5 ms headless;
    - alloc ≤ 40 KB/frame in play;
    - no mass-kill frame > 25 ms;
    - terrain primitives ≤ 1.5 M per frame;
    - crowd solve ≤ 0.3 ms.
  - Update the Status line.
  - **Design decisions flagged for the owner:** the Cataclysm ripple; soft separation (with its feel numbers);
    the shadow mode (with its captures); the proposed spread of the StartFinalBoss clear; the CombatQuery grid.
- **`docs/design/enemies.md`:**
  - "Shared behaviour", the physics bullet: rewrite it for soft separation. Cover the mask, the grid, the
    constants, mass and immovables (bosses, frozen, charging Brutes), queueing, the walls, the
    `--crowd=bodies` A/B flag, and the feel numbers.
  - Keep the "pass through the player" rule.
- **`docs/design/run.md` -> "Pickups":** the Cataclysm resolves as an outward ripple over 8 frames, and the toast
  count is the snapshot.
- **`docs/design/art-pipeline.md` -> "Voxels" / "Cave walls":** the vertex-colour single surface, the alpha
  emissive flag, the LOD import setting, and terrain chunks (12 m, with the rock chunked per marching-squares
  origin).
- **`docs/design/menus.md` -> "Loading screen":** the GC collect behind it.
- **`docs/devlog/journal.md`:** one entry per agent: what changed, the before/after headline numbers, and the
  screenshot references (shadow, voxel colour, crowd side-by-sides).
- **CLAUDE.md, proposed lines only (the parent or the owner decides; don't edit):**
  - Under **Scripts**: "`scripts/perf_bench.sh --mode=rift|reality|crowd [--reps=3] [--window] [--forcevoxel]`
    runs `scenes/tools/PerfBench.tscn` (fixed seed; probes, alloc per probe, GC log, render counts) and prints
    `PERF SUMMARY` lines; `--window` uses Xvfb + lavapipe Forward+ (counts exact, timings relative);
    `--shots=<dir>` saves the fixed screenshot set used for visual A/B; `--crowd=bodies` restores
    monster-vs-monster bodies for a crowd A/B. Results in `user://bench/perf_<mode>.txt` and `history/`. Every
    performance change is measured with it before and after (`docs/design/performance.md`)."
  - Under **Graphics**: "Voxel meshes are one surface with vertex colours and one shared material
    (`VoxelLibrary`); don't read colours from surface materials. Terrain is chunked (12 m); `player_pos` is a
    global shader uniform (`[shader_globals]`)."

## 6. Risks at a glance

| Item | Risk | Guard |
|---|---|---|
| C1b soft separation | Feel and balance (crowd flow, contact damage, AoE density) | Bench `--crowd=bodies` A/B, crowd metrics with bands, bot ttk/survival, density captures, owner playtest |
| C4 voxel colours | Colour space, LOD bleed, code that read surface materials | Recorded palette/debris tests, LOD A/B, side-by-sides; stop on any shift |
| C7 shadow | Softer shadows, acne, max-distance edge | Captures for the owner; `Parallel2Splits` fallback |
| C3b rock chunks | Seams | Same-geometry construction, seam-focused diff |
| C6 child ticks | Order within a frame; missed early-return branches | Ticks before every branch; new check incl. replicas |
| C10 ripple | Kills land ≤ 0.13 s late; power-up rule | `_nuking` per batch; new check |
| C11 globals | Global missing in exports | Runtime `GlobalShaderParameterAdd` fallback; check an export |
| C4 pre-flight | EasyPixel fetch blocked, or a non-reproducible export | Pre-flight before I9; stop if `assets/voxels` isn't byte-identical |
