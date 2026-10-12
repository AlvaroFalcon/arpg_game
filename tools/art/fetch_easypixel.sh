#!/usr/bin/env bash
# Fetches EasyPixel (https://github.com/AlvaroFalcon/EasyPixel) at a pinned commit
# into tools/art/vendor/easypixel. Only packages/core is used: it is DOM-free, so
# the same draw_grid + Godot export code the editor's MCP server runs can run here
# from plain Node, without launching the Electron app.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
COMMIT="bf08d5d7d09d894026dd9e116cc6fcbc0b1f77b7"
DEST="${HERE}/vendor/easypixel"
if [[ -d "${DEST}/.git" ]] && [[ "$(git -C "${DEST}" rev-parse HEAD)" == "${COMMIT}" ]]; then
  echo "EasyPixel already at ${COMMIT}"
  exit 0
fi
rm -rf "${DEST}"
git clone --quiet https://github.com/AlvaroFalcon/EasyPixel "${DEST}"
git -C "${DEST}" checkout --quiet "${COMMIT}"
echo "EasyPixel checked out at ${COMMIT}"
