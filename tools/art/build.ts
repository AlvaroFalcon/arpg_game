/**
 * Builds every sprite: `npm run setup && npm run build` from tools/art.
 * Pass `--preview <out.png>` to also write a 4x contact sheet of every frame
 * (handy for reviewing art without opening Godot).
 */
import { writeFileSync } from 'node:fs';
import { buildDocument, emit, encodePng, type SpriteSpec } from './lib';
import { compositeFrame } from './vendor/easypixel/packages/core/src/index';
import { all } from './sprites/index';

function contactSheet(specs: SpriteSpec[], scale = 4, pad = 4): Buffer {
  const docs = specs.map(buildDocument);
  const rowHeights = docs.map((d) => d.height * scale + pad);
  const width = Math.max(...docs.map((d) => d.frames.length * (d.width * scale + pad))) + pad;
  const height = rowHeights.reduce((a, b) => a + b, 0) + pad;
  const data = new Uint8ClampedArray(width * height * 4);
  // Checker background so transparency reads.
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const v = ((x >> 3) + (y >> 3)) % 2 ? 52 : 40;
      data.set([v, v, v + 6, 255], (y * width + x) * 4);
    }
  }
  let oy = pad;
  docs.forEach((doc, di) => {
    doc.frames.forEach((_, fi) => {
      const frame = compositeFrame(doc, fi);
      const ox = pad + fi * (doc.width * scale + pad);
      for (let y = 0; y < doc.height * scale; y++) {
        for (let x = 0; x < doc.width * scale; x++) {
          const si = ((y / scale | 0) * doc.width + (x / scale | 0)) * 4;
          if (frame[si + 3] === 0) continue;
          data.set(frame.subarray(si, si + 4), ((oy + y) * width + ox + x) * 4);
        }
      }
    });
    oy += rowHeights[di];
  });
  return encodePng({ width, height, data });
}

const args = process.argv.slice(2);
const previewAt = args.indexOf('--preview');
const only = args.find((a) => !a.startsWith('--') && (previewAt < 0 || a !== args[previewAt + 1]));
const specs = only ? all.filter((s) => only.split(',').some((o) => s.name.includes(o))) : all;

if (previewAt >= 0) {
  writeFileSync(args[previewAt + 1], contactSheet(specs));
  console.log(`preview -> ${args[previewAt + 1]}`);
} else {
  for (const spec of specs) {
    emit(spec);
    console.log(`exported ${spec.dir}/${spec.name}`);
  }
}
