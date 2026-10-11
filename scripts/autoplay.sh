#!/usr/bin/env bash
# Plays full runs with the autoplay bot, headless and unthrottled, for balance
# work (docs/design/run.md -> "Tuning"). Extra args go to the bot:
#   ./scripts/autoplay.sh --skill=spark --runs=3
# Prints [BOT] lines: a status line every 30 s of game time and a RESULT line
# per run. Rebuild C# first (dotnet build) if scripts changed.
set -euo pipefail
PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec "${PROJECT_DIR}/scripts/run_headless.sh" --fixed-fps 60 "res://scenes/tools/AutoplayBot.tscn" -- "$@"
