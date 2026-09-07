#!/usr/bin/env bash
# Run the movement-capture scene WINDOWED and save a devlog screenshot.
# Usage: ./scripts/capture_devlog_screenshot.sh
#
# This deliberately does NOT pass --headless: headless uses the dummy renderer, so
# GetViewport().GetTexture().GetImage() comes back empty. A real window is required,
# so this briefly opens one. Output lands in docs/devlog/screenshots/.
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ -x "${HOME}/.dotnet/dotnet" ]]; then
  export DOTNET_ROOT="${HOME}/.dotnet"
  export PATH="${HOME}/.dotnet:${PATH}"
fi

GODOT="${GODOT_BIN:-${HOME}/Applications/Godot_mono.app/Contents/MacOS/Godot}"
if [[ ! -x "${GODOT}" ]]; then
  echo "error: no Mono/.NET Godot build found at ${GODOT}." >&2
  exit 1
fi

exec "${GODOT}" --path "${PROJECT_DIR}" "res://scenes/tools/MovementCapture.tscn" "$@"
