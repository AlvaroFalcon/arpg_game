#!/usr/bin/env bash
# Two windowed instances of the game on this PC, for trying co-op alone:
# the first hosts (lobby open on the port), the second joins 127.0.0.1.
# Press Start in the host window once the second player shows in the lobby.
#
# Usage: ./scripts/coop_local.sh [port] [extra godot args...]
#   COOP_CLIENTS=3 ./scripts/coop_local.sh   # host + three clients (four players)
#
# The joining windows play a throwaway copy of your profile (--profile-readonly):
# both windows read the same save, and only the host's results are written back.
# Both windows use your active character; to see two different characters,
# change the active one in Play, start the host, then change it again before
# the client window opens (or give COOP_DELAY a few more seconds).
# See docs/design/multiplayer.md -> "Testing it yourself".
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PORT="${1:-7777}"
shift || true
CLIENTS="${COOP_CLIENTS:-1}"
DELAY="${COOP_DELAY:-4}"

if [[ -x "${HOME}/.dotnet/dotnet" ]]; then
  export DOTNET_ROOT="${HOME}/.dotnet"
  export PATH="${HOME}/.dotnet:${PATH}"
fi

canonicalise() {
  python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "$1" 2>/dev/null || echo "$1"
}

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
  for candidate in "${HOME}/Applications/Godot_mono.app/Contents/MacOS/Godot" "/Applications/Godot_mono.app/Contents/MacOS/Godot"; do
    if [[ -x "${candidate}" ]]; then
      echo "${candidate}"
      return 0
    fi
  done
  return 1
}

GODOT="$(canonicalise "$(find_godot || true)")"
if [[ -z "${GODOT}" ]] || ! "${GODOT}" --version 2>/dev/null | grep -q mono; then
  echo "error: no Mono/.NET Godot 4 binary found (set GODOT_BIN to the real binary, not a symlink)." >&2
  exit 1
fi

# New or edited C# only reaches the game after a build (CLAUDE.md -> Toolchain).
(cd "${PROJECT_DIR}" && dotnet build ArpgGame.csproj -nologo -v q >/dev/null)

echo "host:   window 1, lobby on port ${PORT}"
"${GODOT}" --path "${PROJECT_DIR}" --position 40,60 --resolution 960x540 "$@" -- --coop-host="${PORT}" &
pids=($!)
sleep "${DELAY}"
for ((i = 1; i <= CLIENTS; i++)); do
  echo "client: window $((i + 1)), joining 127.0.0.1:${PORT}"
  "${GODOT}" --path "${PROJECT_DIR}" --position $((40 + 980 * (i % 2))),$((60 + 300 * (i / 2))) --resolution 960x540 "$@" \
    -- --coop-join="127.0.0.1:${PORT}" --profile-readonly &
  pids+=($!)
done

echo "close any window (or Ctrl+C here) to stop them all"
trap 'kill "${pids[@]}" 2>/dev/null || true' INT TERM EXIT
wait -n "${pids[@]}" 2>/dev/null || true
