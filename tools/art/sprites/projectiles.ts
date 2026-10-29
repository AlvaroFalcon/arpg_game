import { pngGrid, recolor, sym, type SpriteSpec } from '../lib';

const dir = 'assets/sprites/projectiles';

// All projectiles face right (+X); the game rotates them to their heading.

// The fireball comes from the approved ComfyUI strip (4 frames of 10x8, flying right):
// docs/design/art-pipeline.md -> "HD-2D with flat figures", recipe in tools/comfyui/env.json.
const FIREBALL_PNG = 'tools/comfyui/env/approved/fireball.png';
const fireballLegend: Record<string, string> = {};
const fireballFrames = [0, 1, 2, 3].map((i) => pngGrid(FIREBALL_PNG, fireballLegend, { x: i * 10, y: 0, w: 10, h: 8 }).map((r) => r.join('')));
export const fireball: SpriteSpec = {
  name: 'fireball', dir, legend: fireballLegend,
  frames: fireballFrames.map((rows) => ({ rows, duration: 80 })),
  animations: [{ name: 'fly', from: 0, to: 3 }], autoplay: 'fly',
};

const iceLegend = { b: '#2a6fd6', c: '#6ec6ff', w: '#e8fbff' };
const iceA = [
  '..........',
  'bb..ccc...',
  '.bbcwwwcc.',
  'bbccwwwwwc',
  '.bbcwwwcc.',
  'bb..ccc...',
];
export const frostShard: SpriteSpec = {
  name: 'frost_shard', dir, legend: iceLegend,
  frames: [{ rows: iceA, duration: 100 }],
};

const sparkLegend = { b: '#3a5aff', c: '#8ab4ff', w: '#ffffff', y: '#fff27a' };
const sparkA = [
  '...c....',
  '.c.cw.c.',
  '..cwwc..',
  'ccwyywcc',
  '..cwwc..',
  '.c.wc.c.',
  '....c...',
  '........',
];
const sparkB = [
  'c......c',
  '..c.c...',
  '...wwcc.',
  '.cwyywc.',
  '.cwyyw..',
  '..cwwc.c',
  '.c....c.',
  '........',
];
export const spark: SpriteSpec = {
  name: 'spark', dir, legend: sparkLegend,
  frames: [{ rows: sparkA, duration: 60 }, { rows: sparkB, duration: 60 }],
  animations: [{ name: 'fly', from: 0, to: 1 }], autoplay: 'fly',
};

const bladeLegend = { k: '#2a2a33', s: '#b8c0cc', w: '#f2f6ff', g: '#8a6a3a' };
const bladeA = [
  '....kk....',
  '....ksk...',
  '....kswk..',
  'kkk.kswk..',
  'kssskggkkk',
  'kkkggkssssk'.slice(0, 10),
  '..kwsk.kkk',
  '..kwsk....',
  '...ksk....',
  '....kk....',
];
export const blade: SpriteSpec = {
  name: 'blade', dir, legend: bladeLegend,
  frames: [{ rows: bladeA, duration: 100 }],
};

const boltLegend = { p: '#6a2aa0', f: '#b060ff', w: '#f0d8ff' };
const boltA = [
  '........',
  '..pp.ff.',
  '.ppffwff',
  'ppfwwwwf',
  'ppfwwwwf',
  '.ppffwff',
  '..pp.ff.',
  '........',
];
export const enemyBolt: SpriteSpec = {
  name: 'enemy_bolt', dir, legend: boltLegend,
  frames: [{ rows: boltA, duration: 100 }, { rows: recolor(boltA, { f: 'w', w: 'f' }), duration: 100 }],
  animations: [{ name: 'fly', from: 0, to: 1 }], autoplay: 'fly',
};

const orbLegend = { k: '#5a1006', m: '#ff3a1a', o: '#ff8a2a', y: '#ffe08a' };
const orbA = sym([
  '...kk',
  '.kkmm',
  '.kmoo',
  'kmooy',
  'kmoyy',
  'kmoyy',
  'kmooy',
  '.kmoo',
  '.kkmm',
  '...kk',
]);
export const bossOrb: SpriteSpec = {
  name: 'boss_orb', dir, legend: orbLegend,
  frames: [{ rows: orbA, duration: 90 }, { rows: recolor(orbA, { o: 'y', y: 'o' }), duration: 90 }],
  animations: [{ name: 'fly', from: 0, to: 1 }], autoplay: 'fly',
};

export const projectiles = [fireball, frostShard, spark, blade, enemyBolt, bossOrb];
