import { patch, recolor, shift, sym, type SpriteSpec } from '../lib';

// Monsters and the boss of the two newer realities (docs/design/endgame.md ->
// "Layouts"): the Bone Crypt's skeletons and the Drowned Mirror's thralls,
// sirens and Nereth. Same conventions as enemies.ts: half-drawn and mirrored
// with sym() where the creature is symmetric, 'k' outline, two-frame loops.

const enemyDir = 'assets/sprites/enemies';
const bossDir = 'assets/sprites/bosses';

// --- Crypt skeleton: rattling melee chaser -------------------------------------------
const skeletonLegend = { k: '#14121a', w: '#e8e2cf', W: '#b3aa92', d: '#7a725f', e: '#7bd0ff', r: '#6b4a2a', s: '#9aa3ad' };
const skeletonA = sym([
  '...kkk',
  '..kwww',
  '..kwek',
  '..kwww',
  '...kWk',
  '..kkwk',
  '.kWkwk',
  '.kWkWw',
  'kd.kwk',
  'kd.kWk',
  '...kwk',
  '..kWkk',
  '..kWk.',
  '..kWk.',
  '..kkk.',
], false);
// Swinging an old blade on the off-beat frame.
const skeletonB = patch(shift(skeletonA, 0, 1), 5, ['..kkwkkwk.ss', '.kWkwkkwkss.']);
export const skeleton: SpriteSpec = {
  name: 'skeleton', dir: enemyDir, legend: skeletonLegend,
  frames: [{ rows: skeletonA, duration: 200 }, { rows: skeletonB, duration: 200 }],
  animations: [{ name: 'move', from: 0, to: 1 }],
  autoplay: 'move',
};

// --- Drowned thrall: slow, bloated, hits hard -----------------------------------------
const thrallLegend = { k: '#081316', g: '#3f6f6a', G: '#5f9a8e', d: '#294a48', e: '#e8f86a', b: '#7aa0a8', s: '#2b5e3a' };
const thrallA = sym([
  '....kkkk',
  '...kGGGG',
  '..kGGeGG',
  '..kGGGGk',
  '...kkGGG',
  '..kGGkGG',
  '.kGGGGkg',
  'kGGggGGg',
  'kgsgggGg',
  'kgsggggb',
  'kgkgggbg',
  '.k.kgggg',
  '...kgggd',
  '...kddkd',
  '...kddk.',
  '...kkkk.',
], false);
const thrallB = recolor(shift(thrallA, 0, 1), { s: 'g', b: 's' });
export const thrall: SpriteSpec = {
  name: 'thrall', dir: enemyDir, legend: thrallLegend,
  frames: [{ rows: thrallA, duration: 320 }, { rows: thrallB, duration: 320 }],
  animations: [{ name: 'move', from: 0, to: 1 }],
  autoplay: 'move',
};

// --- Siren: ranged, hovers, sings cold bolts ---------------------------------------------
const sirenLegend = { k: '#0a1020', s: '#9fd7e8', S: '#d8f6ff', h: '#2a7fa8', H: '#5fc0e0', t: '#1c4f7a', e: '#ffffff', p: '#a0f0ff' };
const sirenA = sym([
  '...kkkk',
  '..kHHHH',
  '.kHhSSS',
  '.khSekS',
  '.khSSSS',
  'khhkSSS',
  'kh.kssk',
  'kh.ksSS',
  '.kkssss',
  '..ksSSs',
  '..ktttt',
  '...kttt',
  '...ktt.',
  '....kt.',
  '....kp.',
  '.......',
], false);
const sirenB = recolor(shift(sirenA, 0, -1), { p: 'S', H: 'p' });
export const siren: SpriteSpec = {
  name: 'siren', dir: enemyDir, legend: sirenLegend,
  frames: [{ rows: sirenA, duration: 260 }, { rows: sirenB, duration: 260 }],
  animations: [{ name: 'move', from: 0, to: 1 }],
  autoplay: 'move',
};

// --- Nereth, the Drowned Reflection: the Drowned Mirror's boss ----------------------------
const nerethLegend = {
  k: '#06101a', c: '#c9e9f5', C: '#ffffff', m: '#3a7f9e', M: '#6fc3e0',
  d: '#163a52', D: '#24577a', e: '#7af0ff', g: '#d9b45a', p: '#a8f0ff',
};
const nerethA = sym([
  '.......g....',
  '......gpg...',
  '.....gkpkg.g',
  '....g.kcccgp',
  '......kcCCCC',
  '.....kcCCkkC',
  '.....kcCkeek',
  '.....kcCCkkC',
  '......kcCCCC',
  '.....kmkcccc',
  '....kmMMkkkk',
  '...kmMMMMMMM',
  '..kmMMdMMMMM',
  '.kmMMkdMMMMM',
  'kmMkk.kdMMMM',
  'kmk...kdMMDD',
  'kMk...kddDDD',
  '.kk...kdDDDD',
  '......kdDDDd',
  '.....kdDDDdd',
  '....kdDDDddd',
  '...kdDDddddd',
  '..kdDddkdddk',
  '..kkk.k.kk.k',
]);
const nerethB = recolor(nerethA, { M: 'm', m: 'M', p: 'C' });
// Casting: arms and crown flare with mirror-light.
const nerethCast = recolor(nerethA, { M: 'p', g: 'C', e: 'C' });
export const nereth: SpriteSpec = {
  name: 'nereth', dir: bossDir, legend: nerethLegend,
  frames: [{ rows: nerethA, duration: 260 }, { rows: nerethB, duration: 260 }, { rows: nerethCast, duration: 120 }],
  animations: [
    { name: 'idle', from: 0, to: 1 },
    { name: 'windup', from: 2, to: 2 },
  ],
  autoplay: 'idle',
};

// --- Projectiles ---------------------------------------------------------------------
const tideLegend = { k: '#0a2a44', b: '#3fa0e0', B: '#a8e8ff', w: '#ffffff' };
const tideA = [
  '..kkkk..',
  '.kbbBBk.',
  'kbbBBwBk',
  'kbBBwwBk',
  'kbBBBBbk',
  'kbbBBbbk',
  '.kbbbbk.',
  '..kkkk..',
];
export const tideOrb: SpriteSpec = {
  name: 'tide_orb', dir: 'assets/sprites/projectiles', legend: tideLegend,
  frames: [{ rows: tideA, duration: 120 }, { rows: recolor(tideA, { b: 'B', B: 'w' }), duration: 120 }],
  animations: [{ name: 'fly', from: 0, to: 1 }],
  autoplay: 'fly',
};

export const realms = [skeleton, thrall, siren, nereth, tideOrb];
