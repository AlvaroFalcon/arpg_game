# Localization (English, Spanish)

Status: **implemented, first pass** (2026-11-07). Owner request (translated): *"can we set up
a language switch? I'd like it to have Spanish too."*

## What it is

**Settings → Gameplay → Language / Idioma** offers *Automatic* (the system's language when the
game has it, else English), *English* and *Español*. The setting is saved in
`user://settings.cfg` (`general/language`) and applies at once. Labels, buttons, tabs and option
lists re-translate themselves; text built in code shows in the new language the next time it is
rebuilt (opening a panel, the next level-up). `-- --lang=es` plays or captures in a language
without touching the saved setting.

## How it works

- **English is the key.** The game is written in English. `assets/i18n/es.tsv` maps each English
  text to its Spanish one: `English<TAB>Spanish`, with `\n` for a line break and `{0}`, `{1:0.0}`
  for values. A missing line shows in English, never as a blank, so a new feature never breaks
  the Spanish game. It only shows some English until its lines are added.
- **Godot translates most of the UI by itself.** Labels, Buttons, TabContainer titles and
  OptionButton items translate their text through the `TranslationServer` (auto-translate is on
  by default). `Core/Loc` loads the table at startup into a `PatternTranslation` (a
  `Translation` subclass) and registers it, so most of the UI needed no code change.
- **Texts with values** ("Level 6", "Danger 3 · Tier 2", "Chest · Spark → Lv 4") are matched
  against the table's templates ("Level {0}" → "Nivel {0}"). A value that is itself a known text
  is translated too, so "Spark" inside a chest line becomes "Chispa". Rules that keep it sane:
  - a template needs at least two letters of its own;
  - two values side by side ("{1}{2}") split as "everything, then the rest";
  - a captured value with more than two spaces must be a known text, or the template does not
    fit (otherwise "{0}% more {1}" would half-translate unrelated sentences);
  - results are cached.
- **Multi-line texts** are found whole, and every line of a multi-line entry is also a key of
  its own, because the instant tooltips show text one line at a time.
- **Text drawn by hand** (`DrawString` / `DrawStringOutline`: skill bar, shrines, maps, the loot
  feed, plates) goes through `Loc.T(...)`. Damage numbers are left alone: they are only numbers.
- **Code that builds a sentence from pieces** must translate the pieces: `Loc.T(name)` and
  `Loc.F("template {0}", value)`, not `$"..."` concatenation. The Rift's entry panel, level-up
  screen, chest lines, evolution banner, challenge toast and danger gauge do this already. Other
  screens still have some concatenated texts that stay partly English ("Next steps" below).

## Keeping it up to date

- `python3 tools/i18n/extract.py --missing es` lists the player-facing English strings in
  `scripts/` and the skill `.tres` files (literals, and `$"..."` templates turned into
  `{0}` form) that `es.tsv` has no line for, grouped by file. It also writes the full list to
  `tools/i18n/strings.txt`.
- Add the missing lines to `es.tsv`, keeping the placeholders. A line whose placeholders don't
  match its key is a bug: the merge used for the first pass refuses it.
- Glossary used: Survival Rift → *Grieta de Supervivencia*, Hideout → *Refugio*, Mirror →
  *Espejo*, Altered Reality → *Realidad Alterada*, Ember Colossus → *Coloso de Ascuas*, Bone
  Herald → *Heraldo de Hueso*, keystone → *piedra angular*, power-up → *power-up*, Lv → *Nv*,
  tier → *nivel*, Greed → *Codicia*, Fear card → *carta de miedo*, the Onslaught → *la
  Embestida*.
- The export presets include `assets/i18n/*.tsv` (`export_presets.cfg` -> `include_filter`):
  without it the exported game would have no table and stay in English.

## Coverage of the first pass

2,263 entries, out of 2,465 strings the extractor finds. The 200-odd left are mostly log and
warning lines that never reach the screen. Visible gaps known:

- **Concatenated texts outside the Rift's screens** (some tooltips, item lines, co-op status
  lines) show partly in English until their code uses `Loc.F`.
- **Item affix lines and names built from parts** (`ItemTooltipText`, the affix roller) are not
  covered by templates yet.
- **Quest copy with Markdown-like bold** is translated per text block as written.

## Alternatives considered

- **Godot's CSV import** (`.csv` → `.translation`): needs the editor import step and makes the
  table a binary resource. A plain TSV read at startup works headless and in tests, and is easy
  to edit and review in a diff.
- **Keys instead of English text** (`"menu.play"`): safer against two texts that are the same
  English but different Spanish, but it would mean touching every one of ~2,400 strings in the
  code. English-as-key gets most of the UI for free. A context can be added later
  (`Translation._GetMessage` receives one) if a collision shows up.
- **Converting every `$"..."` to `Loc.F`**: about 2,000 sites. The template matcher covers most
  of them without a code change; the visible composites are converted as they are found.

## Next steps

1. Convert the remaining visible composites screen by screen (item tooltips, co-op status
   lines, the character panel), checking with `RunCapture -- --only=<screen> --lang=es`.
2. A Spanish proofreading pass by the owner on the captures.
3. More languages: add a row to `Loc.Languages` and an `assets/i18n/<locale>.tsv`.
