#!/usr/bin/env bash
# Builds the C# project, then runs every headless test scene under
# scenes/tests/ and reports PASS/FAIL per scene.
#
# Usage: ./scripts/run_all_tests.sh [name-filter]
#
# Follows the CLAUDE.md rules for judging headless runs:
#  - a run is judged by its positive "RESULT: PASS" line, never by an absence of
#    FAIL lines (a dead C# run prints nothing at all);
#  - output goes to a file that is polled, because scenes touching the
#    NavigationServer can hang at shutdown *after* passing;
#  - leftover headless Godot processes are killed between runs.
set -uo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FILTER="${1:-}"
TIMEOUT_S="${TEST_TIMEOUT_S:-90}"
LOG_DIR="${TEST_LOG_DIR:-${TMPDIR:-/tmp}/pte-tests}"
mkdir -p "${LOG_DIR}"

if [[ -x "${HOME}/.dotnet/dotnet" ]]; then
  export DOTNET_ROOT="${HOME}/.dotnet"
  export PATH="${HOME}/.dotnet:${PATH}"
fi

echo "== building C#"
if ! build_out="$(cd "${PROJECT_DIR}" && dotnet build ArpgGame.csproj -nologo -v q 2>&1)"; then
  echo "${build_out}" | grep -E "error" >&2
  echo "build failed" >&2
  exit 1
fi

pkill -9 -f "Godot.*--headless" 2>/dev/null || true

pass=0
fail=0
failed=()
for scene in "${PROJECT_DIR}"/scenes/tests/*.tscn; do
  name="$(basename "${scene}" .tscn)"
  if [[ -n "${FILTER}" && "${name}" != *"${FILTER}"* ]]; then
    continue
  fi
  log="${LOG_DIR}/${name}.log"
  : > "${log}"
  # Tests that may hang at shutdown mirror their output to
  # user://test_results/<name>.txt (stdout is block-buffered; see CLAUDE.md).
  mirror=""
  for dir in "${HOME}/.local/share/godot/app_userdata/ARPG Game" "${HOME}/Library/Application Support/Godot/app_userdata/ARPG Game"; do
    if [[ -d "${dir}" ]]; then
      mirror="${dir}/test_results/${name}.txt"
      rm -f "${mirror}"
    fi
  done
  "${PROJECT_DIR}/scripts/run_headless.sh" "res://scenes/tests/${name}.tscn" --quit-after 6000 > "${log}" 2>&1 &
  pid=$!
  verdict=""
  for ((i = 0; i < TIMEOUT_S * 4; i++)); do
    if grep -qE "RESULT: (PASS|FAIL)" "${log}" 2>/dev/null; then
      verdict="$(grep -oE "RESULT: (PASS|FAIL)" "${log}" | tail -1)"
      break
    fi
    if [[ -n "${mirror}" ]] && grep -qE "RESULT: (PASS|FAIL)" "${mirror}" 2>/dev/null; then
      verdict="$(grep -oE "RESULT: (PASS|FAIL)" "${mirror}" | tail -1)"
      cat "${mirror}" >> "${log}"
      break
    fi
    if ! kill -0 "${pid}" 2>/dev/null; then
      verdict="$(grep -oE "RESULT: (PASS|FAIL)" "${log}" | tail -1)"
      break
    fi
    sleep 0.25
  done
  sleep 0.5
  kill -9 "${pid}" 2>/dev/null || true
  wait "${pid}" 2>/dev/null
  pkill -9 -f "Godot.*--headless" 2>/dev/null || true
  if [[ "${verdict}" == "RESULT: PASS" ]]; then
    echo "PASS  ${name}"
    pass=$((pass + 1))
  else
    echo "FAIL  ${name}  (${verdict:-no RESULT line}; log: ${log})"
    grep -E ": FAIL" "${log}" | head -5 | sed 's/^/        /'
    fail=$((fail + 1))
    failed+=("${name}")
  fi
done

echo "== ${pass} passed, ${fail} failed"
[[ ${fail} -eq 0 ]]
