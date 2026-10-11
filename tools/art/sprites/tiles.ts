import { mulberry32, type SpriteSpec } from '../lib';

// The Ashen Arena tileset: 16x16 tiles in a 4x2 atlas.
//   row 0: four ash-stone floor variants
//   row 1: lava A, lava B, arena wall, rune floor (the boss circle)
// Generated procedurally (deterministic seed) and written through draw_grid like
// every other sprite, so the result is an ordinary EasyPixel document.
const legend = {
  a: '#211c22', b: '#29232b', c: '#322b34', d: '#3b333c', e: '#17131a', // stone
  l: '#7a1a0a', m: '#c0350f', n: '#ff6a1a', o: '#ffb347', // lava
  w: '#4a3f3a', W: '#6e605a', x: '#2e2522', k: '#120c0e', // wall
  r: '#3a1e16', R: '#9a4420', // runes (dim: they sit under the fight)
};

const T = 16;

function floorTile(seed: number): string[][] {
  const rnd = mulberry32(seed);
  const g: string[][] = [];
  for (let y = 0; y < T; y++) {
    g.push([]);
    for (let x = 0; x < T; x++) {
      const v = rnd();
      g[y].push(v < 0.55 ? 'b' : v < 0.8 ? 'a' : v < 0.95 ? 'c' : 'd');
    }
  }
  // Slab seams so the floor reads as laid stone, not noise.
  const seamY = 7 + Math.floor(rnd() * 3);
  const seamX = 4 + Math.floor(rnd() * 8);
  for (let x = 0; x < T; x++) g[seamY][x] = 'e';
  for (let y = 0; y < seamY; y++) g[y][seamX] = 'e';
  const seamX2 = (seamX + 8) % T;
  for (let y = seamY + 1; y < T; y++) g[y][seamX2] = 'e';
  // A crack.
  let cx = Math.floor(rnd() * T);
  let cy = Math.floor(rnd() * T);
  for (let i = 0; i < 5; i++) {
    g[cy][cx] = 'e';
    cx = Math.min(T - 1, Math.max(0, cx + (rnd() < 0.5 ? -1 : 1)));
    cy = Math.min(T - 1, cy + 1);
  }
  return g;
}

function lavaTile(seed: number): string[][] {
  const rnd = mulberry32(seed);
  const g: string[][] = [];
  for (let y = 0; y < T; y++) {
    g.push([]);
    for (let x = 0; x < T; x++) {
      const wave = Math.sin((x + seed) * 0.8) + Math.cos((y * 0.9 + x * 0.3 + seed) * 0.7);
      const v = wave + rnd() * 0.8;
      g[y].push(v > 1.4 ? 'o' : v > 0.6 ? 'n' : v > -0.4 ? 'm' : 'l');
    }
  }
  return g;
}

function wallTile(): string[][] {
  const g: string[][] = [];
  for (let y = 0; y < T; y++) {
    g.push([]);
    for (let x = 0; x < T; x++) {
      const brickRow = Math.floor(y / 4);
      const offset = brickRow % 2 ? 4 : 0;
      const mortar = y % 4 === 3 || (x + offset) % 8 === 7;
      g[y].push(mortar ? 'x' : y % 4 === 0 ? 'W' : 'w');
    }
  }
  for (let x = 0; x < T; x++) g[T - 1][x] = 'k';
  return g;
}

function runeTile(): string[][] {
  const g = floorTile(99);
  const glyph = [
    '................',
    '................',
    '.....rRRRRr.....',
    '....rR....Rr....',
    '...rR..rr..Rr...',
    '...R..rRRr..R...',
    '...R.rR..Rr.R...',
    '...R.R....R.R...',
    '...R.R....R.R...',
    '...R.rR..Rr.R...',
    '...R..rRRr..R...',
    '...rR..rr..Rr...',
    '....rR....Rr....',
    '.....rRRRRr.....',
    '................',
    '................',
  ];
  glyph.forEach((row, y) => [...row].forEach((c, x) => c !== '.' && (g[y][x] = c)));
  return g;
}

function atlas(tiles: string[][][], columns: number): string[] {
  const rows: string[] = [];
  const rowsOfTiles = Math.ceil(tiles.length / columns);
  for (let ty = 0; ty < rowsOfTiles; ty++) {
    for (let y = 0; y < T; y++) {
      let line = '';
      for (let tx = 0; tx < columns; tx++) line += tiles[ty * columns + tx][y].join('');
      rows.push(line);
    }
  }
  return rows;
}

export const arenaTiles: SpriteSpec = {
  name: 'ashen_arena',
  dir: 'assets/tiles',
  legend,
  frames: [{
    rows: atlas([floorTile(1), floorTile(2), floorTile(3), floorTile(4), lavaTile(5), lavaTile(9), wallTile(), runeTile()], 4),
    duration: 100,
  }],
  tileset: { tileWidth: 16, tileHeight: 16 },
};

export const tiles = [arenaTiles];
