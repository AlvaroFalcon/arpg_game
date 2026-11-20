# Knowledge: product (users, goals and priorities)

What the `product` agent needs to judge whether a request is worth it. Each bullet states one fact
and says where it comes from. Update a fact in place when it changes; don't append contradictions.

Sources: *owner decision on YYYY-MM-DD* · *measured on YYYY-MM-DD* · *user feedback on YYYY-MM-DD*
· *official docs* (link).

## Users
- The players today are the owner and their friends, who install the game once with the launcher
  and always get the latest build (`docs/design/distribution.md`). A general audience is for
  later, not now. (*owner decision on 2026-11-20*)

## Purpose
- A hobby project the owner wants to polish and perhaps publish one day. No commercial release is
  decided. (*owner decision on 2026-11-20*)

## The experience and its pillars
- In the owner's words: a game that lets you theorycraft your character and grind to improve your
  gear, with a story and lore behind it. (*owner decision on 2026-11-20*)
- Every request is judged against three pillars; one that strengthens none of them, or makes
  builds shallower, ranks low (*owner decision on 2026-11-20*):
  1. **Theorycrafting:** class and ascendancy, passive tree, skills and their trees, crafting.
     Build decisions must matter.
  2. **Grind and gear progression:** loot and character progress are the reward.
  3. **Story and lore** that back the world and give the grind a meaning.
- Online co-op is an important extra, not a pillar and not a priority now: changes must keep it
  working, but a request gains no value just for being about co-op. (*owner decision on
  2026-11-20*)

## Goals
- Now: polish what exists before adding systems. Make the first hour hook a new player (The
  Threshold, the first boss); balance skills, enemies and the passive tree; define a clear tone
  and a story. The agreed order of the player feedback work is `docs/backlog.md`. (*owner
  decision on 2026-11-20*)
- How we know it works: feedback from different friends, filed as issues labelled
  `feedback-jugador`; balance is measured with the autoplay bot (`docs/design/balance-testing.md`).
  Player metrics may come later. (*owner decision on 2026-11-20*)

## Not a priority
- Web, mobile and consoles (Web is ruled out anyway: C# cannot export to Web in Godot 4, see
  `CLAUDE.md` -> "Target platforms"). (*owner decision on 2026-11-20*)
- Monetisation, store pages and marketing. (*owner decision on 2026-11-20*)
- Big new systems (more classes, more modes) until the polish above is done. (*owner decision on
  2026-11-20*)

## Decisions
- Keep the game publishable: only own assets or assets whose license allows publishing, and no
  copyrighted material from other games. Adapted agents and skills are credited in
  `.claude/THIRD_PARTY.md`. (*owner decision on 2026-11-20*)
- The art pipeline will move from EasyPixel to ComfyUI. Before adopting a model, checkpoint or LoRA,
  check that its license allows commercial use of what it generates and note it in
  `docs/design/art-pipeline.md`; some popular models are non-commercial. (*owner decision on
  2026-11-20*; licenses are volatile, re-check before relying on them)
