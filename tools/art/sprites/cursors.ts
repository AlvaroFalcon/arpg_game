import type { SpriteSpec } from '../lib';

/**
 * Mouse cursors (scripts/UI/GameCursor.cs scales them up with nearest filtering
 * to suit the window). Hotspots live in GameCursor — docs/design/menus.md -> "Cursor".
 */
const dir = 'assets/ui/cursors';

// Bronze-and-gold gauntlet arrow: lit left edge, shaded right edge, a garnet set in it.
const arrowRows = [
  'kk...........',
  'kGk..........',
  'kGgk.........',
  'kGggk........',
  'kGgggk.......',
  'kGggggk......',
  'kGgggmmk.....',
  'kGggrRmmk....',
  'kGggrrmmmk...',
  'kGgggmmmmmk..',
  'kGgggmmmmmbk.',
  'kGggmmkkkkkkk',
  'kGgmkgmk.....',
  'kGmk.kgmk....',
  'kGk..kgmk....',
  'kk....kgmk...',
  'k.....kgmk...',
  '.......kk....',
];
const arrowLegend = { k: '#0c0810', G: '#fff0b0', g: '#e0a84a', m: '#a8682a', b: '#6a3c18', r: '#a01830', R: '#ff6070' };

export const cursorArrow: SpriteSpec = {
  name: 'cursor_arrow', dir, legend: arrowLegend,
  frames: [{ rows: arrowRows, duration: 100 }],
};

// Over buttons and things you can click: the same arrow, white-hot.
export const cursorHover: SpriteSpec = {
  name: 'cursor_hover', dir,
  legend: { ...arrowLegend, G: '#ffffff', g: '#fff0a0', m: '#f0b850', b: '#b06a28', r: '#30c8ff', R: '#c8f4ff' },
  frames: [{ rows: arrowRows, duration: 100 }],
};

// Over a monster: a sword, point up-left (the hotspot), gold guard, leather grip.
const swordRows = [
  'kk..............',
  'kWk.............',
  'kWwk............',
  '.kWwk...........',
  '..kWwk..........',
  '...kWwk.........',
  '....kWwk........',
  '.....kWwk.......',
  '......kWwk..kk..',
  '.......kWwk.kGk.',
  '........kWwkGgk.',
  '.........kWGgk..',
  '..........kgbbk.',
  '.........kGkkbbk',
  '.........kgk.kbr',
  '..........k...kk',
];
export const cursorAttack: SpriteSpec = {
  name: 'cursor_attack', dir,
  legend: { k: '#0c0810', W: '#f4f8ff', w: '#9aa6bc', G: '#fff0b0', g: '#d89a3a', b: '#5a3418', r: '#c02838' },
  frames: [{ rows: swordRows, duration: 100 }],
};

// Over an item on the ground: an open gauntlet hand (the hotspot is the index fingertip).
const handRows = [
  '.....kk.........',
  '....kGgk........',
  '....kGgk........',
  '....kGgkkk......',
  '....kGgkGgk.....',
  '....kGgkGgkkk...',
  '....kGgkGgkGgk..',
  '.kk.kGgkGgkGgk..',
  'kGgkkGgkGgkGgk..',
  'kGggkGgmGgmGgk..',
  '.kGgggggggggmk..',
  '..kGgggggggggk..',
  '..kGggggggggmk..',
  '...kGgggggmmk...',
  '...kbbbrbbbbk...',
  '...kkkkkkkkkk...',
];
export const cursorGrab: SpriteSpec = {
  name: 'cursor_grab', dir, legend: arrowLegend,
  frames: [{ rows: handRows, duration: 100 }],
};

export const cursors: SpriteSpec[] = [cursorArrow, cursorHover, cursorAttack, cursorGrab];
