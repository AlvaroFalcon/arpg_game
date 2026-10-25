#!/bin/bash
# usage: batch.sh <label> <parts...>   parts: pb fv win rb ex ap coop cap   (writes impl/<label>/)
S=/tmp/claude-0/-home-user/01f50d60-7603-5568-9cde-d97b2c383e48/scratchpad/impl
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH GODOT_BIN=/opt/godot/Godot_v4.1.1-stable_mono_linux_x86_64/Godot_v4.1.1-stable_mono_linux.x86_64
G=$GODOT_BIN
U="/root/.local/share/godot/app_userdata/ARPG Game"
P=/home/user/arpg_game
label=$1; shift; O=$S/$label; mkdir -p $O
cd $P
dotnet build ArpgGame.csproj -nologo -v q >/dev/null 2>&1 || { echo BUILD FAILED; exit 1; }
clean() { dotnet build-server shutdown >/dev/null 2>&1; pkill -9 -f "[V]BCSCompiler"; pkill -9 -f "[G]odot_v4.1.1"; pkill -9 -f "[X]vfb"; sleep 1; }
# wait for a marker in a file or process exit
waitfor() { local pid=$1 file=$2 pat=$3 to=$4; for i in $(seq 1 $to); do sleep 1; grep -qE "$pat" "$file" 2>/dev/null && break; kill -0 $pid 2>/dev/null || break; done; sleep 1; kill -9 $pid 2>/dev/null; clean; }
pb() { # name mode reps extra...
  local name=$1 mode=$2 reps=$3; shift 3
  for r in $(seq ${R0:-1} ${R1:-$reps}); do clean; $P/scripts/perf_bench.sh --mode=$mode "$@" > $O/${name}_$r.out 2>&1; cp "$U/bench/perf_${mode}.txt" $O/${name}_$r.txt 2>/dev/null; done; }
for part in "$@"; do case $part in
 pb) pb pb_rift rift 3; pb pb_reality reality 3 ;;
 pbrift) pb pb_rift rift 3 ;;
 pbreality) pb pb_reality reality 3 ;;
 pbscreen) pb pb_screen reality 3 -- --viascreen ;;
 fv) pb pb_fv rift 2 --forcevoxel ;;
 fvreality) pb pb_fvr reality 2 --forcevoxel ;;
 win) for r in a b; do clean; $P/scripts/perf_bench.sh --mode=rift --window -- --shots=$O/shots_$r --lineup > $O/win_rift_$r.out 2>&1; cp "$U/bench/perf_rift.txt" $O/win_rift_$r.txt
       clean; $P/scripts/perf_bench.sh --mode=reality --window -- --shots=$O/shots_$r > $O/win_reality_$r.out 2>&1; cp "$U/bench/perf_reality.txt" $O/win_reality_$r.txt; done ;;
 winrift) for r in a b; do clean; $P/scripts/perf_bench.sh --mode=rift --window -- --shots=$O/shots_$r --lineup > $O/win_rift_$r.out 2>&1; cp "$U/bench/perf_rift.txt" $O/win_rift_$r.txt; done ;;
 winreality) for r in a b; do clean; $P/scripts/perf_bench.sh --mode=reality --window -- --shots=$O/shots_$r > $O/win_reality_$r.out 2>&1; cp "$U/bench/perf_reality.txt" $O/win_reality_$r.txt; done ;;
 rb) for r in 1 2 3; do clean; rm -f "$U/bench/rift_bench.txt"; "$G" --headless --path . --fixed-fps 60 res://scenes/tools/RiftBench.tscn > $O/rb_$r.log 2>&1 & waitfor $! "$U/bench/rift_bench.txt" "RESULT: (PASS|FAIL)" 600; cp "$U/bench/rift_bench.txt" $O/rb_$r.txt; done ;;
 ex) for m in reality rift; do for k in volatile chain; do for r in 1 2 3; do clean; rm -f "$U/bench/explosion.txt"; "$G" --headless --path . --fixed-fps 60 res://scenes/tools/ExplosionBench.tscn -- --mode=$m --kind=$k --count=60 > $O/ex_${m}_${k}_$r.log 2>&1 & waitfor $! "$U/bench/explosion.txt" "RESULT: (PASS|FAIL)" 600; cp "$U/bench/explosion.txt" $O/ex_${m}_${k}_$r.txt; done; done; done ;;
 ap) for spec in "surv_warrior --runs=6 --class=warrior" "surv_arcanist --runs=6 --class=arcanist" "reality_warrior --mode=reality --class=warrior --tier=10 --runs=6"; do
       set -- $spec; n=$1; shift; clean; rm -f "$U/autoplay.log"; "$G" --headless --path . --fixed-fps 60 res://scenes/tools/AutoplayBot.tscn -- "$@" > $O/ap_$n.log 2>&1 & waitfor $! "$U/autoplay.log" "\[BOT\] done" 5400; cp "$U/autoplay.log" $O/ap_$n.txt; done ;;
 coop) clean; rm -f "$U/bench/coop_bench.txt"; "$G" --headless --path . res://scenes/tools/CoopBench.tscn -- --clients=3 --run-time=350 > $O/coop_rift.log 2>&1 & waitfor $! "$U/bench/coop_bench.txt" "RESULT: (PASS|FAIL)" 1500; cp "$U/bench/coop_bench.txt" $O/coop_rift.txt
       clean; rm -f "$U/bench/coop_bench_reality.txt"; "$G" --headless --path . res://scenes/tools/CoopBench.tscn -- --clients=3 --mode=reality --tier=10 > $O/coop_reality.log 2>&1 & waitfor $! "$U/bench/coop_bench_reality.txt" "RESULT: (PASS|FAIL)" 1500; cp "$U/bench/coop_bench_reality.txt" $O/coop_reality.txt ;;
 cap) for only in caves town; do clean; mkdir -p $O/cap_$only; VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run -a -s "-screen 0 1280x720x24" "$G" --path . --rendering-driver vulkan --resolution 1280x720 --fixed-fps 60 res://scenes/tools/RunCapture.tscn -- --only=$only --out=$O/cap_$only > $O/cap_$only.log 2>&1 & waitfor $! $O/cap_$only.log "CAPTURE\] done" 2400; done ;;
 winrift1) clean; $P/scripts/perf_bench.sh --mode=rift --window -- --shots=$O/shots_a --lineup > $O/win_rift_a.out 2>&1; cp "$U/bench/perf_rift.txt" $O/win_rift_a.txt ;;
 winreality1) clean; $P/scripts/perf_bench.sh --mode=reality --window -- --shots=$O/shots_a > $O/win_reality_a.out 2>&1; cp "$U/bench/perf_reality.txt" $O/win_reality_a.txt ;;
 caves) clean; mkdir -p $O/cap_caves; VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run -a -s "-screen 0 1280x720x24" "$G" --path . --rendering-driver vulkan --resolution 1280x720 --fixed-fps 60 res://scenes/tools/RunCapture.tscn -- --only=caves --out=$O/cap_caves > $O/cap_caves.log 2>&1 & waitfor $! $O/cap_caves.log "CAPTURE\] done" 2400 ;;
 town) clean; mkdir -p $O/cap_town; VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/lvp_icd.json xvfb-run -a -s "-screen 0 1280x720x24" "$G" --path . --rendering-driver vulkan --resolution 1280x720 --fixed-fps 60 res://scenes/tools/RunCapture.tscn -- --only=town --out=$O/cap_town > $O/cap_town.log 2>&1 & waitfor $! $O/cap_town.log "CAPTURE\] done" 2400 ;;
 apr) clean; rm -f "$U/autoplay.log"; "$G" --headless --path . --fixed-fps 60 res://scenes/tools/AutoplayBot.tscn -- --mode=reality --class=warrior --tier=10 --runs=${APRUNS:-3} > $O/apr_$R0.log 2>&1 & waitfor $! "$U/autoplay.log" "\[BOT\] done" 5400; cp "$U/autoplay.log" $O/apr_$R0.txt ;;
esac; echo "part $part done $(date +%T)" >> $O/progress; done
echo ALLDONE >> $O/progress
