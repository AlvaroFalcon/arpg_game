/**
 * Voxel export for the HD-2D view — docs/design/art-pipeline.md -> "Voxels".
 * `npx tsx voxels.ts [name,filter]` from tools/art.
 *
 * Every animated sprite (player and classes, enemies, bosses, props,
 * projectiles) goes through EasyPixel's voxel export (`documentToVoxels` in
 * `inflate` mode + `voxelMesh`): each animation frame becomes a puffy voxel
 * model, and all frames of a sprite are packed into one binary glTF with one
 * node per frame, named `f<frame index>` (the frame's index in the EasyPixel
 * document, which is also its cell in the exported spritesheet). Godot imports
 * the .glb natively; the game swaps meshes in step with the 2D animation.
 *
 * Tilesets become terrain pieces built from the tile pixels as voxel models:
 * floor slabs, lower liquid slabs, tall brick wall blocks and the rune floor.
 * The 2D art stays the single source: change a sprite, re-run this, and the
 * voxel version follows.
 */
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { buildDocument, PROJECT_ROOT, type SpriteSpec } from './lib';
import {
  compositeFrame,
  documentToVoxels,
  voxelMesh,
  type VoxelMesh,
  type VoxelModel,
} from './vendor/easypixel/packages/core/src/index';
import { all } from './sprites/index';
import { tiles } from './sprites/tiles';

const VOXEL_SIZE = 1 / 16; // one art pixel = 1/16 m; a 16 px tile = 1 m

// ---------------------------------------------------------------------------
// Multi-node binary glTF (one mesh + node per frame, materials shared by colour)
// ---------------------------------------------------------------------------

function srgbToLinear(c: number): number {
  return c <= 0.04045 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
}

function writeGlbFrames(meshes: VoxelMesh[], names: string[], emissive?: (hex: string) => boolean): Uint8Array {
  const chunks: Uint8Array[] = [];
  let length = 0;
  const bufferViews: object[] = [];
  const accessors: object[] = [];
  const materials: object[] = [];
  const materialOf = new Map<string, number>();
  const addView = (data: Float32Array | Uint32Array, target: number) => {
    const bytes = new Uint8Array(data.buffer, data.byteOffset, data.byteLength);
    bufferViews.push({ buffer: 0, byteOffset: length, byteLength: bytes.length, target });
    chunks.push(bytes);
    length += bytes.length;
    return bufferViews.length - 1;
  };
  const material = (hex: string) => {
    let index = materialOf.get(hex);
    if (index === undefined) {
      const rgb = [0, 2, 4].map((i) => srgbToLinear(parseInt(hex.slice(i, i + 2), 16) / 255));
      const m: Record<string, unknown> = {
        name: `color_${hex}`,
        pbrMetallicRoughness: { baseColorFactor: [...rgb, 1], metallicFactor: 0, roughnessFactor: 1 },
      };
      if (emissive?.(hex)) m.emissiveFactor = rgb;
      materials.push(m);
      index = materials.length - 1;
      materialOf.set(hex, index);
    }
    return index;
  };

  const gltfMeshes: object[] = [];
  meshes.forEach((mesh, mi) => {
    const quadCount = mesh.positions.length / 12;
    const byColor = new Map<string, number[]>();
    for (let q = 0; q < quadCount; q++) {
      const c = mesh.colors.subarray(q * 16, q * 16 + 3);
      const key = Array.from(c, (v) => Math.round(v * 255).toString(16).padStart(2, '0')).join('');
      let list = byColor.get(key);
      if (!list) byColor.set(key, (list = []));
      list.push(q);
    }

    const primitives: object[] = [];
    for (const [hex, quads] of byColor) {
      const positions = new Float32Array(quads.length * 12);
      const normals = new Float32Array(quads.length * 12);
      const indices = new Uint32Array(quads.length * 6);
      const min = [Infinity, Infinity, Infinity];
      const max = [-Infinity, -Infinity, -Infinity];
      quads.forEach((q, k) => {
        positions.set(mesh.positions.subarray(q * 12, q * 12 + 12), k * 12);
        normals.set(mesh.normals.subarray(q * 12, q * 12 + 12), k * 12);
        indices.set([k * 4, k * 4 + 1, k * 4 + 2, k * 4, k * 4 + 2, k * 4 + 3], k * 6);
        for (let i = 0; i < 12; i++) {
          const a = i % 3;
          min[a] = Math.min(min[a], positions[k * 12 + i]);
          max[a] = Math.max(max[a], positions[k * 12 + i]);
        }
      });
      const pos = accessors.push({ bufferView: addView(positions, 34962), componentType: 5126, count: quads.length * 4, type: 'VEC3', min, max }) - 1;
      const nor = accessors.push({ bufferView: addView(normals, 34962), componentType: 5126, count: quads.length * 4, type: 'VEC3' }) - 1;
      const idx = accessors.push({ bufferView: addView(indices, 34963), componentType: 5125, count: indices.length, type: 'SCALAR' }) - 1;
      primitives.push({ attributes: { POSITION: pos, NORMAL: nor }, indices: idx, material: material(hex), mode: 4 });
    }

    if (primitives.length === 0) {
      // An empty frame still needs a node so frame indices stay aligned: a degenerate triangle.
      const p = new Float32Array(9);
      const n = new Float32Array([0, 1, 0, 0, 1, 0, 0, 1, 0]);
      const pos = accessors.push({ bufferView: addView(p, 34962), componentType: 5126, count: 3, type: 'VEC3', min: [0, 0, 0], max: [0, 0, 0] }) - 1;
      const nor = accessors.push({ bufferView: addView(n, 34962), componentType: 5126, count: 3, type: 'VEC3' }) - 1;
      primitives.push({ attributes: { POSITION: pos, NORMAL: nor }, material: material('000000'), mode: 4 });
    }

    gltfMeshes.push({ name: names[mi], primitives });
  });

  const bin = new Uint8Array(length);
  let offset = 0;
  for (const c of chunks) {
    bin.set(c, offset);
    offset += c.length;
  }

  const gltf = {
    asset: { version: '2.0', generator: 'EasyPixel voxel export (ARPG Game pipeline)' },
    scene: 0,
    scenes: [{ nodes: names.map((_, i) => i) }],
    nodes: names.map((name, i) => ({ name, mesh: i })),
    meshes: gltfMeshes,
    materials,
    buffers: [{ byteLength: bin.length }],
    bufferViews,
    accessors,
  };

  let json = new TextEncoder().encode(JSON.stringify(gltf));
  const jsonPad = (4 - (json.length % 4)) % 4;
  if (jsonPad) {
    const padded = new Uint8Array(json.length + jsonPad).fill(0x20);
    padded.set(json);
    json = padded;
  }
  const total = 12 + 8 + json.length + 8 + bin.length;
  const out = new Uint8Array(total);
  const view = new DataView(out.buffer);
  view.setUint32(0, 0x46546c67, true);
  view.setUint32(4, 2, true);
  view.setUint32(8, total, true);
  view.setUint32(12, json.length, true);
  view.setUint32(16, 0x4e4f534a, true);
  out.set(json, 20);
  const binStart = 20 + json.length;
  view.setUint32(binStart, bin.length, true);
  view.setUint32(binStart + 4, 0x004e4942, true);
  out.set(bin, binStart + 8);
  return out;
}

function write(path: string, data: Uint8Array): void {
  const full = join(PROJECT_ROOT, path);
  mkdirSync(dirname(full), { recursive: true });
  writeFileSync(full, data);
}

// ---------------------------------------------------------------------------
// Sprites → inflated voxel figures
// ---------------------------------------------------------------------------

const SPRITE_DIRS = ['assets/sprites/player', 'assets/sprites/enemies', 'assets/sprites/bosses', 'assets/sprites/props', 'assets/sprites/projectiles'];

function exportSprite(spec: SpriteSpec): void {
  const doc = buildDocument(spec);
  // Thickness grows with the sprite: a puffy body, not a flat card.
  const depth = Math.max(3, Math.min(16, Math.round(doc.width * 0.5)));
  const meshes: VoxelMesh[] = [];
  const names: string[] = [];
  doc.frames.forEach((_, fi) => {
    // No crop: every frame keeps the canvas origin, so feet stay planted while animating.
    const model = documentToVoxels(doc, { mode: 'inflate', depth, frame: fi, crop: false });
    meshes.push(voxelMesh(model, { voxelSize: VOXEL_SIZE, centerBottom: true }));
    names.push(`f${fi}`);
  });
  const out = spec.dir.replace('assets/sprites/', 'assets/voxels/') + `/${spec.name}.glb`;
  write(out, writeGlbFrames(meshes, names));
  console.log(`voxels ${out} (${doc.frames.length} frames, depth ${depth})`);
}

// ---------------------------------------------------------------------------
// Tilesets → terrain pieces
// ---------------------------------------------------------------------------

type Rgba = [number, number, number, number];

function pack([r, g, b]: Rgba): number {
  return ((r << 24) | (g << 16) | (b << 8) | 255) >>> 0;
}

function shade(c: Rgba, f: number): Rgba {
  return [Math.min(255, Math.round(c[0] * f)), Math.min(255, Math.round(c[1] * f)), Math.min(255, Math.round(c[2] * f)), 255];
}

function model(sx: number, sy: number, sz: number): VoxelModel {
  return { sizeX: sx, sizeY: sy, sizeZ: sz, voxels: new Uint32Array(sx * sy * sz) };
}

function setV(m: VoxelModel, x: number, y: number, z: number, c: number): void {
  m.voxels[x + m.sizeX * (y + m.sizeY * z)] = c;
}

export const WALL_HEIGHT_VOXELS = 22;

function exportTileset(spec: SpriteSpec): void {
  const doc = buildDocument(spec);
  const sheet = compositeFrame(doc, 0);
  const T = 16;
  const px = (tx: number, ty: number, x: number, y: number): Rgba => {
    const i = ((ty * T + y) * doc.width + tx * T + x) * 4;
    return [sheet[i], sheet[i + 1], sheet[i + 2], sheet[i + 3]];
  };

  const meshes: VoxelMesh[] = [];
  const names: string[] = [];
  const liquid = new Set<string>();

  // Floor slabs (two voxels) and the rune floor: image x → X, image y → Z (top-down).
  const slab = (tx: number, ty: number, height: number, name: string, isLiquid = false) => {
    const m = model(T, height, T);
    for (let z = 0; z < T; z++) {
      for (let x = 0; x < T; x++) {
        const c = px(tx, ty, x, z);
        for (let y = 0; y < height; y++) setV(m, x, y, z, pack(y === height - 1 ? c : shade(c, 0.7)));
        if (isLiquid) liquid.add(pack(c).toString(16).padStart(8, '0').slice(0, 6));
      }
    }
    meshes.push(voxelMesh(m, { voxelSize: VOXEL_SIZE, centerBottom: true }));
    names.push(name);
  };
  [0, 1, 2, 3].forEach((i) => slab(i, 0, 2, `floor${i}`));
  slab(0, 1, 1, 'hazard0', true);
  slab(1, 1, 1, 'hazard1', true);
  slab(3, 1, 2, 'rune');

  // Wall: a tall block. Sides repeat the brick rows of the wall tile with height,
  // the top is a lighter cap so wall tops read from above.
  const wall = model(T, WALL_HEIGHT_VOXELS, T);
  for (let z = 0; z < T; z++) {
    for (let y = 0; y < WALL_HEIGHT_VOXELS; y++) {
      for (let x = 0; x < T; x++) {
        const top = y === WALL_HEIGHT_VOXELS - 1;
        const brick = px(2, 1, (x + z) % T, (WALL_HEIGHT_VOXELS - 1 - y) % T);
        setV(wall, x, y, z, pack(top ? shade(px(2, 1, x, z), 1.25) : brick));
      }
    }
  }
  meshes.push(voxelMesh(wall, { voxelSize: VOXEL_SIZE, centerBottom: true }));
  names.push('wall');

  const out = `assets/voxels/tiles/${spec.name}.glb`;
  write(out, writeGlbFrames(meshes, names, (hex) => liquid.has(hex)));
  console.log(`voxels ${out} (${names.join(', ')})`);
}

const filter = process.argv.slice(2).find((a) => !a.startsWith('--'));
const want = (name: string) => !filter || filter.split(',').some((f) => name.includes(f));

for (const spec of all) {
  if (!want(spec.name)) continue;
  if (spec.tileset) {
    exportTileset(spec);
  } else if (SPRITE_DIRS.includes(spec.dir)) {
    exportSprite(spec);
  }
}

void tiles;
