/**
 * Launcher artwork — docs/design/distribution.md -> "The launcher".
 * `npx tsx launcher.ts` from tools/art writes:
 *   tools/launcher/Assets/background.png  480×270 pixel-art key art (drawn ×2 by the launcher)
 *   tools/launcher/Assets/icon.ico        window/exe icon (the Arcanist's portrait)
 *
 * A heroes-versus-monsters scene composed from the game's own sprites: the
 * party on the left in warm gold light, the horde and the Ember Colossus on the
 * right in hellfire red, a rift of light splitting them with a "VS". Light is
 * accumulated in floating point (ambient + coloured point lights + rim light
 * from the rift on every figure) and quantised back to 8-bit, so it stays
 * crisp pixel art with soft glows.
 */
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { buildDocument, encodePng, mulberry32, PROJECT_ROOT, type SpriteSpec } from './lib';
import { compositeFrame } from './vendor/easypixel/packages/core/src/index';
import { all } from './sprites/index';
import { arcanistCast, bruteRear, rogueLeap, sageRead, warriorLunge, type Painted } from './heroes';

const W = 480;
const H = 270;
const HORIZON = 146;
/** Everything sits this much higher than the ground line would suggest: the launcher's bottom bar covers the last ~60 px. */
const LIFT = 26;
const rand = mulberry32(7);

type RGB = [number, number, number];
const img = new Float32Array(W * H * 3);
const mask = new Uint8Array(W * H); // 1 = a figure covers the pixel (rim light pass)

const hex = (h: string): RGB => [parseInt(h.slice(1, 3), 16) / 255, parseInt(h.slice(3, 5), 16) / 255, parseInt(h.slice(5, 7), 16) / 255];
const mix = (a: RGB, b: RGB, t: number): RGB => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];

function set(x: number, y: number, c: RGB): void {
  if (x < 0 || y < 0 || x >= W || y >= H) return;
  const i = (y * W + x) * 3;
  img[i] = c[0];
  img[i + 1] = c[1];
  img[i + 2] = c[2];
}

function get(x: number, y: number): RGB {
  const i = (Math.max(0, Math.min(H - 1, y)) * W + Math.max(0, Math.min(W - 1, x))) * 3;
  return [img[i], img[i + 1], img[i + 2]];
}

function add(x: number, y: number, c: RGB, k: number): void {
  if (x < 0 || y < 0 || x >= W || y >= H) return;
  const i = (y * W + x) * 3;
  img[i] += c[0] * k;
  img[i + 1] += c[1] * k;
  img[i + 2] += c[2] * k;
}

/** Additive radial light with a quadratic falloff (ry squashes it into the floor plane). */
function glow(cx: number, cy: number, r: number, c: RGB, k: number, ry = r): void {
  for (let y = Math.floor(cy - ry); y <= cy + ry; y++) {
    for (let x = Math.floor(cx - r); x <= cx + r; x++) {
      const d = Math.hypot((x - cx) / r, (y - cy) / ry);
      if (d < 1) add(x, y, c, k * (1 - d) * (1 - d));
    }
  }
}

// --- backdrop -------------------------------------------------------------------------------

const skyTop = hex('#07050e');
const skyMid = hex('#1a0f2c');
for (let y = 0; y < HORIZON; y++) {
  for (let x = 0; x < W; x++) {
    const t = y / HORIZON;
    // Cooler on the heroes' side, hotter on the monsters'.
    const side = x / W;
    let c = mix(skyTop, skyMid, t * t);
    c = mix(c, side < 0.5 ? hex('#0c1630') : hex('#2a0c14'), Math.abs(side - 0.5) * 0.9 * t);
    set(x, y, c);
  }
}

// Distant crypt: arches and pillars in silhouette, a slightly lighter far layer.
function arches(baseY: number, span: number, height: number, color: RGB, offset: number): void {
  for (let x = 0; x < W; x++) {
    const local = ((x + offset) % span) / span; // 0..1 across one arch
    const pillar = local < 0.18 || local > 0.82;
    const archTop = baseY - height + Math.round(Math.sqrt(Math.max(0, 1 - ((local - 0.5) / 0.32) ** 2)) * -height * 0.35);
    const top = pillar ? baseY - height - 6 : archTop;
    for (let y = Math.max(0, top); y < HORIZON; y++) {
      // The arch's opening stays sky below its curve.
      if (!pillar && y > archTop + 4 && y < baseY - 6) continue;
      set(x, y, color);
    }
  }
}

arches(HORIZON, 96, 92, hex('#140d20'), 20);
arches(HORIZON, 64, 58, hex('#0e0918'), 52);

// A far moon-glow behind the rift.
glow(240, 40, 120, hex('#5a3a9a'), 0.35, 70);

// --- floor ----------------------------------------------------------------------------------

const vanishX = 240;
const vanishY = HORIZON - 40;
for (let y = HORIZON; y < H; y++) {
  const depth = (y - HORIZON) / (H - HORIZON); // 0 far .. 1 near
  for (let x = 0; x < W; x++) {
    let c = mix(hex('#120c1a'), hex('#2a2032'), depth);
    // Perspective tile seams: rows spaced by depth, columns converging on the vanishing point.
    const rowPhase = (Math.log(1 + depth * 9) * 3.2) % 1;
    const colPos = (x - vanishX) / (y - vanishY) * 6;
    const colPhase = ((colPos % 1) + 1) % 1;
    if (rowPhase < 0.08 + (1 - depth) * 0.05 || colPhase < 0.06) c = mix(c, hex('#0a0710'), 0.7);
    // Speckle.
    if (rand() < 0.04) c = mix(c, hex('#3a2e44'), 0.5);
    set(x, y, c);
  }
}

// --- figures --------------------------------------------------------------------------------

function spec(name: string): SpriteSpec {
  const s = all.find((x) => x.name === name);
  if (!s) throw new Error(`no sprite ${name}`);
  return s;
}

interface Figure {
  name: string;
  frame?: number;
  /** Feet centre on the canvas. */
  x: number;
  y: number;
  scale: number;
  flip?: boolean;
  /** 0..1: how much the figure sinks into shadow (back rows). */
  dim?: number;
  /** Paint as a dark silhouette where only hot/bright details (eyes, molten
   * cracks, magic) glow: background monsters, whose low-res sprites would
   * otherwise clash with the illustrated heroes. */
  silhouette?: boolean;
}

const ambientLeft = hex('#8a8fb8');
const ambientRight = hex('#b0707a');

function blit(f: Figure): void {
  const doc = buildDocument(spec(f.name));
  const px = compositeFrame(doc, f.frame ?? 0);
  // Contact shadow first.
  for (let dy = -3; dy <= 3; dy++) {
    for (let dx = -doc.width * f.scale * 0.45; dx <= doc.width * f.scale * 0.45; dx++) {
      const d = Math.hypot(dx / (doc.width * f.scale * 0.45), dy / 3);
      if (d < 1) {
        const [r, g, b] = get(Math.round(f.x + dx), Math.round(f.y + dy));
        set(Math.round(f.x + dx), Math.round(f.y + dy), [r * (0.4 + d * 0.6), g * (0.4 + d * 0.6), b * (0.4 + d * 0.6)]);
      }
    }
  }

  const left = Math.round(f.x - (doc.width * f.scale) / 2);
  const top = Math.round(f.y - doc.height * f.scale);
  const side = f.x / W;
  const ambient = mix(ambientLeft, ambientRight, side);
  for (let sy = 0; sy < doc.height; sy++) {
    for (let sx = 0; sx < doc.width; sx++) {
      const srcX = f.flip ? doc.width - 1 - sx : sx;
      const i = (sy * doc.width + srcX) * 4;
      if (px[i + 3] < 128) continue;
      const base: RGB = [px[i] / 255, px[i + 1] / 255, px[i + 2] / 255];
      const shade = 1 - (f.dim ?? 0) * 0.55;
      let c: RGB = [base[0] * ambient[0] * shade, base[1] * ambient[1] * shade, base[2] * ambient[2] * shade];
      if (f.silhouette) {
        const hot = base[0] > 0.75 && base[0] > base[2] * 1.6 && base[1] > 0.3;
        const bright = base[0] + base[1] + base[2] > 2.4;
        const magic = base[2] > 0.7 && base[2] > base[1] * 1.3;
        c = hot || bright || magic ? [base[0] * 1.5, base[1] * 1.3, base[2] * 1.3] : mix(hex('#150b16'), base, 0.16);
      }
      for (let oy = 0; oy < f.scale; oy++) {
        for (let ox = 0; ox < f.scale; ox++) {
          const x = left + sx * f.scale + ox;
          const y = top + sy * f.scale + oy;
          set(x, y, c);
          if (x >= 0 && y >= 0 && x < W && y < H) mask[y * W + x] = 1;
        }
      }
    }
  }
}

// Back to front. Monsters face left (towards the heroes). The front line meets
// in the middle — the warrior and the rogue mid-swing into the horde — while
// the casters hang back charging their spells.
const CLASH_X = 236;
const figures: Figure[] = [
  { name: 'ember_colossus', frame: 1, x: 410, y: 196 - LIFT, scale: 4, flip: true, silhouette: true },
  { name: 'bat', x: 288, y: 84, scale: 3, flip: true, silhouette: true },
  { name: 'bat', frame: 1, x: 362, y: 66 - LIFT, scale: 2, flip: true, silhouette: true },
  { name: 'caster', x: 456, y: 216 - LIFT, scale: 3, flip: true, silhouette: true },
  { name: 'skeleton', x: 362, y: 222 - LIFT, scale: 3, flip: true, silhouette: true },
  { name: 'slime', x: 402, y: 246 - LIFT, scale: 3, flip: true, silhouette: true },

  { name: 'imp', x: 326, y: 256 - LIFT, scale: 3, flip: true, silhouette: true },
];

/** Illustrated figures (tools/art/heroes.ts), placed by their feet. */
const painted: { fig: Painted; x: number; y: number; dim?: number }[] = [
  { fig: sageRead(), x: 52, y: 214 - LIFT, dim: 0.15 },
  { fig: arcanistCast(), x: 118, y: 228 - LIFT },
  { fig: bruteRear(), x: 292, y: 238 - LIFT },
  { fig: warriorLunge(), x: 206, y: 244 - LIFT },
  // Mid-air, driving a dagger into a bat.
  { fig: rogueLeap(), x: 212, y: 118 },
];

function blitPainted(f: Painted, fx: number, fy: number, dim = 0): void {
  const rgb = (c: string): RGB => hex(f.legend[c] ?? '#ff00ff');
  const left = Math.round(fx - f.anchor[0]);
  const top = Math.round(fy - f.anchor[1]);
  const ambient = mix(ambientLeft, ambientRight, fx / W);
  const width = f.rows[0].length;
  // Contact shadow.
  for (let dy = -3; dy <= 3; dy++) {
    for (let dx = -16; dx <= 16; dx++) {
      const d = Math.hypot(dx / 16, dy / 3);
      if (d < 1) {
        const [r, g, b] = get(Math.round(fx + dx), Math.round(fy + dy));
        set(Math.round(fx + dx), Math.round(fy + dy), [r * (0.45 + d * 0.55), g * (0.45 + d * 0.55), b * (0.45 + d * 0.55)]);
      }
    }
  }
  f.rows.forEach((row, y) => {
    for (let x = 0; x < width; x++) {
      const c = row[x];
      if (c === '.') continue;
      const base = rgb(c);
      const shade = 1 - dim * 0.55;
      set(left + x, top + y, [base[0] * ambient[0] * shade * 1.1, base[1] * ambient[1] * shade * 1.1, base[2] * ambient[2] * shade * 1.1]);
      const px = left + x;
      const py = top + y;
      if (px >= 0 && py >= 0 && px < W && py < H) mask[py * W + px] = 1;
    }
  });
}

// Casters' ground runes go under everyone (drawn before the figures).
function ring(cx: number, cy: number, r: number, c: RGB, k: number): void {
  for (let a = 0; a < Math.PI * 2; a += 0.004) {
    const x = Math.round(cx + Math.cos(a) * r);
    const y = Math.round(cy + Math.sin(a) * r * 0.32);
    add(x, y, c, k);
  }
}
function runeCircle(cx: number, cy: number, r: number, c: RGB): void {
  ring(cx, cy, r, c, 0.28);
  ring(cx, cy, r * 0.72, c, 0.16);
  for (let i = 0; i < 12; i++) {
    const a = (i / 12) * Math.PI * 2;
    const x = Math.round(cx + Math.cos(a) * r * 0.86);
    const y = Math.round(cy + Math.sin(a) * r * 0.86 * 0.32);
    add(x, y, c, 0.7);
    add(x + 1, y, c, 0.35);
  }
  glow(cx, cy, r * 1.3, c, 0.35, r * 0.45);
}
runeCircle(118, 228 - LIFT, 34, hex('#5ad8ff'));
runeCircle(52, 214 - LIFT, 24, hex('#7aff9a'));

for (const f of figures) blit(f);
for (const f of painted) blitPainted(f.fig, f.x, f.y, f.dim);

// --- light ----------------------------------------------------------------------------------

const gold = hex('#ffc860');
const ember = hex('#ff5a28');
const arcane = hex('#7ae8ff');
const nature = hex('#8aff9e');
const white = hex('#fff6e6');

// The heroes' warm light and the horde's hellfire.
glow(100, 214 - LIFT, 150, gold, 0.36, 80);
glow(400, 200 - LIFT, 170, ember, 0.5, 95);
glow(410, 140 - LIFT, 46, hex('#ff8a3a'), 0.55); // the Colossus' molten core

// The Arcanist charges a big orb at her staff tip; the Sage's tome glows.
// Positions follow the painted figures: left = x - anchor, top = y - anchor.
const orb = { x: 98, y: 77 };
glow(orb.x, orb.y, 60, arcane, 0.5);
glow(orb.x, orb.y, 18, arcane, 1.1);
glow(orb.x, orb.y, 7, white, 1.6);
for (let i = 0; i < 14; i++) {
  // Wisps spiralling into the orb.
  const a = (i / 14) * Math.PI * 2;
  const d = 14 + rand() * 18;
  for (let t = 0; t < 1; t += 0.12) {
    add(Math.round(orb.x + Math.cos(a + t * 1.4) * d * (1 - t * 0.6)), Math.round(orb.y + Math.sin(a + t * 1.4) * d * (1 - t * 0.6)), arcane, 0.5 * (1 - t));
  }
}
glow(150, 123, 14, arcane, 0.9); // the casting palm
glow(81, 111, 22, nature, 0.8); // the Sage's tome
for (let i = 0; i < 18; i++) add(Math.round(70 + rand() * 24), Math.round(88 + rand() * 24), nature, 0.9 + rand());

// The enemy caster answers with a dark bolt of its own.
glow(440, 186 - LIFT, 20, hex('#c46aff'), 0.9);

// The clash: a slash arc from the warrior's swing, sparks where it bites.
function arc(cx: number, cy: number, r: number, from: number, to: number, thick: number, c: RGB, k: number): void {
  for (let a = from; a <= to; a += 0.01) {
    const fade = (a - from) / (to - from); // bright at the leading edge
    for (let w = 0; w < thick * fade + 1; w++) {
      add(Math.round(cx + Math.cos(a) * (r - w)), Math.round(cy + Math.sin(a) * (r - w)), c, k * (0.3 + fade * 0.9));
    }
  }
}
const hit = { x: 266, y: 116 };
arc(206, 143, 50, -2.5, -0.35, 5, white, 0.9);
arc(206, 143, 43, -2.2, -0.45, 2, gold, 0.6);
glow(hit.x, hit.y, 40, white, 0.55);
glow(hit.x, hit.y, 12, white, 1.2);
for (let i = 0; i < 16; i++) {
  const a = rand() * Math.PI * 2;
  const len = 6 + rand() * 18;
  for (let t = 0; t < len; t += 0.7) {
    add(Math.round(hit.x + Math.cos(a) * t), Math.round(hit.y + Math.sin(a) * t), mix(gold, white, rand()), 1.1 * (1 - t / len));
  }
}
// The rogue's quick stab into the imp.
const stab = { x: 272, y: 70 };
arc(250, 74, 22, -0.8, 0.3, 3, hex('#d8f0ff'), 0.8);
glow(stab.x, stab.y, 16, hex('#d8f0ff'), 0.8);

// Where the two lines meet the floor brightens.
glow(CLASH_X, 222 - LIFT, 90, mix(gold, white, 0.4), 0.3, 18);

// Rim light from the clash: figure pixels whose neighbour towards it is empty catch it.
for (let y = 0; y < H; y++) {
  for (let x = 0; x < W; x++) {
    if (!mask[y * W + x]) continue;
    const toward = x < CLASH_X ? 1 : -1;
    const n = x + toward;
    if (n >= 0 && n < W && !mask[y * W + n]) {
      const near = 1 - Math.min(1, Math.abs(x - CLASH_X) / 220);
      add(x, y, x < CLASH_X ? gold : ember, 0.25 + near * 0.6);
    }
    if (y > 0 && !mask[(y - 1) * W + x]) add(x, y, hex('#c8b8ff'), 0.1);
  }
}

// Embers drifting up on the monsters' side, motes of light on the heroes'.
for (let i = 0; i < 90; i++) {
  const right = rand() < 0.6;
  const x = Math.round(right ? 250 + rand() * 230 : rand() * 230);
  const y = Math.round(20 + rand() * 200);
  const c = right ? mix(ember, gold, rand() * 0.6) : mix(gold, hex('#bfe8ff'), rand());
  add(x, y, c, 0.9 + rand() * 0.8);
  if (rand() < 0.3) add(x, y - 1, c, 0.4);
}

// --- vignette and output --------------------------------------------------------------------

const out = new Uint8ClampedArray(W * H * 4);
for (let y = 0; y < H; y++) {
  for (let x = 0; x < W; x++) {
    const v = Math.hypot((x - W / 2) / (W * 0.62), (y - H * 0.45) / (H * 0.7));
    const k = Math.max(0.35, 1 - Math.max(0, v - 0.55) * 1.4);
    const [r, g, b] = get(x, y);
    // Soft tonemap so stacked lights saturate instead of clipping flat.
    const tm = (c: number) => Math.round(255 * (1 - Math.exp(-c * 1.15)) / (1 - Math.exp(-1.15)));
    const i = (y * W + x) * 4;
    out[i] = tm(r * k);
    out[i + 1] = tm(g * k);
    out[i + 2] = tm(b * k);
    out[i + 3] = 255;
  }
}

function write(path: string, data: Buffer): void {
  const full = join(PROJECT_ROOT, path);
  mkdirSync(dirname(full), { recursive: true });
  writeFileSync(full, data);
  console.log(`wrote ${path} (${data.length} bytes)`);
}

write('tools/launcher/Assets/background.png', encodePng({ width: W, height: H, data: out }));

// --- icon: the Arcanist's portrait as a PNG-in-ICO (64 and 256 px) -------------------------

const portrait = all.find((s) => s.name === 'player' && s.dir === 'assets/portraits');
if (portrait) {
  const doc = buildDocument(portrait);
  const px = compositeFrame(doc, 0);
  const scaled = (factor: number): Buffer => {
    const w = doc.width * factor;
    const data = new Uint8ClampedArray(w * w * 4);
    for (let y = 0; y < w; y++) for (let x = 0; x < w; x++) data.set(px.subarray(((Math.floor(y / factor) * doc.width) + Math.floor(x / factor)) * 4, ((Math.floor(y / factor) * doc.width) + Math.floor(x / factor)) * 4 + 4), (y * w + x) * 4);
    return encodePng({ width: w, height: w, data });
  };
  const images = [scaled(1), scaled(4)];
  const header = Buffer.alloc(6 + 16 * images.length);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(images.length, 4);
  let offset = header.length;
  images.forEach((png, i) => {
    const size = i === 0 ? 64 : 0; // 0 means 256
    const e = 6 + i * 16;
    header.writeUInt8(size, e);
    header.writeUInt8(size, e + 1);
    header.writeUInt16LE(1, e + 4);
    header.writeUInt16LE(32, e + 6);
    header.writeUInt32LE(png.length, e + 8);
    header.writeUInt32LE(offset, e + 12);
    offset += png.length;
  });
  write('tools/launcher/Assets/icon.ico', Buffer.concat([header, ...images]));
}
