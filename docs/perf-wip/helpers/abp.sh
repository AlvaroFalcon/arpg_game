#!/bin/bash
# abp.sh <patch> <before_label> <after_label> <reps> <parts...>
# after = the main tree as it is; before = the tree with <patch> reverse-applied.
S=/tmp/claude-0/-home-user/01f50d60-7603-5568-9cde-d97b2c383e48/scratchpad/impl
p=$1; b=$2; a=$3; n=$4; shift 4
cd /home/user/arpg_game
rev=0
trap '[ $rev = 1 ] && git apply "$p"' EXIT
trap 'exit 1' TERM INT HUP
for r in $(seq 1 $n); do
  git apply -R "$p" || exit 1; rev=1
  R0=$r R1=$r $S/batch.sh $b "$@" >/dev/null 2>&1
  git apply "$p" || { echo APPLY FAILED; exit 1; }; rev=0
  R0=$r R1=$r $S/batch.sh $a "$@" >/dev/null 2>&1
  echo "rep $r done $(date +%T)"
done
