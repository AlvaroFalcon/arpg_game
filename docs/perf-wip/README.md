# Performance pass: where it stopped and how to continue

The performance effort ran as research → plan → implement. It paused after **I6** because of usage limits. This folder holds everything needed to pick it up again. **Delete the folder once the pass is finished.** Its lasting results go in [`docs/design/performance.md`](../design/performance.md).

| File | What it is |
|---|---|
| [`perf_research.md`](perf_research.md) | The research: baseline numbers, the owner's 2D-optimisation list evaluated against this game, extra techniques, ranked candidates C1–C13 |
| [`perf_plan.md`](perf_plan.md) | The plan: increments I0–I14 with files, acceptance thresholds, measurements, risks and the visual comparison protocol |
| [`impl_notes.md`](impl_notes.md) | The implementer's running notes for I0–I6: numbers, deviations, machine notes |
| `0001-wip-I7a-terrain-timer.patch`, `0002-wip-I7.patch`, `0003-wip-I8.patch` | Untested drafts of I7/I8, from the implementer's scratch worktree (`git format-patch` on top of I6's wip) |
| `I7-in-progress-main-tree.patch` | The uncommitted I7 work that was in the main tree when it was stopped. It is untested. |
| `helpers/` | The implementer's A/B and capture scripts. Their paths point at an old scratchpad; adapt them before use. |

## Owner decisions (binding)

- Scope: all candidates **C1–C8**, plus C9/C11 where the plan ties them in.
- Crowd: **soft separation**. Remove monster-to-monster body collision and replace it with a cheap separation force over a spatial grid. Monsters may overlap slightly.
- Cataclysm: **spread its kills over about 6–10 frames**. A kill may land up to about 0.15 s later.
- The sun shadow (C7) changes how the game looks. Show the owner before/after captures before committing.

## Done (on `main` once the PR merges)

| Step | Commit | Before → after |
|---|---|---|
| I0 PerfBench, `perf_bench.sh`, gated probes, baseline | 02c3f09, 70b7cd3 | — |
| I1 allocation-free `Stat.ComputeFormula` | ef5d81a | Rift allocations 80 → 20 KB/frame |
| I2 idle monsters skip `MoveAndSlide` (except their first 10 physics frames) | ec41cbb | reality steady 11.8 → 9.0 ms; active bodies 269 → 1 |
| I3 voxel actor meshes from a per-SpriteFrames table, updated on signals | 13d055e | `VoxelActor` 14.9 → 5.1 µs per actor, 0 allocations |
| I4 GC behind the loading screen | 254f84e | **Rejected.** A forced collect crashed co-op guests (a GodotSharp finalizer race). Only the tools were kept. |
| I5 a monster ticks its own Stats/Health/Ailments | c403c39 | Rift per-monster physics scripts −20 %, reality −40 % |
| I6 `player_pos` as a global shader uniform | 0fbaf11 | `VoxelStage` 88.7 → 44.7 µs. The ≤ 30 µs target was missed. Captures are identical. |

## Remaining, in order

1. **I7, C3a: chunk the floor, liquid bed, wall and liquid surface MultiMeshes** (12 m chunks).
   - Start from `0001`+`0002`, or from `I7-in-progress-main-tree.patch`, which is newer.
   - Accept: Rift ≤ 1.5 M primitives, cavern ≤ 0.8 M, draw calls up by at most 30 %, no seams.
2. **I8, C3b: chunk the cave rock** per marching-squares origin. Start from `0003`. Accept: cavern ≤ 0.5 M primitives, no seams.
3. **I9, C10: the Cataclysm as a ripple** over 8 physics frames. Accept: worst frame ≤ 25 ms, everything dead within 10 frames.
4. **I10, C7: the sun shadow in Orthogonal mode.** Accept: Rift draw calls ≤ 50 % of the value after I8, plus captures for the owner.
5. **I11, C4: one surface and one material per voxel mesh.**
   - This changes `tools/art/voxels.ts`, then the assets are regenerated and re-imported.
   - Pre-flight: an unchanged re-export must be byte-identical.
   - Any colour shift blocks the commit.
6. **I12, C9a + C1b: a monster registry, a 48 px grid and soft separation.**
   - Add a `--crowd=bodies` A/B switch.
   - Accept: `MoveAndSlide` ≤ 4 µs per call, Rift −3 ms, crowd solve ≤ 0.3 ms.
   - The feel must stay within the plan's limits. Check autoplay time-to-kill and survival, take density captures, and re-check co-op.
7. **I13, C9b:** `TargetFrame`, `RunDirector` and `RiftMinimap` use the registry. Accept: ≤ 0.2 ms together.
8. **I14: wrap-up.**
   - Re-run the baseline and fill the expected-total table in `perf_plan.md` §4.
   - Update `performance.md` and `enemies.md` and add a journal entry.
   - Propose the CLAUDE.md lines below.
   - Delete this folder.

## Things learned the hard way

- **Measure A/B on the same machine, interleaved.** After a container restart the machine ran about 2.2× slower headless and about 3.5× slower on lavapipe, so absolute thresholds from the plan only hold as relative changes.
- **Reality monster counts vary** between runs with the same seed (183–339). Compare per monster.
- **Don't force a full GC around scene changes.** It triggers a GodotSharp finalizer crash in co-op guests (`gchandle.is_released()`).
- **In-play gen2 pauses of 100–350 ms remain.** The heap grows about 300 MB per 8-minute reality. The suspects are finalizable `StringName`/`GodotObject` wrappers created per call. This is worth a follow-up after I14.
- **`RenderingServer.GlobalShaderParameterGetList` is editor-only at run time.** Check `ProjectSettings.HasSetting("shader_globals/...")` instead.
- **Any Bash command that contains the Godot binary path is killed by a helper's `pkill -f "[G]odot_v4.1.1"`.** Run such helpers in a separate call.
- **`VBCSCompiler` can survive `dotnet build-server shutdown`.** Kill it with `pkill -9 -f "[V]BCSCompiler"`.
- **Windowed Forward+ runs on lavapipe take about 25 minutes** each in the cloud container.

## Proposed CLAUDE.md lines (not applied)

- `scripts/perf_bench.sh` / `scenes/tools/PerfBench.tscn`: what they measure and their flags (see `perf_plan.md` I0).
- The last four items under "Things learned the hard way".
