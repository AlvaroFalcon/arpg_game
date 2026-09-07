#!/usr/bin/env bash
# Run this Godot 4 (Mono/.NET, C#) project headless (no GUI), for agents/CI.
# Usage: ./scripts/run_headless.sh [extra godot args...]
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Godot's C# tooling needs .NET 6 specifically (matches the Godot.NET.Sdk/net6.0
# target generated for this project). Homebrew's dotnet (if installed) is often a
# newer major version with an incompatible layout for Godot's hostfxr resolver, so
# force the known-good SDK here rather than trusting whatever the caller's PATH has.
if [[ -x "${HOME}/.dotnet/dotnet" ]]; then
  export DOTNET_ROOT="${HOME}/.dotnet"
  export PATH="${HOME}/.dotnet:${PATH}"
fi

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
    "${HOME}/Applications/Godot_mono.app/Contents/MacOS/Godot"
    "/Applications/Godot_mono.app/Contents/MacOS/Godot"
    "/Applications/Godot.app/Contents/MacOS/Godot"
    "${HOME}/Applications/Godot.app/Contents/MacOS/Godot"
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
  echo "  or install the Mono/.NET build to ~/Applications/Godot_mono.app." >&2
  exit 1
fi

if ! "${GODOT}" --version 2>/dev/null | grep -q mono; then
  echo "warning: ${GODOT} is not a Mono/.NET build — this project uses C# and needs the mono build to run." >&2
fi

if [[ ! -f "${PROJECT_DIR}/project.godot" ]]; then
  echo "error: no project.godot found at ${PROJECT_DIR}." >&2
  echo "  the Godot project hasn't been created yet — this script is ready for when it is." >&2
  exit 1
fi

exec "${GODOT}" --headless --path "${PROJECT_DIR}" "$@"
