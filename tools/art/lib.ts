/**
 * Thin authoring layer over EasyPixel's core. Every sprite in the game is an
 * EasyPixel document built with the same `draw_grid` semantics the editor's MCP
 * server exposes (one string per row, one char per pixel, a legend of chars to
 * hex colours), then exported with EasyPixel's own Godot exporter
 * (PNG spritesheet + SpriteFrames .tres + AnimatedSprite2D .tscn).
 *
 * The `.epx.json` source of every sprite is written next to the art sources in
 * `assets/art_src/`, so any of them can be opened in the EasyPixel app, edited
 * by hand, and re-exported over the generated files.
 */
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { deflateSync, inflateSync } from 'node:zlib';
import {
  addFrame,
  addTag,
  createDocument,
  documentToJson,
  exportGodot,
  exportGodotTileset,
  gridToPixels,
  parseHex,
  setPixels,
  type PixelRegion,
  type SpriteDocument,
  type TagDirection,
} from './vendor/easypixel/packages/core/src/index';

export const PROJECT_ROOT = join(import.meta.dirname, '..', '..');

export interface FrameSpec {
  rows: string[];
  /** Milliseconds. */
  duration?: number;
}

export interface AnimSpec {
  name: string;
  from: number;
  to: number;
  loop?: boolean;
  direction?: TagDirection;
}

export interface SpriteSpec {
  name: string;
  /** Project-relative folder, e.g. "assets/sprites/enemies". */
  dir: string;
  legend: Record<string, string>;
  frames: FrameSpec[];
  animations?: AnimSpec[];
  autoplay?: string;
  /** Tileset export instead of a sprite export. */
  tileset?: { tileWidth: number; tileHeight: number };
}

function validate(spec: SpriteSpec): { width: number; height: number } {
  const height = spec.frames[0].rows.length;
  const width = [...spec.frames[0].rows[0]].length;
  spec.frames.forEach((f, fi) => {
    if (f.rows.length !== height) throw new Error(`${spec.name} frame ${fi}: ${f.rows.length} rows, expected ${height}`);
    f.rows.forEach((r, ri) => {
      if ([...r].length !== width) {
        throw new Error(`${spec.name} frame ${fi} row ${ri}: width ${[...r].length}, expected ${width}: "${r}"`);
      }
    });
  });
  return { width, height };
}

export function buildDocument(spec: SpriteSpec): SpriteDocument {
  const { width, height } = validate(spec);
  const palette = [...new Set(Object.values(spec.legend).filter((v) => v.startsWith('#')))].map(parseHex);
  let doc = createDocument({ name: spec.name, width, height, palette });
  const layerId = doc.layers[0].id;
  spec.frames.forEach((frame, i) => {
    if (i > 0) doc = addFrame(doc, i, frame.duration).doc;
    const frameId = doc.frames[i].id;
    if (frame.duration) doc.frames[i] = { ...doc.frames[i], duration: frame.duration };
    doc = setPixels(doc, layerId, frameId, gridToPixels({ rows: frame.rows, legend: spec.legend }, palette));
  });
  for (const a of spec.animations ?? []) {
    doc = addTag(doc, { name: a.name, from: a.from, to: a.to, loop: a.loop ?? true, direction: a.direction }).doc;
  }
  return doc;
}

// --- PNG encoding (RGBA8, no filtering) --------------------------------------

const CRC_TABLE = (() => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c >>> 0;
  }
  return t;
})();

function crc32(buf: Buffer): number {
  let c = 0xffffffff;
  for (const b of buf) c = CRC_TABLE[(c ^ b) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

function chunk(type: string, data: Buffer): Buffer {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(td));
  return Buffer.concat([len, td, crc]);
}

export function encodePng(region: PixelRegion): Buffer {
  const { width, height, data } = region;
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8;
  ihdr[9] = 6;
  const raw = Buffer.alloc((width * 4 + 1) * height);
  for (let y = 0; y < height; y++) {
    raw[y * (width * 4 + 1)] = 0;
    Buffer.from(data.buffer, data.byteOffset + y * width * 4, width * 4).copy(raw, y * (width * 4 + 1) + 1);
  }
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', deflateSync(raw)),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

function write(rel: string, content: string | Buffer): void {
  const abs = join(PROJECT_ROOT, rel);
  mkdirSync(dirname(abs), { recursive: true });
  writeFileSync(abs, content);
}

/** Builds, exports to Godot, and saves the editable .epx.json source. */
export function emit(spec: SpriteSpec): void {
  const doc = buildDocument(spec);
  const resDir = `res://${spec.dir}`;
  write(join('assets/art_src', spec.dir.replace(/^assets\//, ''), `${spec.name}.epx.json`), documentToJson(doc));
  if (spec.tileset) {
    const out = exportGodotTileset(doc, { resDir, baseName: spec.name, ...spec.tileset });
    write(join(spec.dir, out.pngName), encodePng(out.image));
    for (const f of out.files) write(join(spec.dir, f.name), f.text);
    return;
  }
  const out = exportGodot(doc, { resDir, baseName: spec.name, autoplay: spec.autoplay });
  write(join(spec.dir, out.pngName), encodePng(out.sheet));
  for (const f of out.files) write(join(spec.dir, f.name), f.text);
}

// --- Grid helpers -------------------------------------------------------------

/** Mirrors left-half rows into full rows. `odd` keeps a centre column. */
export function sym(half: string[], odd = false): string[] {
  return half.map((r) => {
    const chars = [...r];
    const mirrored = [...chars].reverse();
    return (odd ? chars.concat(mirrored.slice(1)) : chars.concat(mirrored)).join('');
  });
}

/** Shifts a grid by (dx, dy), filling with transparency. */
export function shift(rows: string[], dx: number, dy: number): string[] {
  const h = rows.length;
  const w = [...rows[0]].length;
  const out: string[] = [];
  for (let y = 0; y < h; y++) {
    let line = '';
    for (let x = 0; x < w; x++) {
      const sy = y - dy;
      const sx = x - dx;
      line += sy >= 0 && sy < h && sx >= 0 && sx < w ? [...rows[sy]][sx] : '.';
    }
    out.push(line);
  }
  return out;
}

/** Replaces rows starting at `at` with `patch` (same width). */
export function patch(rows: string[], at: number, patchRows: string[]): string[] {
  const out = [...rows];
  patchRows.forEach((r, i) => (out[at + i] = r));
  return out;
}

/** Character substitution, e.g. recolour an outline for a hit/variant frame. */
export function recolor(rows: string[], map: Record<string, string>): string[] {
  return rows.map((r) => [...r].map((c) => map[c] ?? c).join(''));
}

export function flipH(rows: string[]): string[] {
  return rows.map((r) => [...r].reverse().join(''));
}

/** Small deterministic PRNG for procedural tiles. */
export function mulberry32(seed: number): () => number {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

// --- PNG sources (art made outside this pipeline) ----------------------------
// docs/design/art-pipeline.md -> "HD-2D with flat figures": some art now comes from
// tools/comfyui (generated, approved, cleaned by hand). Those PNGs are read back as
// draw_grid rows so the sprite or tile still goes through the same document, Godot
// export and voxel export as everything else.

/** Decodes an 8-bit, non-interlaced RGB or RGBA PNG. */
export function decodePng(path: string): { width: number; height: number; data: Uint8Array } {
  const buf = readFileSync(join(PROJECT_ROOT, path));
  let pos = 8;
  let width = 0;
  let height = 0;
  let colorType = 6;
  const idat: Buffer[] = [];
  while (pos < buf.length) {
    const len = buf.readUInt32BE(pos);
    const type = buf.toString('ascii', pos + 4, pos + 8);
    const body = buf.subarray(pos + 8, pos + 8 + len);
    if (type === 'IHDR') {
      width = body.readUInt32BE(0);
      height = body.readUInt32BE(4);
      colorType = body[9];
      if (body[8] !== 8 || body[12] !== 0 || (colorType !== 6 && colorType !== 2)) {
        throw new Error(`${path}: only 8-bit non-interlaced RGB/RGBA PNGs are supported`);
      }
    } else if (type === 'IDAT') {
      idat.push(body);
    } else if (type === 'IEND') {
      break;
    }
    pos += 12 + len;
  }
  const bpp = colorType === 6 ? 4 : 3;
  const raw = inflateSync(Buffer.concat(idat));
  const stride = width * bpp;
  const pixels = new Uint8Array(height * stride);
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (stride + 1)];
    const line = raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1));
    for (let x = 0; x < stride; x++) {
      const a = x >= bpp ? pixels[y * stride + x - bpp] : 0;
      const b = y > 0 ? pixels[(y - 1) * stride + x] : 0;
      const c = x >= bpp && y > 0 ? pixels[(y - 1) * stride + x - bpp] : 0;
      let v = line[x];
      if (filter === 1) v += a;
      else if (filter === 2) v += b;
      else if (filter === 3) v += (a + b) >> 1;
      else if (filter === 4) {
        const p = a + b - c;
        const pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
        v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
      }
      pixels[y * stride + x] = v & 255;
    }
  }
  const data = new Uint8Array(width * height * 4);
  for (let i = 0; i < width * height; i++) {
    data[i * 4] = pixels[i * bpp];
    data[i * 4 + 1] = pixels[i * bpp + 1];
    data[i * 4 + 2] = pixels[i * bpp + 2];
    data[i * 4 + 3] = bpp === 4 ? pixels[i * bpp + 3] : 255;
  }
  return { width, height, data };
}

const LEGEND_CHARS = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!$%&*+-=?@^~<>/|:;,{}[]()';

/**
 * A region of a PNG as draw_grid rows ('.' = transparent). Colours missing from
 * `legend` are added to it under unused characters, so the rows can share a legend
 * with hand-typed art (a tileset).
 */
export function pngGrid(path: string, legend: Record<string, string>, box?: { x: number; y: number; w: number; h: number }): string[][] {
  const img = decodePng(path);
  const { x: bx, y: by, w, h } = box ?? { x: 0, y: 0, w: img.width, h: img.height };
  const charOf = new Map<string, string>();
  for (const [ch, hex] of Object.entries(legend)) charOf.set(hex.toLowerCase(), ch);
  const grid: string[][] = [];
  for (let y = 0; y < h; y++) {
    const row: string[] = [];
    for (let x = 0; x < w; x++) {
      const i = ((by + y) * img.width + bx + x) * 4;
      if (img.data[i + 3] < 128) {
        row.push('.');
        continue;
      }
      const hex = '#' + [0, 1, 2].map((k) => img.data[i + k].toString(16).padStart(2, '0')).join('');
      let ch = charOf.get(hex);
      if (!ch) {
        ch = [...LEGEND_CHARS].find((c) => !(c in legend));
        if (!ch) throw new Error(`${path}: too many colours for one legend`);
        legend[ch] = hex;
        charOf.set(hex, ch);
      }
      row.push(ch);
    }
    grid.push(row);
  }
  return grid;
}
