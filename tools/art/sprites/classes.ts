import { patch, recolor, type SpriteSpec } from '../lib';

const dir = 'assets/sprites/player';

function walkFrames(base: string[], feetRow: number, a: string, b: string, c: string) {
  return [patch(base, feetRow, [a]), patch(base, feetRow, [b]), patch(base, feetRow, [c]), patch(base, feetRow, [b])];
}

function classSprite(name: string, legend: Record<string, string>, base: string[], idle2: string[], feet: [string, string, string], attack: string[]): SpriteSpec {
  const walk = walkFrames(base, 14, ...feet);
  return {
    name, dir, legend,
    frames: [
      { rows: base, duration: 400 },
      { rows: idle2, duration: 400 },
      ...walk.map((rows) => ({ rows, duration: 120 })),
      { rows: attack, duration: 120 },
    ],
    animations: [
      { name: 'idle', from: 0, to: 1 },
      { name: 'walk', from: 2, to: 5 },
      { name: 'attack', from: 6, to: 6, loop: false },
    ],
    autoplay: 'idle',
  };
}

// --- Warrior: plate, round helm, sword and shield ---------------------------------
const warriorLegend = {
  k: '#14100e', m: '#9aa3ad', M: '#d4dbe2', d: '#5d6670', s: '#e6b48a', e: '#1a1a1a',
  r: '#8a2a22', R: '#c0453a', w: '#6a4426', g: '#e8b84a', b: '#3a2a1e', W: '#f2f6ff',
};
const warriorBase = [
  '.....kkkk.......',
  '....kMMMmk......',
  '...kMMmmmdk.....',
  '...kmkkkkdk.....',
  '...kmseseskk..kW',
  '...kkssssk...kWk',
  '..kRRkkkkRk.kWk.',
  '.kRrmmmmmrRkWk..',
  'kddkmMMMmmkgk...',
  'kmdkmMmmmdkkk...',
  'kmdkmmmmmdk.....',
  'kmdkrrrrrrk.....',
  '.kkkmmmdddk.....',
  '...kmmkkddk.....',
  '...kbbk.kbbk....',
  '...kkk...kkk....',
];
const warriorIdle = patch(warriorBase, 6, ['..kRRkkkkRk..kW.', '.kRrmmmmmrRkkWk.']);
const warriorAttack = patch(warriorBase, 4, [
  '...kmseseskk....',
  '...kkssssk.kkkkk',
  '..kRRkkkkRkWWWWW',
  '.kRrmmmmmrRkkkkk',
]);
export const warrior = classSprite('warrior', warriorLegend, warriorBase, warriorIdle,
  ['...kbbkkkbbk....', '..kbbk...kbbk...', '....kbbkbbk.....'], warriorAttack);

// --- Rogue: dark hood and mask, twin daggers --------------------------------------
const rogueLegend = {
  k: '#0e100e', h: '#2f4a32', H: '#466b48', c: '#1e2a20', s: '#d9a57a', e: '#ffd23f',
  l: '#5a3f2a', L: '#7a5a3a', W: '#e6eef5', d: '#3a3a44', r: '#8a1a1a',
};
const rogueBase = [
  '......kkkk......',
  '.....kHHHhk.....',
  '....kHHhhhhk....',
  '....kHkkkkhk....',
  '....kkcekeck....',
  '....kkccccck....',
  '...khhkkkkhk....',
  '..kHhhhrhhhhk...',
  '.kWkhhhrhhhkWk..',
  'kWk.khhrhhk.kWk.',
  '.k..klllllk..k..',
  '....khhhhhk.....',
  '....khhkhhk.....',
  '....kddkddk.....',
  '....kddkddk.....',
  '....kkk.kkk.....',
];
const rogueIdle = patch(rogueBase, 4, ['....kkcckcck....']);
const rogueAttack = patch(rogueBase, 7, [
  '..kHhhhrhhhhkkk.',
  '.kWkhhhrhhhhkWWk',
  'kWk.khhrhhk..kk.',
]);
export const rogue = classSprite('rogue', rogueLegend, rogueBase, rogueIdle,
  ['...kddk.kddk....', '....kddkddk.....', '.....kddkk......'], rogueAttack);

// Ascendancy looks: palette swaps of the base class (docs/design/classes.md -> "Art").
export const ascendancyLooks: SpriteSpec[] = [
  { ...warrior, name: 'barbarian', legend: { ...warriorLegend, m: '#7a4a2a', M: '#a8683a', d: '#4a2a18', R: '#d4553a' } },
  { ...warrior, name: 'paladin', legend: { ...warriorLegend, m: '#d8c890', M: '#fff4c8', d: '#9a8a50', r: '#e8e8f0', R: '#ffffff' } },
  { ...warrior, name: 'samurai', legend: { ...warriorLegend, m: '#3a3a4a', M: '#5a5a70', d: '#22222c', r: '#a81a1a', R: '#e83a2a' } },
  { ...rogue, name: 'assassin', legend: { ...rogueLegend, h: '#3a1a2a', H: '#5a2a40', r: '#4aff6a' } },
  { ...rogue, name: 'ninja', legend: { ...rogueLegend, h: '#1a1a24', H: '#2a2a3a', c: '#0a0a10', r: '#ff3a3a' } },
];

export const classes = [warrior, rogue, ...ascendancyLooks];

// Arcanist ascendancy looks are recolours of the existing player sprite.
export function arcanistLooks(player: SpriteSpec): SpriteSpec[] {
  return [
    { ...player, name: 'ascendant', legend: { ...player.legend, H: '#e8e8ff', h: '#a8a8d8', R: '#f0f0ff', r: '#b8b8e8', d: '#8888b8', o: '#7af0ff', O: '#e8ffff' } },
    { ...player, name: 'warlock', legend: { ...player.legend, H: '#4a1a3a', h: '#2a0a20', R: '#5a2048', r: '#3a1030', d: '#200818', o: '#a03aff', O: '#e0a0ff', e: '#ff3a3a' } },
    { ...player, name: 'sage', legend: { ...player.legend, H: '#3a6a5a', h: '#24483c', R: '#4a8a70', r: '#2e6a54', d: '#1e4a3a', g: '#fff0a0' } },
  ];
}

export { recolor };
