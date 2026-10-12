import { patch, type SpriteSpec } from '../lib';

// The Exile: a hooded pyromancer with an ember-topped staff. Faces right; the game
// flips it horizontally when moving left.
const legend = {
  k: '#1b1426', // outline
  H: '#5a48a0', // hood light
  h: '#3b2d6b', // hood shade
  s: '#f2c29b', // skin
  e: '#ffe066', // glowing eyes
  R: '#6d5bc4', // robe light
  r: '#4b3a8c', // robe
  d: '#33276a', // robe shade
  g: '#e8b84a', // gold trim
  w: '#8a5a2b', // staff wood
  o: '#ff7b2e', // ember
  O: '#ffd27a', // ember core
  b: '#2a2038', // boots
};

const base = [
  '......kkkk....kk',
  '.....kHHHHk..kOk',
  '....kHHHHHhk.koO',
  '....kHHkkkhk..kk',
  '....kHkesekk..w.',
  '....kkssssk...w.',
  '...kRrkkkkrk..w.',
  '..kRRrrgrrrrk.w.',
  '..kRrrrgrrrdkkss',
  '..ksRrrgrrrdk.w.',
  '...kRrrgrrrdk.w.',
  '...kRrrgrrrdk.w.',
  '...kRrrgrrdddkw.',
  '..kRRrrgrrdddkw.',
  '..kkkbbkkbbkkkw.',
  '....kkk..kkk....',
];

const idle2 = patch(base, 1, ['.....kHHHHk..kok', '....kHHHHHhk.kOo']);

const feet = (a: string, b: string) => patch(base, 14, [a, b]);
const walk1 = feet('..kkbbkkkkbbkkw.', '...kkk....kkk...');
const walk2 = feet('..kkkbbkkbbkkkw.', '....kkk..kkk....');
const walk3 = feet('..kkkkbbbbkkkkw.', '.....kkkkkk.....');

export const player: SpriteSpec = {
  name: 'player',
  dir: 'assets/sprites/player',
  legend,
  frames: [
    { rows: base, duration: 400 },
    { rows: idle2, duration: 400 },
    { rows: walk1, duration: 120 },
    { rows: walk2, duration: 120 },
    { rows: walk3, duration: 120 },
    { rows: walk2, duration: 120 },
  ],
  animations: [
    { name: 'idle', from: 0, to: 1 },
    { name: 'walk', from: 2, to: 5 },
  ],
  autoplay: 'idle',
};
