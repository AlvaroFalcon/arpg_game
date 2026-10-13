import { mulberry32, type SpriteSpec } from '../lib';
import { Pix } from '../pix';

// Inventory icons (docs/design/items.md -> "Item art"): one per base item, drawn at
// 16 px per inventory cell (shown at 2x) so each icon fills its grid footprint, and
// 16x16 icons for the crafting currencies (docs/design/crafting.md).

const k = '#120d0c';
const palette = {
  k,
  // iron
  i: '#3e424e', I: '#6c7282', J: '#a8aebc', H: '#e8ecf4',
  // wood
  w: '#5a381c', W: '#8a5a2e', V: '#b47c42',
  // leather
  l: '#4e2e1a', L: '#80502e', M: '#a87244',
  // copper
  c: '#7a3e1c', C: '#c0723a', D: '#f0b070',
  // bone
  b: '#8c7e66', B: '#d2c6aa', E: '#f4ecd8',
  // linen
  n: '#7a705e', N: '#b0a68e', O: '#dad2bc',
  // rope
  y: '#7a6236', Y: '#c0a060',
  // rust
  r: '#5a2a12', R: '#9a5226',
  // gems
  g: '#1e7a5a', G: '#6ee0b0', p: '#8a1a24', P: '#f05a5a', q: '#203c9a', Q: '#7aa8ff',
};

function item(name: string, p: Pix): SpriteSpec {
  return { name, dir: 'assets/sprites/items', legend: palette, frames: [{ rows: p.outline('k').rows(), duration: 100 }] };
}

function boneDagger(): Pix {
  const p = new Pix(16, 32);
  p.poly([[8, 1], [11, 7], [11, 20], [5, 20], [5, 7]], 'B');
  p.rect(7, 4, 1, 16, 'E').shade('B', 'b', (x) => x >= 10);
  p.rect(2, 20, 12, 2, 'b').rect(2, 20, 2, 2, 'B').rect(12, 20, 2, 2, 'B');
  p.rect(6, 22, 4, 7, 'L');
  for (let y = 22; y < 29; y += 2) p.rect(6, y, 4, 1, 'l');
  p.disc(7.5, 29.8, 2.3, 1.8, 'B').set(7, 29, 'E');
  return p;
}

function rustedSword(): Pix {
  const p = new Pix(16, 48);
  p.poly([[8, 1], [11, 5], [11, 33], [5, 33], [5, 5]], 'I');
  p.rect(6, 5, 1, 28, 'J').rect(8, 6, 1, 25, 'i').shade('I', 'i', (x) => x >= 10);
  const rnd = mulberry32(7);
  for (let n = 0; n < 16; n++) {
    const x = 5 + Math.floor(rnd() * 6);
    const y = 6 + Math.floor(rnd() * 26);
    p.set(x, y, rnd() < 0.5 ? 'R' : 'r');
  }
  p.rect(1, 33, 14, 3, 'i').rect(1, 33, 14, 1, 'I').rect(1, 33, 2, 3, 'R').rect(13, 33, 2, 3, 'R');
  p.rect(6, 36, 4, 8, 'l');
  for (let y = 36; y < 44; y += 2) p.rect(6, y, 4, 1, 'L');
  p.disc(7.5, 45.5, 2.5, 2, 'I').set(7, 45, 'J');
  return p;
}

function ironAxe(): Pix {
  const p = new Pix(32, 48);
  p.rect(14, 4, 4, 42, 'W').rect(14, 4, 1, 42, 'V').rect(17, 4, 1, 42, 'w');
  p.poly([[14, 5], [6, 1], [1, 9], [1, 19], [5, 25], [14, 17]], 'I');
  p.shade('I', 'J', (x, y) => x + y * 0.2 < 6).shade('I', 'i', (x, y) => y > 15 && x > 8);
  p.line(2, 9, 2, 19, 'H');
  p.poly([[18, 7], [27, 10], [18, 14]], 'i');
  p.rect(12, 3, 8, 4, 'i').rect(12, 3, 8, 1, 'I');
  p.rect(13, 34, 6, 9, 'L');
  for (let y = 34; y < 43; y += 2) p.rect(13, y, 6, 1, 'l');
  p.rect(13, 45, 6, 2, 'i');
  return p;
}

function ironWand(): Pix {
  const p = new Pix(16, 32);
  p.rect(7, 10, 3, 18, 'i').rect(7, 10, 1, 18, 'I');
  p.rect(5, 8, 7, 3, 'C').rect(5, 8, 7, 1, 'D').rect(6, 18, 5, 2, 'C');
  p.poly([[8, 0], [12, 4], [8, 9], [4, 4]], 'Q').shade('Q', 'q', (x, y) => x >= 8 && y >= 4);
  p.set(7, 2, 'H').set(6, 3, 'H');
  p.disc(8, 29.5, 2.2, 1.8, 'I');
  return p;
}

function copperRing(): Pix {
  const p = new Pix(16, 16);
  p.ring(8, 9.5, 6, 2.4, 'C').shade('C', 'c', (_, y) => y >= 11).shade('C', 'D', (x, y) => x < 6 && y < 9);
  p.disc(8, 3.5, 2.4, 2, 'G').set(7, 3, 'H').set(8, 5, 'g');
  return p;
}

function copperAmulet(): Pix {
  const p = new Pix(16, 16);
  p.ring(8, 6, 6.5, 1, 'J');
  for (let y = 7; y < 16; y++) for (let x = 0; x < 16; x++) if (p.g[y][x] === 'J') p.g[y][x] = '.';
  p.disc(8, 10.5, 4.2, 4.2, 'C').shade('C', 'c', (x, y) => x + y > 20).shade('C', 'D', (x, y) => x + y < 15);
  p.disc(8, 10.5, 2, 2, 'P').set(7, 10, 'H');
  return p;
}

function leatherCap(): Pix {
  const p = new Pix(32, 32);
  p.disc(16, 17, 12, 12, 'L');
  for (let y = 22; y < 32; y++) for (let x = 0; x < 32; x++) if (p.g[y][x] === 'L') p.g[y][x] = '.';
  p.shade('L', 'l', (x, y) => x > 20 || y > 18).shade('L', 'M', (x, y) => x < 12 && y < 12);
  p.rect(3, 21, 26, 5, 'l').rect(3, 21, 26, 1, 'M');
  p.line(16, 5, 16, 20, 'M');
  for (let y = 7; y < 20; y += 3) p.set(15, y, 'l').set(17, y, 'l');
  p.rect(5, 26, 4, 3, 'l').rect(23, 26, 4, 3, 'l');
  p.set(7, 23, 'J').set(24, 23, 'J');
  return p;
}

function ringmailVest(): Pix {
  const p = new Pix(32, 48);
  p.poly([[8, 4], [24, 4], [28, 10], [27, 42], [5, 42], [4, 10]], 'I');
  p.rect(0, 10, 6, 15, 'I').rect(26, 10, 6, 15, 'I');
  for (let y = 0; y < 48; y++) {
    for (let x = 0; x < 32; x++) {
      if (p.g[y][x] !== 'I') continue;
      const link = (x + (y % 2 ? 1 : 0)) % 2 === 0;
      p.g[y][x] = !link ? 'i' : x < 12 ? 'J' : x > 22 ? 'i' : 'I';
    }
  }
  p.disc(16, 3, 5, 4, '.');
  p.ring(16, 3, 6, 1.5, 'L');
  for (let y = 0; y < 3; y++) for (let x = 0; x < 32; x++) if (p.g[y][x] === 'L') p.g[y][x] = '.';
  p.rect(5, 40, 22, 3, 'L').rect(5, 40, 22, 1, 'M');
  p.rect(0, 23, 6, 2, 'L').rect(26, 23, 6, 2, 'L');
  p.rect(14, 26, 4, 3, 'C');
  return p;
}

function linenTrousers(): Pix {
  const p = new Pix(32, 32);
  p.poly([[6, 4], [16, 4], [16, 12], [14, 30], [6, 30]], 'N');
  p.poly([[16, 4], [26, 4], [26, 30], [18, 30], [16, 12]], 'N');
  p.shade('N', 'n', (x) => x > 22 || (x > 12 && x < 16)).shade('N', 'O', (x) => x < 9);
  p.rect(6, 2, 20, 4, 'n').rect(6, 2, 20, 1, 'O');
  p.line(10, 12, 9, 26, 'n').line(21, 12, 22, 26, 'n');
  p.rect(6, 28, 8, 2, 'n').rect(18, 28, 8, 2, 'n');
  p.line(15, 6, 13, 10, 'Y').line(17, 6, 19, 10, 'Y');
  return p;
}

function oakBuckler(): Pix {
  const p = new Pix(32, 32);
  p.disc(15.5, 15.5, 14, 14, 'W');
  p.shade('W', 'w', (x, y) => x + y > 36).shade('W', 'V', (x, y) => x + y < 18);
  for (const x of [10, 15, 21]) for (let y = 2; y < 30; y++) if (p.g[y][x] !== '.') p.g[y][x] = 'w';
  p.ring(15.5, 15.5, 14, 2.2, 'I').shade('I', 'J', (x, y) => x + y < 20).shade('I', 'i', (x, y) => x + y > 40);
  p.disc(15.5, 15.5, 4, 4, 'J').shade('J', 'i', (x, y) => x + y > 33).set(14, 14, 'H').set(13, 14, 'H');
  for (const [x, y] of [[15, 3], [15, 28], [3, 15], [28, 15]]) p.set(x, y, 'H');
  return p;
}

function boot(p: Pix, x0: number, y0: number): void {
  p.rect(x0, y0, 9, 15, 'L');
  p.disc(x0 + 9, y0 + 18, 5.5, 4, 'L').rect(x0, y0 + 14, 10, 8, 'L');
  p.rect(x0, y0 + 21, 15, 2, 'l');
  p.rect(x0 - 1, y0, 11, 3, 'M');
  for (let y = y0 + 5; y < y0 + 13; y += 2) p.set(x0 + 6, y, 'Y').set(x0 + 7, y + 1, 'Y');
  p.shade('L', 'l', (x, y) => x < x0 + 2 && y > y0 + 3);
}

function rawhideBoots(): Pix {
  const p = new Pix(32, 32);
  boot(p, 15, 6);
  boot(p, 3, 3);
  return p;
}

function rawhideGloves(): Pix {
  const p = new Pix(32, 32);
  p.rect(10, 12, 13, 12, 'L');
  for (const [x, y, h] of [[10, 5, 9], [13, 2, 11], [16, 3, 10], [19, 6, 8]]) p.rect(x, y, 3, h, 'L').set(x + 1, y, 'M');
  p.poly([[10, 14], [5, 10], [3, 13], [9, 20]], 'L');
  p.shade('L', 'l', (x) => x > 20).shade('L', 'M', (x, y) => x < 12 && y > 12);
  for (const x of [13, 16, 19]) p.line(x, 10, x, 13, 'l');
  p.rect(9, 24, 15, 6, 'M').rect(9, 26, 15, 2, 'l');
  return p;
}

function ropeBelt(): Pix {
  const p = new Pix(32, 16);
  p.rect(1, 5, 30, 4, 'Y');
  for (let x = 1; x < 31; x++) for (let y = 5; y < 9; y++) if ((x + y) % 3 === 0) p.set(x, y, 'y');
  p.disc(16, 7, 3.6, 3.4, 'Y').set(15, 6, 'O').set(17, 8, 'y').set(16, 9, 'y');
  p.line(15, 10, 12, 15, 'Y').line(17, 10, 20, 15, 'Y');
  p.set(12, 15, 'y').set(20, 15, 'y');
  return p;
}

export const itemIcons: SpriteSpec[] = [
  item('bone_dagger', boneDagger()),
  item('rusted_sword', rustedSword()),
  item('iron_axe', ironAxe()),
  item('iron_wand', ironWand()),
  item('copper_ring', copperRing()),
  item('copper_amulet', copperAmulet()),
  item('leather_cap', leatherCap()),
  item('ringmail_vest', ringmailVest()),
  item('linen_trousers', linenTrousers()),
  item('oak_buckler', oakBuckler()),
  item('rawhide_boots', rawhideBoots()),
  item('rawhide_gloves', rawhideGloves()),
  item('rope_belt', ropeBelt()),
];

// --- crafting currency ------------------------------------------------------------

function currency(name: string, legend: Record<string, string>, p: Pix): SpriteSpec {
  return { name, dir: 'assets/sprites/currency', legend: { k, H: '#ffffff', ...legend }, frames: [{ rows: p.outline('k').rows(), duration: 100 }] };
}

/** A faceted crystal shard (the cheap "Shard" currencies). */
function shard(): Pix {
  const p = new Pix(16, 16);
  p.poly([[8, 0], [12, 5], [11, 15], [5, 15], [4, 6]], 'm');
  p.shade('m', 'd', (x, y) => x >= 9 && y > 4).shade('m', 'l', (x, y) => x <= 6 && y < 10);
  p.line(8, 1, 8, 14, 'l').set(6, 4, 'H').set(7, 2, 'H');
  return p;
}

/** A round orb with a highlight (the rarer "Orb" currencies). */
function orb(): Pix {
  const p = new Pix(16, 16);
  p.disc(7.5, 8, 6.6, 6.6, 'm');
  p.shade('m', 'd', (x, y) => x + y > 17).shade('m', 'l', (x, y) => x + y < 10);
  p.disc(5, 5, 1.6, 1.6, 'l').set(5, 4, 'H').set(4, 5, 'H');
  return p;
}

function kindling(): Pix {
  const p = shard();
  p.set(8, 6, 'y').set(7, 7, 'y').set(8, 8, 'Y').set(9, 7, 'y').set(8, 9, 'y');
  return p;
}

function flux(): Pix {
  const p = shard();
  p.line(6, 11, 10, 8, 'H').line(6, 8, 10, 5, 'l');
  return p;
}

function graft(): Pix {
  const p = shard();
  p.line(8, 4, 8, 12, 'y').line(5, 8, 11, 8, 'y');
  return p;
}

function ash(): Pix {
  const p = orb();
  p.line(4, 10, 8, 8, 'd').line(8, 8, 11, 10, 'd').line(6, 12, 10, 11, 'l');
  return p;
}

function crown(): Pix {
  const p = orb();
  p.rect(4, 7, 8, 3, 'y').set(4, 5, 'y').set(4, 6, 'y').set(8, 4, 'y').set(8, 5, 'y').set(8, 6, 'y').set(11, 5, 'y').set(11, 6, 'y');
  p.set(8, 8, 'r').set(6, 8, 'b');
  return p;
}

function tempest(): Pix {
  const p = orb();
  p.line(9, 2, 6, 8, 'y').line(6, 8, 9, 8, 'y').line(9, 8, 6, 14, 'y').set(8, 5, 'H');
  return p;
}

function tuning(): Pix {
  const p = orb();
  p.ring(7.5, 8, 4.2, 1, 'H');
  p.set(7, 8, 'y').set(8, 8, 'y');
  return p;
}

function sundering(): Pix {
  const p = orb();
  p.line(7, 1, 6, 5, 'k').line(6, 5, 9, 9, 'k').line(9, 9, 7, 14, 'k').line(9, 9, 12, 10, 'k');
  return p;
}

function ascendant(): Pix {
  const p = orb();
  p.line(7, 3, 7, 13, 'y').line(8, 3, 8, 13, 'y').line(2, 8, 13, 8, 'y').line(3, 7, 12, 7, 'y');
  p.set(7, 7, 'H').set(8, 8, 'H').set(7, 8, 'H').set(8, 7, 'H');
  return p;
}

function dust(seed: number): Pix {
  const p = new Pix(16, 16);
  p.poly([[1, 15], [4, 10], [8, 7], [12, 10], [15, 15]], 'm');
  p.shade('m', 'd', (x, y) => x > 9 && y > 11).shade('m', 'l', (x, y) => x < 7 && y < 12);
  const rnd = mulberry32(seed);
  for (let n = 0; n < 5; n++) p.set(3 + Math.floor(rnd() * 10), 9 + Math.floor(rnd() * 5), 'l');
  p.set(4, 4, 'H').set(11, 3, 'l').set(13, 6, 'H');
  return p;
}

function seal(): Pix {
  const p = new Pix(16, 16);
  p.disc(8, 8, 7, 7, 'm');
  for (const [x, y] of [[1, 5], [14, 9], [4, 14], [12, 2]]) p.disc(x, y, 1.5, 1.5, 'm');
  p.shade('m', 'd', (x, y) => x + y > 18).shade('m', 'l', (x, y) => x + y < 10);
  p.ring(8, 8, 4.5, 1, 'd');
  p.rect(7, 5, 2, 6, 'y').rect(5, 7, 6, 2, 'y');
  return p;
}

function gold(): Pix {
  const p = new Pix(16, 16);
  for (const [x, y] of [[7, 12], [7, 10], [7, 8], [3, 12], [11, 12]]) {
    p.disc(x + 0.5, y + 0.5, 3.5, 1.6, 'm');
    p.rect(x - 3, y + 1, 7, 1, 'd');
  }
  p.disc(7.5, 4.5, 3, 3, 'm').ring(7.5, 4.5, 3, 1, 'd').set(6, 3, 'H');
  return p;
}

const orbLegend = (d: string, m: string, l: string, extra: Record<string, string> = {}) => ({ d, m, l, y: '#ffe070', Y: '#fff8c0', ...extra });

export const currencyIcons: SpriteSpec[] = [
  currency('kindling_shard', orbLegend('#a8401a', '#ff8a2a', '#ffc060'), kindling()),
  currency('flux_shard', orbLegend('#1e4a9a', '#4a86e0', '#9ac8ff'), flux()),
  currency('graft_shard', orbLegend('#1e7a4a', '#4ac084', '#a8f0c8'), graft()),
  currency('ash_orb', orbLegend('#4a4644', '#8a8480', '#c8c2bc'), ash()),
  currency('crown_orb', orbLegend('#8a5a10', '#d8a838', '#ffe090', { r: '#c02a2a', b: '#3a6ad8' }), crown()),
  currency('tempest_orb', orbLegend('#4a1e8a', '#8a4ad8', '#c8a0ff'), tempest()),
  currency('tuning_orb', orbLegend('#8a7a3a', '#d8cc88', '#fff4c8'), tuning()),
  currency('sundering_orb', orbLegend('#7a1414', '#c83030', '#ff8a7a'), sundering()),
  currency('ascendant_orb', orbLegend('#6a7a9a', '#d8e4f4', '#ffffff'), ascendant()),
  currency('grey_dust', orbLegend('#5a5a60', '#9a9aa4', '#d8d8e0'), dust(1)),
  currency('azure_dust', orbLegend('#1e3a8a', '#4a72d8', '#a8c4ff'), dust(2)),
  currency('gilded_dust', orbLegend('#8a6410', '#d8a838', '#fff0a0'), dust(3)),
  currency('prismatic_dust', orbLegend('#8a2a7a', '#e05ac0', '#a8f0ff'), dust(4)),
  currency('artificer_seal', orbLegend('#8a1a1a', '#c8382a', '#f07a5a'), seal()),
  currency('gold', orbLegend('#8a5a10', '#e8b83a', '#fff0a0'), gold()),
];

export const items = [...itemIcons, ...currencyIcons];
