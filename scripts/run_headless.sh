#!/usr/bin/env bash
# Run this Godot 4 project headless (no GUI), for agents/CI.
# Usage: ./scripts/run_headless.sh [extra godot args...]
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

find_godot() {
  if [[ -n "${GODOT_BIN:-}" && -x "${GODOT_BIN}" ]]; then
    echo "${GODOT_BIN}"
    return 0
  fi
  for candidate in godot4 godot; do
    if command -v "${candidate}" >/dev/null 2>&1; then
      command -v "${candidate}"
      return 0
    fi
  done
  local mac_candidates=(
    "/Applications/Godot.app/Contents/MacOS/Godot"
    "/Applications/Godot_4.app/Contents/MacOS/Godot"
    "${HOME}/Applications/Godot.app/Contents/MacOS/Godot"
    "${HOME}/Downloads/Godot.app/Contents/MacOS/Godot"
  )
  for candidate in "${mac_candidates[@]}"; do
    if [[ -x "${candidate}" ]]; then
      echo "${candidate}"
      return 0
    fi
  done
  return 1
}

GODOT="$(find_godot || true)"
if [[ -z "${GODOT}" ]]; then
  echo "error: no Godot 4 binary found." >&2
  echo "  set GODOT_BIN=/path/to/Godot, or put 'godot'/'godot4' on PATH," >&2
  echo "  or install Godot.app to /Applications (a copy was seen under ~/Downloads at setup time)." >&2
  exit 1
fi

if [[ ! -f "${PROJECT_DIR}/project.godot" ]]; then
  echo "error: no project.godot found at ${PROJECT_DIR}." >&2
  echo "  the Godot project hasn't been created yet — this script is ready for when it is." >&2
  exit 1
fi

exec "${GODOT}" --headless --path "${PROJECT_DIR}" "$@"
