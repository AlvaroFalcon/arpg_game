import { mulberry32, recolor, type SpriteSpec } from '../lib';
import { Pix } from '../pix';

// The hideout town (docs/design/hideout.md): its tilesets, the station props and
// the decorations the hideout shop sells. Drawn procedurally with a tiny raster
// helper so every piece stays an ordinary EasyPixel document.

const T = 16;

function grid(fn: (x: number, y: number) => string): string[][] {
  return Array.from({ length: T }, (_, y) => Array.from({ length: T }, (_, x) => fn(x, y)));
}

function atlas(tiles: string[][][], columns: number): string[] {
  const rows: string[] = [];
  for (let ty = 0; ty < Math.ceil(tiles.length / columns); ty++) {
    for (let y = 0; y < T; y++) rows.push(tiles.slice(ty * columns, ty * columns + columns).map((t) => t[y].join('')).join(''));
  }
  return rows;
}

// --- tilesets ---------------------------------------------------------------------

/** Rounded cobblestones with dark joints. */
function cobble(seed: number): string[][] {
  const rnd = mulberry32(seed);
  const g = grid(() => 'e');
  const stones: [number, number, number, number][] = [];
  for (let row = 0; row < 4; row++) {
    let x = row % 2 ? -2 : 0;
    while (x < T) {
      const w = 3 + Math.floor(rnd() * 3);
      stones.push([x, row * 4, w, 4]);
      x += w + 1;
    }
  }
  for (const [x0, y0, w, h] of stones) {
    const tone = ['a', 'b', 'c', 'b'][Math.floor(rnd() * 4)];
    for (let y = y0; y < y0 + h - 1; y++) {
      for (let x = x0; x < x0 + w; x++) {
        if (x < 0 || x >= T) continue;
        const corner = (x === x0 || x === x0 + w - 1) && (y === y0 || y === y0 + h - 2);
        g[y][x] = corner ? 'e' : y === y0 ? 'd' : tone;
      }
    }
  }
  return g;
}

function water(seed: number): string[][] {
  const rnd = mulberry32(seed);
  return grid((x, y) => {
    const v = Math.sin((x + seed) * 0.7) + Math.cos((y * 0.8 + x * 0.25 + seed) * 0.6) + rnd() * 0.7;
    return v > 1.5 ? 'o' : v > 0.7 ? 'n' : v > -0.3 ? 'm' : 'l';
  });
}

function stoneWall(): string[][] {
  return grid((x, y) => {
    const row = Math.floor(y / 4);
    const mortar = y % 4 === 3 || (x + (row % 2 ? 4 : 0)) % 8 === 7;
    return y === T - 1 ? 'k' : mortar ? 'x' : y % 4 === 0 ? 'W' : 'w';
  });
}

/** The plaza mosaic: a compass star in terracotta and gold on pale stone. */
function mosaic(gold: string, red: string): string[][] {
  return grid((x, y) => {
    const dx = x - 7.5;
    const dy = y - 7.5;
    const r = Math.hypot(dx, dy);
    const star = Math.abs(dx) + Math.abs(dy) < 6 && (Math.abs(dx) < 1.2 || Math.abs(dy) < 1.2 || Math.abs(Math.abs(dx) - Math.abs(dy)) < 1.1);
    if (r > 7.2) return 'd';
    if (r > 6.3) return red;
    return star ? gold : (x + y) % 2 ? 'b' : 'c';
  });
}

export const townTiles: SpriteSpec = {
  name: 'town',
  dir: 'assets/tiles',
  legend: {
    a: '#6e665c', b: '#7c7368', c: '#8a8072', d: '#9a8f80', e: '#3e3832', // cobbles
    l: '#123a5a', m: '#1d5a85', n: '#3a86b5', o: '#a8d8ee', // canal
    w: '#8a7258', W: '#a88e70', x: '#5a4a3a', k: '#2a221a', // sandstone walls
    G: '#d8b048', R: '#a8482e', // mosaic
  },
  frames: [{ rows: atlas([cobble(1), cobble(2), cobble(3), cobble(4), water(5), water(9), stoneWall(), mosaic('G', 'R')], 4), duration: 100 }],
  tileset: { tileWidth: 16, tileHeight: 16 },
};

function marble(seed: number): string[][] {
  const rnd = mulberry32(seed);
  const g = grid((x, y) => ((x + y) % 16 === 0 ? 'b' : (x === 0 || y === 0) ? 'c' : 'a'));
  let x = Math.floor(rnd() * T);
  for (let y = 0; y < T; y++) {
    g[y][x] = 'b';
    x = Math.max(0, Math.min(T - 1, x + (rnd() < 0.5 ? -1 : 1)));
  }
  return g;
}

function planks(): string[][] {
  return grid((x, y) => {
    const board = Math.floor(y / 4);
    const seam = y % 4 === 3 || (x + board * 5) % 12 === 0;
    return seam ? 'k' : (x * 7 + board * 3) % 9 === 0 ? 'w' : board % 2 ? 'W' : 'P';
  });
}

function grass(seed: number): string[][] {
  const rnd = mulberry32(seed);
  return grid(() => {
    const v = rnd();
    return v < 0.08 ? 'F' : v < 0.35 ? 'h' : v < 0.85 ? 'g' : 'H';
  });
}

function carpet(): string[][] {
  return grid((x, y) => {
    const edge = Math.min(x, y, T - 1 - x, T - 1 - y);
    if (edge === 0) return 'K';
    if (edge === 1) return 'G';
    const d = Math.abs(x - 7.5) + Math.abs(y - 7.5);
    return Math.round(d) % 4 === 0 ? 'G' : 'C';
  });
}

function hedge(): string[][] {
  const rnd = mulberry32(77);
  return grid((_, y) => (y === T - 1 ? 'k' : rnd() < 0.3 ? 'H' : rnd() < 0.5 ? 'h' : 'g'));
}

export const townFloorTiles: SpriteSpec = {
  name: 'town_floors',
  dir: 'assets/tiles',
  legend: {
    a: '#e4e0d8', b: '#b8b4ac', c: '#cfcac0', // marble
    P: '#9a6a3a', W: '#87592e', w: '#6e4824', k: '#3e2814', // oak planks
    g: '#4f8a34', h: '#3f7428', H: '#66a540', F: '#e8d860', // grass + flowers
    C: '#8a1f2a', G: '#d8a83a', K: '#4a0e14', // carpet
    l: '#123a5a', m: '#1d5a85', n: '#3a86b5', o: '#a8d8ee', // water
    d: '#cfcac0', R: '#2a5aa8', // gold mosaic ring
  },
  frames: [{
    rows: atlas([marble(3), planks(), grass(4), carpet(), water(31), water(37), hedge(), mosaic('G', 'R')].map((t, i) => i === 7 ? t.map((r) => r.map((c) => (c === 'b' || c === 'c' ? 'a' : c))) : t), 4),
    duration: 100,
  }],
  tileset: { tileWidth: 16, tileHeight: 16 },
};

// --- props ------------------------------------------------------------------------

const wood = { k: '#1e140c', w: '#6e4824', W: '#9a6a3a', V: '#b8844c' };
const iron = { i: '#3a3c44', I: '#6a6e7a', J: '#9aa0ae' };
const fire = { f: '#d8401a', F: '#ff8a2a', y: '#ffd860', Y: '#fff4b0' };
const stone = { s: '#5a5650', S: '#7e7870', T: '#a29a8e' };

function prop(name: string, legend: Record<string, string>, frames: string[][], duration = 160): SpriteSpec {
  return {
    name,
    dir: 'assets/sprites/props',
    legend,
    frames: frames.map((rows) => ({ rows, duration })),
    animations: [{ name: 'idle', from: 0, to: frames.length - 1 }],
    autoplay: 'idle',
  };
}

function flame(p: Pix, cx: number, base: number, h: number, phase: number): void {
  for (let i = 0; i < h; i++) {
    const w = Math.max(1, Math.round((h - i) * 0.45 + ((i + phase) % 3 === 0 ? 1 : 0)));
    const sway = Math.round(Math.sin((i + phase * 2) * 0.9) * 0.8);
    for (let x = -w; x <= w; x++) {
      const c = Math.abs(x) <= w - 2 ? (i < h / 2 ? 'Y' : 'y') : Math.abs(x) <= w - 1 ? 'F' : 'f';
      p.set(cx + x + sway, base - i, c);
    }
  }
}

/** The forge hearth and anvil: the Artificer's bench in the smithy. */
function forgeFrame(phase: number): string[] {
  const p = new Pix(36, 30);
  // Hearth: a stone block with a glowing mouth and a chimney hood.
  p.rect(1, 10, 18, 19, 's').rect(2, 11, 16, 2, 'T').rect(4, 0, 12, 11, 'S').rect(5, 0, 10, 2, 'T');
  p.rect(4, 15, 12, 9, 'k').rect(5, 18, 10, 6, 'f');
  flame(p, 10, 23, 7, phase);
  p.rect(1, 27, 18, 2, 'S');
  // Anvil on a stump.
  p.rect(23, 22, 8, 7, 'w').rect(24, 22, 6, 1, 'W');
  p.rect(25, 18, 4, 4, 'i').rect(21, 14, 13, 4, 'I').rect(21, 14, 13, 1, 'J').rect(33, 15, 2, 2, 'I');
  // A glowing blade on the anvil.
  p.rect(23, 13, 8, 1, phase % 2 ? 'y' : 'F');
  return p.outline('k').rows();
}

export const forge = prop('forge', { ...wood, ...iron, ...fire, ...stone }, [forgeFrame(0), forgeFrame(1), forgeFrame(2)], 180);

function chestRows(): string[] {
  const p = new Pix(24, 18);
  p.rect(1, 6, 22, 11, 'w').rect(1, 2, 22, 5, 'W').rect(2, 1, 20, 1, 'W');
  p.rect(1, 6, 22, 1, 'k');
  for (const x of [4, 19]) p.rect(x, 1, 2, 16, 'I');
  p.rect(10, 5, 4, 4, 'y').rect(11, 7, 2, 1, 'k');
  return p.outline('k').rows();
}

export const stashChest = prop('stash_chest', { ...wood, ...iron, ...fire }, [chestRows()], 500);

/** Odran's market stall: striped awning over a counter of goods. */
function stallRows(): string[] {
  const p = new Pix(44, 36);
  for (const x of [2, 40]) p.rect(x, 6, 2, 29, 'w');
  for (let x = 0; x < 44; x++) for (let y = 2; y < 9; y++) p.set(x, y + (x % 8 < 4 ? 0 : 0), Math.floor(x / 4) % 2 ? 'r' : 'R');
  for (let x = 0; x < 44; x += 4) p.disc(x + 1.5, 9, 2, 1.2, Math.floor(x / 4) % 2 ? 'r' : 'R');
  p.rect(0, 0, 44, 2, 'w');
  p.rect(1, 22, 42, 12, 'W').rect(1, 22, 42, 2, 'V').rect(1, 33, 42, 1, 'k');
  // Goods: potions, a crate of orbs, a sack.
  for (const [x, c] of [[6, 'b'], [9, 'g'], [12, 'R']] as const) p.rect(x, 17, 2, 5, c).rect(x, 16, 2, 1, 'J');
  p.rect(18, 17, 8, 5, 'w').disc(20, 16.5, 1.5, 1.5, 'b').disc(23, 16.5, 1.5, 1.5, 'y');
  p.disc(34, 18, 4, 4, 'V').rect(33, 13, 2, 2, 'w');
  return p.outline('k').rows();
}

export const marketStall = prop('market_stall', { ...wood, ...iron, r: '#e8e0d0', R: '#b02a2a', b: '#3a7ad8', g: '#3aa84a', y: '#e8c040' }, [stallRows()], 500);

function brazierFrame(phase: number): string[] {
  const p = new Pix(14, 22);
  p.rect(2, 21, 3, 1, 'i').rect(9, 21, 3, 1, 'i');
  for (let y = 12; y < 21; y++) {
    p.set(3 + Math.floor((y - 12) / 4), y, 'i').set(10 - Math.floor((y - 12) / 4), y, 'i');
  }
  p.rect(6, 12, 2, 9, 'I');
  p.disc(7, 10, 6, 2.5, 'I').rect(1, 8, 12, 2, 'i').rect(2, 8, 10, 1, 'f');
  flame(p, 7, 7, 7, phase);
  return p.outline('k').rows();
}

export const brazier = prop('brazier', { k: '#120c0c', ...iron, ...fire }, [brazierFrame(0), brazierFrame(1), brazierFrame(2)], 150);

function lampFrame(on: boolean): string[] {
  const p = new Pix(10, 36);
  p.rect(4, 8, 2, 26, 'i').rect(2, 33, 6, 2, 'I').rect(3, 31, 4, 2, 'i');
  p.rect(1, 1, 8, 1, 'I').rect(2, 2, 6, 6, 'i').rect(3, 3, 4, 4, on ? 'Y' : 'y').rect(2, 0, 6, 1, 'i');
  return p.outline('k').rows();
}

export const lampPost = prop('lamp_post', { k: '#101014', ...iron, ...fire }, [lampFrame(true), lampFrame(false), lampFrame(true)], 400);

function bannerFrame(cloth: string, trim: string, sway: number): string[] {
  const p = new Pix(14, 34);
  p.rect(1, 0, 2, 34, 'w').rect(0, 0, 14, 2, 'w').rect(0, 32, 4, 2, 'W');
  for (let y = 2; y < 24; y++) {
    const off = Math.round(Math.sin(y * 0.4 + sway) * 0.7);
    for (let x = 4; x < 13; x++) p.set(x + off, y, x === 4 || x === 12 ? trim : cloth);
  }
  for (let x = 4; x < 13; x++) if ((x % 2) === 0) p.set(x, 24, cloth);
  p.disc(8, 11, 2.5, 2.5, trim).set(8, 11, cloth);
  return p.outline('k').rows();
}

export const bannerRed = prop('banner_red', { k: '#140c0c', ...wood, c: '#b0262a', t: '#e8b840' }, [bannerFrame('c', 't', 0), bannerFrame('c', 't', 1.5), bannerFrame('c', 't', 3)], 260);
export const bannerBlue = prop('banner_blue', { k: '#0c0c14', ...wood, c: '#2a4ab0', t: '#d8d8e8' }, [bannerFrame('c', 't', 0), bannerFrame('c', 't', 1.5), bannerFrame('c', 't', 3)], 260);

function plantRows(): string[] {
  const p = new Pix(14, 18);
  p.rect(3, 11, 8, 6, 'o').rect(2, 10, 10, 2, 'O');
  p.disc(7, 6, 5.5, 4.5, 'g').disc(5, 5, 2.5, 2, 'G').disc(9, 7, 2, 1.5, 'h').set(4, 3, 'F').set(10, 5, 'F').set(7, 2, 'F');
  return p.outline('k').rows();
}

export const pottedPlant = prop('potted_plant', { k: '#14100c', o: '#a8582e', O: '#c8743e', g: '#3f7a2a', G: '#62a440', h: '#2e5a1e', F: '#f0d050' }, [plantRows()], 500);

function crateRows(): string[] {
  const p = new Pix(16, 16);
  p.rect(0, 0, 16, 16, 'W').box(0, 0, 16, 16, 'w').box(1, 1, 14, 14, 'w');
  for (let i = 2; i < 14; i++) p.set(i, i, 'w').set(15 - i, i, 'w');
  return p.outline('k').rows();
}

export const crate = prop('crate', wood, [crateRows()], 500);

function barrelRows(): string[] {
  const p = new Pix(14, 18);
  p.disc(7, 9, 6, 8.5, 'W');
  for (let y = 0; y < 18; y++) for (const x of [3, 7, 10]) if (p.g[y][x] !== '.') p.set(x, y, 'w');
  for (const y of [3, 14]) for (let x = 0; x < 14; x++) if (p.g[y][x] !== '.') p.set(x, y, 'I');
  return p.outline('k').rows();
}

export const barrel = prop('barrel', { ...wood, ...iron }, [barrelRows()], 500);

/** A stone hero on a plinth, sword raised. */
function statueRows(): string[] {
  const p = new Pix(20, 40);
  p.rect(2, 32, 16, 7, 's').rect(3, 31, 14, 2, 'S').rect(2, 38, 16, 1, 'k');
  p.rect(7, 18, 6, 13, 'S').rect(6, 22, 8, 9, 'S'); // body and cloak
  p.rect(8, 26, 2, 5, 's').rect(11, 26, 2, 5, 's');
  p.disc(10, 15, 2.5, 2.5, 'T'); // head
  p.rect(13, 18, 2, 6, 'S').rect(14, 4, 1, 15, 'T').rect(12, 16, 5, 1, 's'); // arm and sword
  p.rect(5, 19, 2, 7, 'S');
  return p.outline('k').rows();
}

export const statue = prop('statue', { k: '#1a1816', ...stone }, [statueRows()], 500);

function benchRows(): string[] {
  const p = new Pix(26, 14);
  p.rect(0, 5, 26, 3, 'W').rect(0, 5, 26, 1, 'V').rect(0, 0, 26, 3, 'W');
  for (const x of [2, 22]) p.rect(x, 0, 2, 14, 'w');
  return p.outline('k').rows();
}

export const bench = prop('bench', wood, [benchRows()], 500);

function treeFrame(sway: number): string[] {
  const p = new Pix(32, 46);
  p.rect(14, 26, 5, 19, 'w').rect(15, 26, 2, 19, 'W').rect(11, 43, 11, 2, 'w');
  const s = sway;
  p.disc(16 + s, 16, 14, 12, 'g').disc(10 + s, 12, 6, 5, 'G').disc(22 + s, 18, 6, 5, 'h').disc(17 + s, 7, 7, 5, 'G').disc(12 + s, 21, 5, 4, 'h');
  return p.outline('k').rows();
}

export const tree = prop('tree', { ...wood, g: '#3f7a2a', G: '#5c9c3a', h: '#2e5a1e' }, [treeFrame(0), treeFrame(1), treeFrame(0)], 600);

function wellRows(): string[] {
  const p = new Pix(26, 32);
  p.rect(2, 20, 22, 11, 's').rect(2, 20, 22, 2, 'T');
  for (let x = 2; x < 24; x += 4) p.rect(x, 22, 1, 9, 'S');
  p.rect(3, 4, 2, 17, 'w').rect(21, 4, 2, 17, 'w');
  for (let y = 0; y < 5; y++) p.rect(y, 4 - y, 26 - y * 2, 1, 'r');
  p.rect(5, 9, 16, 1, 'w').rect(12, 10, 1, 5, 'I').rect(10, 15, 5, 4, 'W');
  return p.outline('k').rows();
}

export const well = prop('well', { ...wood, ...iron, ...stone, r: '#8a3a2a' }, [wellRows()], 500);

function rackRows(): string[] {
  const p = new Pix(22, 26);
  p.rect(1, 2, 2, 23, 'w').rect(19, 2, 2, 23, 'w').rect(0, 6, 22, 2, 'W').rect(0, 18, 22, 2, 'W');
  for (const x of [6, 10, 14]) p.rect(x, 1, 1, 20, x === 10 ? 'J' : 'I').rect(x - 1, 15, 3, 1, 'w');
  p.disc(17, 12, 1.8, 4, 'I');
  return p.outline('k').rows();
}

export const weaponRack = prop('weapon_rack', { ...wood, ...iron }, [rackRows()], 500);

function flowerRows(): string[] {
  const p = new Pix(18, 10);
  p.rect(0, 5, 18, 5, 'w').rect(0, 5, 18, 1, 'W').rect(1, 6, 16, 2, 'd');
  const rnd = mulberry32(5);
  for (let x = 1; x < 17; x += 2) {
    const h = 2 + Math.floor(rnd() * 3);
    p.rect(x, 6 - h, 1, h, 'g').set(x, 5 - h, ['r', 'y', 'b'][Math.floor(rnd() * 3)]);
  }
  return p.outline('k').rows();
}

export const flowerBed = prop('flower_bed', { ...wood, d: '#4a3020', g: '#3f7a2a', r: '#e04a5a', y: '#f0d050', b: '#7a8af0' }, [flowerRows()], 500);

/**
 * Arehucaz, the shard dealer: a tall hooded figure in a ragged black robe, two
 * violet eyes in the dark of the hood, a crooked staff with a lantern of cold
 * violet fire, and a cloth on the ground with Mirror Shards for sale.
 */
function arehucazFrame(phase: number): string[] {
  const p = new Pix(34, 44);
  // Robe: a long tapering cone, ragged hem.
  p.poly([[13, 9], [21, 9], [26, 40], [8, 40]], 'a');
  for (let x = 8; x <= 26; x += 3) p.rect(x, 40, 2, 1 + ((x + phase) % 2), 'a');
  p.poly([[17, 12], [20, 12], [24, 39], [19, 39]], 'A');
  p.line(17, 14, 15, 38, 'b');
  // Hood: a peaked cowl with a black face and two violet eyes.
  p.disc(17, 9, 6, 6, 'a').poly([[14, 3], [17, -1], [20, 3]], 'a');
  p.disc(17, 10, 3.6, 3.8, 'k');
  const eye = phase === 1 ? 'E' : 'e';
  p.set(15, 10, eye).set(19, 10, eye);
  // Shoulders and a bony hand on the staff.
  p.rect(10, 14, 4, 4, 'a').rect(11, 18, 3, 2, 'h');
  // Crooked staff with a hanging lantern.
  p.line(9, 4, 10, 41, 'w', 1).line(9, 4, 5, 2, 'w').line(5, 2, 4, 5, 'w');
  p.rect(2, 6, 5, 6, 'i').rect(3, 7, 3, 4, phase === 2 ? 'L' : 'l').set(4, 8, 'Y');
  p.rect(3, 5, 3, 1, 'I');
  // The cloth of wares: shards glowing on dark felt.
  p.rect(20, 38, 14, 5, 'c').rect(20, 38, 14, 1, 'C');
  for (const [x, c] of [[22, 'e'], [26, 'E'], [30, 'e']] as const) p.poly([[x, 37], [x + 1, 34 - (x % 3)], [x + 2, 37]], c);
  return p.outline('k').rows();
}

export const arehucaz = prop('arehucaz', {
  k: '#07050a', a: '#1c1424', A: '#2c2038', b: '#0e0a14', h: '#b8b0a0', w: '#3a2a1e',
  i: '#2a2a34', I: '#5a5a6a', l: '#9a5aff', L: '#c89aff', Y: '#f2e6ff', e: '#a46aff', E: '#e0c4ff',
  c: '#2a0e1e', C: '#4a1a30',
}, [arehucazFrame(0), arehucazFrame(1), arehucazFrame(2)], 260);

export const town = [
  townTiles, townFloorTiles, forge, stashChest, marketStall, arehucaz, brazier, lampPost, bannerRed, bannerBlue,
  pottedPlant, crate, barrel, statue, bench, tree, well, weaponRack, flowerBed,
];

void recolor;
