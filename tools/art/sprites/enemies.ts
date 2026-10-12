import { patch, recolor, shift, sym, type SpriteSpec } from '../lib';

const dir = 'assets/sprites/enemies';

// --- Ash slime: the basic chaser ------------------------------------------------
const slimeLegend = { k: '#14231a', g: '#3fa34d', G: '#7fd66b', d: '#24693a', w: '#e8ffe0', e: '#14231a' };
const slimeA = sym([
  '.......',
  '.......',
  '....kkk',
  '..kkGGG',
  '.kGGwGg',
  '.kGwggg',
  'kgggkgg',
  'kggggkg',
  'kdgggggg'.slice(0, 7),
  '.kddddd',
  '..kkkkk',
], false);
const slimeB = sym([
  '.......',
  '.......',
  '.......',
  '....kkk',
  '..kkGGG',
  '.kGwwGg',
  'kgggkgg',
  'kggggkg',
  'kdggggg',
  'kdddddd',
  '.kkkkkk',
], false);
const slimeC = shift(sym([
  '....kkk',
  '...kGGG',
  '..kGwGg',
  '.kGwggg',
  '.kggkgg',
  '.kgggkg',
  '.kdgggg',
  '..kdddd',
  '...kkkk',
  '.......',
  '.......',
], false), 0, 0);

export const slime: SpriteSpec = {
  name: 'slime', dir, legend: slimeLegend,
  frames: [{ rows: slimeA, duration: 160 }, { rows: slimeB, duration: 160 }, { rows: slimeC, duration: 200 }],
  animations: [{ name: 'move', from: 0, to: 2, direction: 'pingpong' }],
  autoplay: 'move',
};

// --- Cinder bat: fast swarmer -----------------------------------------------------
const batLegend = { k: '#1a1018', b: '#5b2a4a', B: '#8a3f6b', r: '#ff4040', f: '#c46a9a' };
const batUp = sym([
  'k......',
  'kBk....',
  'kBBk..k',
  '.kBBkkb',
  '.kbBBbb',
  '..kkbrb',
  '....kbb',
  '.....kk',
], true);
const batDown = sym([
  '.......',
  '.......',
  '......k',
  '.....kb',
  '..kkkbb',
  '.kBBbrb',
  'kBBkkbb',
  'kkk..kk',
], true);
export const bat: SpriteSpec = {
  name: 'bat', dir, legend: batLegend,
  frames: [{ rows: batUp, duration: 90 }, { rows: batDown, duration: 90 }],
  animations: [{ name: 'move', from: 0, to: 1 }],
  autoplay: 'move',
};

// --- Bone caster: ranged enemy that keeps its distance ----------------------------
const casterLegend = { k: '#141018', w: '#e6e0d0', W: '#b8b0a0', e: '#7b3cff', p: '#4a2a6a', P: '#6b3f9a', s: '#9a7a4a', o: '#c27bff' };
const casterA = [
  '...kkkkk....',
  '..kwwwwWk...',
  '..kwekeWk.ko',
  '..kwwwwWk.ks',
  '...kWkWk..ks',
  '..kpPPPpk.ks',
  '.kpPPPPPpkks',
  '.kpkPPPpkwks',
  '.kp.kPPpk.ks',
  '..kpPPPpk.ks',
  '..kpPPPPpkks',
  '.kpPPPPPPpks',
  '.kppppppppk.',
  '..kWk..kWk..',
  '..kk....kk..',
];
const casterB = patch(recolor(casterA, { o: 'w' }), 2, ['..kwekeWk.kw']);
export const caster: SpriteSpec = {
  name: 'caster', dir, legend: casterLegend,
  frames: [{ rows: casterA, duration: 300 }, { rows: casterB, duration: 300 }],
  animations: [{ name: 'move', from: 0, to: 1 }],
  autoplay: 'move',
};

// --- Ember brute: slow, tanky, charges ----------------------------------------------
const bruteLegend = { k: '#1a0f0a', r: '#7a3b22', R: '#a4552e', d: '#4f2414', o: '#ff8a2a', y: '#ffd23f', g: '#5a5a62', G: '#8a8a94' };
const bruteA = sym([
  '.....kkkk',
  '....kRRRR',
  '...kRRyRk',
  '...kRRRRo',
  '..kkkRRRR',
  '.kRRRkRRR',
  'kRRRRRkrr',
  'kRRrrRRro',
  'kGRrrrRrr',
  'kGGkrrrro',
  'kGGkrrrrr',
  '.kk.krrrd',
  '....krrdd',
  '...kddkkd',
  '...kddk..',
  '...kkkk..',
  '.........',
  '.........',
], false);
const bruteB = shift(bruteA, 0, 1);
export const brute: SpriteSpec = {
  name: 'brute', dir, legend: bruteLegend,
  frames: [{ rows: bruteA, duration: 260 }, { rows: bruteB, duration: 260 }],
  animations: [{ name: 'move', from: 0, to: 1 }],
  autoplay: 'move',
};

// --- Volatile imp: runs in, flashes, explodes -----------------------------------------
const impLegend = { k: '#1a0a06', r: '#c0392b', R: '#ff6b4a', y: '#ffe14d', w: '#ffffff', o: '#ff9a2e' };
const impA = sym([
  '..k...',
  '.kRk..',
  '.kRRkk',
  '..kRRR',
  '.kRRyR',
  'kRRRRR',
  'kRrRRR',
  'krrkRR',
  '.krrrr',
  '..krrr',
  '..kk.k',
  '......',
], false);
const impFlash = recolor(impA, { R: 'w', r: 'y', y: 'o' });
export const imp: SpriteSpec = {
  name: 'imp', dir, legend: impLegend,
  frames: [{ rows: impA, duration: 120 }, { rows: shift(impA, 0, 1), duration: 120 }, { rows: impFlash, duration: 80 }],
  animations: [
    { name: 'move', from: 0, to: 1 },
    { name: 'primed', from: 1, to: 2 },
  ],
  autoplay: 'move',
};

export const enemies = [slime, bat, caster, brute, imp];
