#!/usr/bin/env bash
# Open this project in the Godot 4 editor (Mono/.NET build) with the correct .NET SDK.
# Usage: ./scripts/run_editor.sh [extra godot args...]
#
# Why this script exists: launching Godot_mono.app from Finder/Dock/`open` gives it a
# minimal environment that does NOT include your shell's PATH, so it can't find the
# .NET SDK installed at ~/.dotnet (a non-standard location — Godot probes
# /usr/local/share/dotnet). The result is a "Failed to load .NET runtime ... hostfxr"
# dialog and no C# support. Launching from here passes the env through explicitly.
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ -x "${HOME}/.dotnet/dotnet" ]]; then
  export DOTNET_ROOT="${HOME}/.dotnet"
  export PATH="${HOME}/.dotnet:${PATH}"
else
  echo "error: no .NET SDK found at ~/.dotnet — C# support will not load." >&2
  exit 1
fi

# Godot locates its bundled GodotSharp API assemblies *relative to the executable
# path it was invoked through*, not relative to the real binary. Invoking it via a
# symlink (e.g. ~/bin/godot -> .../Godot_mono.app/Contents/MacOS/Godot) makes it
# look for ~/bin/GodotSharp/, which does not exist, and C# dies at startup with
# "The specified runtimeconfig.json ... does not exist" followed by a hostfxr
# failure. Same binary, same flags — only the path it was reached by differs.
# So whatever we resolve, canonicalise it before running it.
canonicalise() {
  python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "$1" 2>/dev/null || echo "$1"
}

find_godot_mono() {
  if [[ -n "${GODOT_BIN:-}" && -x "${GODOT_BIN}" ]]; then
    echo "${GODOT_BIN}"
    return 0
  fi
  local candidates=(
    "${HOME}/Applications/Godot_mono.app/Contents/MacOS/Godot"
    "/Applications/Godot_mono.app/Contents/MacOS/Godot"
  )
  for candidate in "${candidates[@]}"; do
    if [[ -x "${candidate}" ]]; then
      echo "${candidate}"
      return 0
    fi
  done
  return 1
}

GODOT="$(find_godot_mono || true)"
GODOT="$(canonicalise "${GODOT}")"
if [[ -z "${GODOT}" ]]; then
  echo "error: no Mono/.NET Godot build found (looked for Godot_mono.app)." >&2
  exit 1
fi

# Guard against the non-mono build: it opens this project without any error, ignores
# every C# script, and strips `script = ExtResource(...)` lines from scenes it saves.
if ! "${GODOT}" --version 2>/dev/null | grep -q mono; then
  echo "error: ${GODOT} is not a Mono/.NET build." >&2
  echo "  Opening this C# project with a non-mono Godot silently breaks it and" >&2
  echo "  corrupts scene files on save. Refusing to launch." >&2
  exit 1
fi

exec "${GODOT}" --editor --path "${PROJECT_DIR}" "$@"
