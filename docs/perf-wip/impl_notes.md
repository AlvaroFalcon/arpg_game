# perf impl phase A — running notes
Branch claude/confident-edison-xy8knx, base 1b22757.
Helpers: scratchpad/impl/hb.sh (headless one-off), scratchpad/impl/batch.sh <label> <parts: pb fv win winrift winreality rb ex ap coop cap>
Outputs in scratchpad/impl/<label>/.

## I0 (tooling)
- Perf.cs, PerfBench.cs/.tscn, perf_bench.sh, RiftBench first-ten line, probes in Enemy/VoxelActor/VoxelStage/TargetFrame/RunDirector, HdView.ForceInHeadless.
- RiftBench A/B base vs I0 (3 each): base 14.93*/14.60/14.07 ms (371/374/374 mon), I0 11.84/11.78/13.80 (312/320/374). per-monster 37.6-40.2 vs 36.8-37.9 us. PASS (within noise).
- Note: VBCSCompiler survives `dotnet build-server shutdown` sometimes -> pkill "[V]BCSCompiler".
- Tests: running...
- Tests I0: 18/18 PASS. Committed+pushed 02c3f09.
- Baseline batch: scratchpad/impl/batch.sh base win pb fv rb ex cap coop ap (outputs impl/base/). Uncommitted tweak in main tree: PerfBench lineup offsets (commit with baseline docs).
## Drafts
- Worktree /home/user/arpg_wt (branch perf-wt, local only): wip commits I1..I8 (+ I7a terrain timer). To land: cherry-pick each into main tree, measure, test, add docs, commit.
- I2 deviation: settle frames (first 10 physics frames always slide) so spawn overlaps still depenetrate.
- I1: tests 18/18 PASS (rerun after restart). Committed ef5d81a, pushed.
- After restart: container ~2.2x slower (I1 Rift 13.7 -> 31.8 ms same code). Using interleaved A/B (impl/ab.sh before after reps parts; stash-based, trap pops stash). If main tree has stash@{0} after a crash: git stash pop.
- I2: committed ec41cbb, pushed=1
- abp.sh <patch> before after reps parts: A/B reverting only <patch> (if a crash leaves it reversed: git apply <patch>).
- I3: committed 13d055e, pushed=1
  I3 deviation: anim/frame dirty split (VoxelLibrary.MeshesFor) beyond wip; test waits on ProcessFrame (off-by-one fix).
- Resumed after rate limit + worker restart: I3 refinement confirmed in 13d055e. Starting I4.
- I4: pbscreen A/B done (i4b/i4a). Bot now enters via LoadingScreen.Go (AutoplayBot.Reality.cs). apr A/B running (APRUNS=3).
- I4: blocking collect cost 2.3-4.1 s on 330-487 MB heaps (bot realities 2,3) -> switched to GC.Collect(2, Forced, blocking:false); re-running apr A/B (i4b/i4a apr_1). Blocking-version bot: before max 844/214/1847, after 147/400/111.
- I4 bg-GC bot A/B in i4old2 (before max 177/472/1441, after 396/472/55). Adding [BOT] spike attribution and re-running.
- I4 CoopRealityTest gchandle crash 3/10 with bg collect; testing 10 runs with collect reverted (if crash: git apply /tmp/claude-0/-home-user/01f50d60-7603-5568-9cde-d97b2c383e48/scratchpad/impl/i4_collect.patch)
- I4 decision: collect REVERTED (guest crash 3/10 vs 0/12); committing tools only (viascreen, bot via LoadingScreen + spike log, [LOADING] ready log).
- I4: committed 254f84e, pushed=1
- I5: cherry-picked, StatsSmokeTest PASS; A/B i5b/i5a running (ab.sh, stash-based)
- I5: committed c403c39, pushed=1
- I6 cherry-picked; A/B fv x2 + winreality1 x1 running (i6b/i6a)
- I6 measured: fv VoxStage 88.7->44.7us; windowed reality 81-88 -> 75-81us; reality shots AE0 vs base; pck has shader_globals. Next: town capture i6a, suite.
- NOTE: never put the Godot path (GODOT_BIN export) in the same shell command as batch.sh/ab.sh: its pkill [G]odot_v4.1.1 kills that shell. I6 fallback fixed: ProjectSettings.HasSetting instead of editor-only GlobalShaderParameterGetList.
- I6: town shots 14/14 AE0 vs base (i6a/cap_town); full suite 18/18 PASS; docs written; commit msg ready in impl/msg.txt. STATE: I6 changes UNCOMMITTED in main tree (Bash classifier outage). Next step: impl/commit.sh I6 impl/msg.txt, then I7 (cherry-pick 1ece0b0 terrain timer + 0a1acd6).
- HANDED BACK EARLY (2026-10-25 ~06:40): 8 Bash calls without a classifier verdict. I6 is green (18/18, shots AE 0) but UNCOMMITTED; msg.txt final. Resume: run impl/commit.sh I6 impl/msg.txt (it commits scripts/scenes/docs/resources/project.godot and pushes), then I7, I8, journal, worktree cleanup.
- I7 plan: before = I6 + I7a timer (commit timer together with I7, measure before via abp with chunk patch). Base windowed counts: Rift 7.36M prims, cavern 5.50M. Compare shots vs base/shots_a, base/cap_caves, base/cap_town (lavapipe here reproduces base AE 0).
- I6: committed 0fbaf11, pushed=1
- I6 committed by main session 0fbaf11. Resuming I7.
