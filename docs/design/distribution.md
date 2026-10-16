# Distribution: releases and the launcher **[IMPLEMENTED 2026-10-12]**

## What it is

- **Releases are built by CI.** `.github/workflows/release.yml` runs on every
  push to `main` (version `v0.1.<run number>`), on a pushed `v*` tag (that exact
  version) or by hand from the Actions tab. It downloads Godot
  4.1.1 mono and its export templates (cached), imports the project, exports the
  `Windows Desktop` preset, zips it and publishes a GitHub release with three
  assets: `ArpgGame-windows.zip`, `version.txt` and `ArpgGameLauncher.exe`.
- **The launcher** (`tools/launcher/`, a .NET Framework 4.8 WinForms app, so
  nothing to install on Windows 10/11) asks the GitHub API for recent releases
  and takes the newest that already has the zip attached (a release exists about
  a minute before its files finish uploading, and `releases/latest/download`
  404s in that window — the first owner test hit exactly that). If the API is
  unreachable or rate-limited (60 requests/hour per IP) it falls back to
  `releases/latest/download/version.txt`. If the version differs from the installed
  `game/version.txt` it downloads the zip with a progress bar, extracts it to
  `game.new` and swaps folders (the old build survives a failed download), then
  PLAY starts `ArpgGame.exe`. Offline, it plays what is installed. The game lives
  in `<install folder>\game` (see "Install folder"); saves stay in Godot's user
  folder (`%APPDATA%\Godot\app_userdata\ARPG Game`), untouched by updates and by
  where the game is installed.
- **Look** (launcher 2.0): a borderless, custom-drawn 960×540 window
  (`LauncherView`, pure System.Drawing so it renders off-screen for previews;
  `LauncherForm` handles DPI, dragging and the Win11 rounded corners). The key art
  is pixel art composed by `tools/art/launcher.ts` from the game's own sprites: the
  warrior and rogue mid-attack into the horde in the middle (slash arcs, sparks),
  the Arcanist and Sage behind on rune circles charging spells, the Ember Colossus
  looming on the right, lit in float RGB (warm gold vs hellfire) and quantised back.
  On top: a glass bottom bar with the title, a slim gradient progress bar with a
  shimmer, a glowing PLAY button (RETRY on failure, UPDATING while busy), drifting
  embers and pulsing lights. A first draft had a literal "VS" over a rift of light;
  the owner found it too on the nose and wanted the scene to show a fight instead.
  A second pass replaced the up-scaled game sprites with illustrated figures
  ("much more quality, like real characters, really moving"): `tools/art/rig.ts`
  is a small pose rig (joints → shaded tapered limbs, torso, anime head in
  three-quarter view, flowing hair/capes) and `tools/art/heroes.ts` poses the
  warrior lunging into a slash, the rogue leaping onto a bat, the Arcanist casting
  and the Sage reading her tome, against a demon brute. The remaining monsters are
  drawn as dark silhouettes whose hot details glow, so their low-res sprites read
  as depth instead of clashing with the illustrated heroes.
- An optional first argument (a folder URL serving `version.txt` and the zip)
  skips the API; it is how the updater was tested against a local server.

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

## Install folder

The launcher used to install silently into `%LOCALAPPDATA%\ArpgGame`. Now, when
the game isn't installed anywhere it knows of, it asks first
(`InstallLocation.cs`, `LauncherPhase.ChooseFolder`):

- **First run:** the bottom bar shows "Install to: <folder>" with the free space on
  that drive, a **Change…** button (folder picker) and **INSTALL**. Nothing is
  downloaded until INSTALL is pressed.
- **The choice is remembered** in `%LOCALAPPDATA%\ArpgGame\launcher.cfg`
  (`install_root=<path>`): that small file never moves, so the launcher finds the
  game wherever it went. Older installs in the old default folder are adopted
  without asking.
- **Picking a folder:** a folder that already holds the game (its root, its
  `game` folder, or an `ArpgGame` folder inside it) is adopted as is — useful after
  moving the game or reinstalling Windows. Any other non-empty folder gets an
  `ArpgGame` subfolder, so choosing `D:\Games` doesn't scatter files into it.
- **Write check:** before downloading, the launcher writes and deletes a probe
  file; a folder it can't write to (e.g. Program Files without admin) shows the
  reason in red and stays on the choose step instead of failing mid-download.
- If the very first download fails, Change… stays available so a different folder
  can be tried. Saves never move: they live in Godot's user folder.

Rejected: a full installer (MSI/Inno Setup) — overkill for a zip the launcher
already swaps in place, and it would need admin rights; a plain "always ask"
dialog — returning players shouldn't see it.

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
- Moving an existing install from the launcher (copying the files) isn't offered:
  move the folder by hand and point Change… at it.
