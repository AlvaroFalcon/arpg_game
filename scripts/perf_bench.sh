#!/usr/bin/env bash
# Frame-budget bench runner — docs/design/performance.md -> "Frame budget pass".
# Builds C#, stops the compiler server (it steals CPU and memory), kills leftover
# bench processes, then runs scenes/tools/PerfBench.tscn N times and prints every
# "PERF SUMMARY" line. Each run is judged by the RESULT line of its result file
# (stdout is block-buffered), and the file is copied to user://bench/history/.
#
# Usage: ./scripts/perf_bench.sh --mode=rift|reality [--reps=3] [--window] [--forcevoxel] [-- extra bench args]
#   headless:  --headless --fixed-fps 60 (simulation only; --forcevoxel adds the voxel view's CPU cost)
#   --window:  Xvfb + lavapipe Forward+ at 1280x720, --fixed-fps 60, defaults --fill=120 --frames=60
#              (draw/object/primitive counts are exact; timings are relative only)
#   extra args go to the bench: --run-time=350 --fill=1800 --frames=1200 --tier=10 --layout=id
#              --seed=7 --tag=name --shots=<dir> --lineup
set -uo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MODE="rift"
REPS=1
WINDOW=0
EXTRA=()
BENCH_ARGS=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --mode=*) MODE="${1#--mode=}" ;;
    --reps=*) REPS="${1#--reps=}" ;;
    --window) WINDOW=1 ;;
    --forcevoxel) BENCH_ARGS+=("--forcevoxel") ;;
    --) shift; EXTRA=("$@"); break ;;
    *) echo "unknown option $1" >&2; exit 2 ;;
  esac
  shift
done

if [[ -x "${HOME}/.dotnet/dotnet" ]]; then
  export DOTNET_ROOT="${HOME}/.dotnet"
  export PATH="${HOME}/.dotnet:${PATH}"
fi

GODOT="${GODOT_BIN:-}"
if [[ -z "${GODOT}" ]]; then
  GODOT="$(command -v godot4 || command -v godot || echo "${HOME}/Applications/Godot_mono.app/Contents/MacOS/Godot")"
fi
# Never run Godot through a symlink (CLAUDE.md): C# would not load.
GODOT="$(python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "${GODOT}")"

USER_DIR=""
for dir in "${HOME}/.local/share/godot/app_userdata/ARPG Game" "${HOME}/Library/Application Support/Godot/app_userdata/ARPG Game"; do
  [[ -d "${dir}" ]] && USER_DIR="${dir}"
done
if [[ -z "${USER_DIR}" ]]; then
  echo "error: no user:// folder for the project yet (run the game once)" >&2
  exit 1
fi

echo "== building C#"
if ! out="$(cd "${PROJECT_DIR}" && dotnet build ArpgGame.csproj -nologo -v q 2>&1)"; then
  echo "${out}" | grep -E "error" >&2
  exit 1
fi
(cd "${PROJECT_DIR}" && dotnet build-server shutdown > /dev/null 2>&1) || true
# A compiler server started by another build may survive the shutdown; it must not share the CPU.
pkill -9 -f "[V]BCSCompiler" 2>/dev/null || true

tag=""
for a in "${EXTRA[@]+"${EXTRA[@]}"}"; do
  [[ "${a}" == --tag=* ]] && tag="${a#--tag=}"
done
result="${USER_DIR}/bench/perf_${MODE}${tag}.txt"
mkdir -p "${USER_DIR}/bench/history"
LOG_DIR="${TMPDIR:-/tmp}/perf-bench"
mkdir -p "${LOG_DIR}"

kill_leftovers() {
  pkill -9 -f "[P]erfBench.tscn" 2>/dev/null || true
  if [[ "${WINDOW}" == 1 ]]; then
    pkill -9 -f "[X]vfb" 2>/dev/null || true
  fi
  sleep 0.5
}

for ((rep = 1; rep <= REPS; rep++)); do
  kill_leftovers
  rm -f "${result}"
  log="${LOG_DIR}/perf_${MODE}${tag}_${rep}.log"
  if [[ "${WINDOW}" == 1 ]]; then
    defaults=(--fill=120 --frames=60)
    VK_ICD_FILENAMES="${VK_ICD_FILENAMES:-/usr/share/vulkan/icd.d/lvp_icd.json}" \
      xvfb-run -a -s "-screen 0 1280x720x24" "${GODOT}" --path "${PROJECT_DIR}" --rendering-driver vulkan \
      --resolution 1280x720 --fixed-fps 60 res://scenes/tools/PerfBench.tscn -- \
      --mode="${MODE}" "${defaults[@]}" "${BENCH_ARGS[@]+"${BENCH_ARGS[@]}"}" "${EXTRA[@]+"${EXTRA[@]}"}" > "${log}" 2>&1 &
    timeout_s=2400
  else
    "${GODOT}" --headless --path "${PROJECT_DIR}" --fixed-fps 60 res://scenes/tools/PerfBench.tscn -- \
      --mode="${MODE}" "${BENCH_ARGS[@]+"${BENCH_ARGS[@]}"}" "${EXTRA[@]+"${EXTRA[@]}"}" > "${log}" 2>&1 &
    timeout_s=600
  fi
  pid=$!
  verdict=""
  for ((i = 0; i < timeout_s; i++)); do
    if grep -qE "RESULT: (PASS|FAIL)" "${result}" 2>/dev/null; then
      verdict="$(grep -oE "RESULT: (PASS|FAIL)" "${result}" | tail -1)"
      break
    fi
    if ! kill -0 "${pid}" 2>/dev/null; then
      break
    fi
    sleep 1
  done
  sleep 1
  kill -9 "${pid}" 2>/dev/null || true
  wait "${pid}" 2>/dev/null
  kill_leftovers
  stamp="$(date +%Y%m%d_%H%M%S)"
  [[ -f "${result}" ]] && cp "${result}" "${USER_DIR}/bench/history/${stamp}_${MODE}${tag}_${rep}.txt"
  echo "== ${MODE}${tag} run ${rep}/${REPS}: ${verdict:-NO RESULT (see ${log})}"
  grep -E "PERF (SUMMARY|SPIKE|GC|ERROR)" "${result}" 2>/dev/null
done
