#!/bin/bash
# usage: hb.sh <name> <result file under user://bench> <timeout_s> <scene res path> [bench args...]
S=/tmp/claude-0/-home-user/01f50d60-7603-5568-9cde-d97b2c383e48/scratchpad/impl
export DOTNET_ROOT=~/.dotnet PATH=~/.dotnet:$PATH
G=/opt/godot/Godot_v4.1.1-stable_mono_linux_x86_64/Godot_v4.1.1-stable_mono_linux.x86_64
U="/root/.local/share/godot/app_userdata/ARPG Game"
name=$1; res="$U/bench/$2"; to=$3; scene=$4; shift 4
pkill -9 -f "[G]odot_v4.1.1"; sleep 0.5
rm -f "$res"
cd /home/user/arpg_game
"$G" --headless --path . --fixed-fps 60 "$scene" -- "$@" > $S/$name.log 2>&1 &
pid=$!
for i in $(seq 1 $to); do
  sleep 1
  grep -qE "RESULT: (PASS|FAIL)" "$res" 2>/dev/null && break
  kill -0 $pid 2>/dev/null || break
done
sleep 1; pkill -9 -f "[G]odot_v4.1.1"
cp "$res" $S/$name.res 2>/dev/null
grep -E "BENCH (steady|frame|managed|cataclysm)|RESULT|kill|worst|Kill" $S/$name.res
