#!/bin/bash
# ab.sh <before_label> <after_label> <reps> <parts...>: interleaved A/B in the main tree.
# "after" = the uncommitted changes in /home/user/arpg_game; "before" = HEAD (changes stashed).
S=/tmp/claude-0/-home-user/01f50d60-7603-5568-9cde-d97b2c383e48/scratchpad/impl
b=$1; a=$2; n=$3; shift 3
cd /home/user/arpg_game
stashed=0
trap '[ $stashed = 1 ] && git stash pop -q' EXIT
trap 'exit 1' TERM INT HUP
for r in $(seq 1 $n); do
  git stash -q || exit 1; stashed=1
  R0=$r R1=$r $S/batch.sh $b "$@" >/dev/null 2>&1
  git stash pop -q || { echo STASH POP FAILED; exit 1; }; stashed=0
  R0=$r R1=$r $S/batch.sh $a "$@" >/dev/null 2>&1
  echo "rep $r done $(date +%T)"
done
