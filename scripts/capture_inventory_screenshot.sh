#!/usr/bin/env bash
# Run the inventory-capture scene WINDOWED and save a devlog screenshot.
# Usage: ./scripts/capture_inventory_screenshot.sh
#
# Deliberately does NOT pass --headless: headless uses the dummy renderer, so
# GetViewport().GetTexture().GetImage() comes back empty. A real window is required.
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ -x "${HOME}/.dotnet/dotnet" ]]; then
  export DOTNET_ROOT="${HOME}/.dotnet"
  export PATH="${HOME}/.dotnet:${PATH}"
fi

# Canonicalised for the same reason run_headless.sh does it: invoking Godot through
# a symlink makes it resolve GodotSharp relative to the symlink's directory, and C#
# dies at startup. See CLAUDE.md.
GODOT="${GODOT_BIN:-${HOME}/Applications/Godot_mono.app/Contents/MacOS/Godot}"
GODOT="$(python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "${GODOT}" 2>/dev/null || echo "${GODOT}")"
if [[ ! -x "${GODOT}" ]]; then
  echo "error: no Mono/.NET Godot build found at ${GODOT}." >&2
  exit 1
fi

exec "${GODOT}" --path "${PROJECT_DIR}" "res://scenes/tools/InventoryCapture.tscn" "$@"
