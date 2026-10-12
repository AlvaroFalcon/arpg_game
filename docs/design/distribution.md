# Distribution: releases and the launcher **[IMPLEMENTED 2026-10-12]**

## What it is

- **Releases are built by CI.** `.github/workflows/release.yml` runs on a pushed
  `v*` tag (or by hand from the Actions tab with a version). It downloads Godot
  4.1.1 mono and its export templates (cached), imports the project, exports the
  `Windows Desktop` preset, zips it and publishes a GitHub release with three
  assets: `ArpgGame-windows.zip`, `version.txt` and `ArpgGameLauncher.exe`.
- **The launcher** (`tools/launcher/`, a .NET Framework 4.8 WinForms app, so
  nothing to install on Windows 10/11) reads
  `releases/latest/download/version.txt`; if it differs from the installed
  `game/version.txt` it downloads the zip with a progress bar, extracts it to
  `game.new` and swaps folders (the old build survives a failed download), then
  PLAY starts `ArpgGame.exe`. Offline, it plays what is installed. The game lives
  in `%LOCALAPPDATA%\ArpgGame\game`; saves stay in Godot's user folder
  (`%APPDATA%\Godot\app_userdata\ARPG Game`), untouched by updates.
- `releases/latest/download/<asset>` is a plain redirect, so the launcher makes
  no API calls and has no rate limits. An optional first argument overrides the
  base URL (used to test against a local server).

## Why

The owner wanted friends to install once and always play the latest build, with
no billing risk and no extra services. Alternatives considered:

- **Google Drive** — the connector can't upload a 90 MB build or replace a file
  in place, can't create public links, and Drive throttles/blocks automated
  downloads of large files. Rejected.
- **Cloudflare R2** — robust and effectively free, but no hard spending cap; the
  owner was not comfortable with that. **Dropbox** — workable but fragile.
- **itch.io + butler** — great (delta patches, the itch app is the launcher) but
  needs everyone to use the itch app. Kept as a later option.
- **A separate public builds repo** — the owner preferred to make the game repo
  itself public instead.

## The rename

Before going public the project stopped using the "Path to Exile" name: the
assembly, namespaces, solution, export presets and window title are now
`ArpgGame` / "ARPG Game" (a placeholder until the game has a real name). Godot
keys `user://` on the project name, so `Core/LegacySave.cs` copies
`profile.json`/`settings.cfg` from the old folder once if the new one has no
profile.

## Open questions

- The launcher doesn't update itself yet (it is tiny and rarely changes; a new
  one can be downloaded from the release page).
- Unsigned binaries: SmartScreen warns on first run ("More info" → "Run anyway").
- macOS builds aren't published yet; the launcher is Windows-only.
