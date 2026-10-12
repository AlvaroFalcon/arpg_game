/**
 * A tiny pose rig for illustration-quality pixel figures (launcher key art) —
 * owner brief: "redraw it with much more quality, like real characters,
 * really moving, arms in motion". Instead of up-scaling 32×48 game sprites,
 * a figure is a set of joints (head, shoulders, elbows, hands, hips, knees,
 * feet) and is painted limb by limb: tapered, shaded capsules for arms and
 * legs, a torso polygon, an anime head in three-quarter view, flowing hair,
 * capes and weapons attached to the hands. Change the joints and the same
 * character strikes a different pose.
 *
 * Painting goes into a {@link Paint} char grid with a per-figure legend, so the
 * launcher composites it with the same lighting as everything else.
 */
import { Paint } from './paint';

export type V = [number, number];

export const add = (a: V, b: V): V => [a[0] + b[0], a[1] + b[1]];
export const sub = (a: V, b: V): V => [a[0] - b[0], a[1] - b[1]];
export const mul = (a: V, k: number): V => [a[0] * k, a[1] * k];
export const lerp = (a: V, b: V, t: number): V => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t];
export const len = (a: V): number => Math.hypot(a[0], a[1]);
export const norm = (a: V): V => {
  const l = len(a) || 1;
  return [a[0] / l, a[1] / l];
};
export const perp = (a: V): V => [-a[1], a[0]];

/** Colour slots every figure's legend provides (plus 'k' for the outline). */
export interface Tones {
  base: string;
  shade: string;
  light: string;
}

export class Rig {
  constructor(readonly p: Paint) {}

  /**
   * A tapered limb segment from a to b with rounded ends, shaded: the side
   * facing away from the upper-left light gets the shade tone, a highlight runs
   * along the lit edge.
   */
  limb(a: V, b: V, wa: number, wb: number, t: Tones, only?: string): void {
    const d = norm(sub(b, a));
    const n = perp(d);
    const quad = (wA: number, wB: number, o: V = [0, 0]): V[] => [
      add(add(a, mul(n, wA / 2)), o),
      add(add(b, mul(n, wB / 2)), o),
      add(add(b, mul(n, -wB / 2)), o),
      add(add(a, mul(n, -wA / 2)), o),
    ];
    this.p.poly(quad(wa, wb), t.shade, only);
    this.p.ellipse(a[0], a[1], wa / 2, wa / 2, t.shade, only);
    this.p.ellipse(b[0], b[1], wb / 2, wb / 2, t.shade, only);
    // The lit part: same shape nudged towards the light and slimmer.
    const toLight: V = [-0.7, -0.7];
    this.p.poly(quad(wa * 0.72, wb * 0.72, toLight), t.base, only);
    this.p.ellipse(a[0] - 0.7, a[1] - 0.7, (wa * 0.72) / 2, (wa * 0.72) / 2, t.base, only);
    this.p.ellipse(b[0] - 0.7, b[1] - 0.7, (wb * 0.72) / 2, (wb * 0.72) / 2, t.base, only);
    // Highlight along whichever edge faces up-left.
    const side = n[0] + n[1] < 0 ? 1 : -1;
    const h1 = add(a, mul(n, (side * wa) / 4));
    const h2 = add(b, mul(n, (side * wb) / 4));
    this.p.line(lerp(h1, h2, 0.15)[0], lerp(h1, h2, 0.15)[1], lerp(h1, h2, 0.8)[0], lerp(h1, h2, 0.8)[1], t.light);
  }

  /** A filled shape shaded by a vertical split: left of `splitX` lit, right shaded. */
  shape(points: V[], t: Tones, splitX?: number): void {
    this.p.poly(points, t.base);
    if (splitX !== undefined) {
      const xs = points.map((q) => q[0]);
      const ys = points.map((q) => q[1]);
      const right: V[] = [
        [splitX, Math.min(...ys) - 1],
        [Math.max(...xs) + 1, Math.min(...ys) - 1],
        [Math.max(...xs) + 1, Math.max(...ys) + 1],
        [splitX, Math.max(...ys) + 1],
      ];
      this.p.poly(right, t.shade, t.base);
    }
  }

  /** A flowing strip (cape, scarf tail, hair lock): a polyline with a width profile. */
  ribbon(path: V[], widths: number[], t: Tones): void {
    const left: V[] = [];
    const right: V[] = [];
    path.forEach((pt, i) => {
      const dir = norm(sub(path[Math.min(i + 1, path.length - 1)], path[Math.max(i - 1, 0)]));
      const n = perp(dir);
      left.push(add(pt, mul(n, widths[i] / 2)));
      right.push(add(pt, mul(n, -widths[i] / 2)));
    });
    this.p.poly([...left, ...right.reverse()], t.shade);
    const inner: V[] = [];
    const innerR: V[] = [];
    path.forEach((pt, i) => {
      const dir = norm(sub(path[Math.min(i + 1, path.length - 1)], path[Math.max(i - 1, 0)]));
      const n = perp(dir);
      inner.push(add(add(pt, mul(n, widths[i] / 2)), [-0.5, -0.8]));
      innerR.push(add(add(pt, mul(n, -widths[i] * 0.1)), [-0.5, -0.8]));
    });
    this.p.poly([...inner, ...innerR.reverse()], t.base);
  }
}

/** Joints of a standing human figure, facing right in three-quarter view. */
export interface Pose {
  head: V;
  neck: V;
  shoulderN: V; // near (viewer-side) shoulder
  shoulderF: V; // far shoulder
  elbowN: V;
  elbowF: V;
  handN: V;
  handF: V;
  hipN: V;
  hipF: V;
  kneeN: V;
  kneeF: V;
  footN: V;
  footF: V;
}

export interface FigureStyle {
  skin: Tones;
  top: Tones; // tunic / armour body
  sleeve: Tones;
  legs: Tones;
  boots: Tones;
  hair: Tones;
  eye: string;
  eyeDark: string;
  /** Size of the head (radius). */
  headR: number;
  hairStyle: 'spiky' | 'long' | 'hood' | 'bob';
  /** Pauldrons and a breastplate. */
  plate?: Tones;
  /** Long robe skirt down to the ankles. */
  robe?: boolean;
  hat?: Tones;
  belt: string;
  /** Limb and torso thickness multiplier (1 = adult, 1.7 = brute). */
  bulk?: number;
  /** Sleeves that flare at the wrist (robes). */
  bellSleeves?: boolean;
}

/** Paints a full figure: far limbs, torso, near limbs, head. Returns the near hand position. */
export function paintFigure(rig: Rig, pose: Pose, s: FigureStyle, extrasBehind?: () => void, extrasFront?: () => void): void {
  const p = rig.p;
  const k = s.bulk ?? 1;
  extrasBehind?.();

  // Far side first: far leg and far arm sit behind the body (a touch darker).
  const farLegs: Tones = { base: s.legs.shade, shade: s.legs.shade, light: s.legs.base };
  const farBoots: Tones = { base: s.boots.shade, shade: s.boots.shade, light: s.boots.base };
  const farSleeve: Tones = { base: s.sleeve.shade, shade: s.sleeve.shade, light: s.sleeve.base };
  if (!s.robe) {
    rig.limb(pose.hipF, pose.kneeF, 9 * k, 7 * k, farLegs);
    rig.limb(pose.kneeF, pose.footF, 7 * k, 5.5 * k, farBoots);
  }
  p.ellipse(pose.footF[0] + 2, pose.footF[1] + 1, 4.4 * k, 2.4, s.boots.shade);
  rig.limb(pose.shoulderF, pose.elbowF, 7 * k, 6 * k, farSleeve);
  rig.limb(pose.elbowF, pose.handF, s.bellSleeves ? 6 : 6 * k, s.bellSleeves ? 9 : 4.8 * k, farSleeve);
  p.ellipse(pose.handF[0], pose.handF[1], 2.6 * k, 2.6 * k, s.skin.shade);

  // Near leg (unless a robe hides the legs).
  if (!s.robe) {
    rig.limb(pose.hipN, pose.kneeN, 10 * k, 8 * k, s.legs);
    rig.limb(pose.kneeN, pose.footN, 8 * k, 6 * k, s.boots);
    const cuff = lerp(pose.kneeN, pose.footN, 0.3);
    p.ellipse(cuff[0], cuff[1], 4.4 * k, 2, s.boots.light);
  }
  p.ellipse(pose.footN[0] + 2.5, pose.footN[1] + 1, 5 * k, 2.6, s.boots.base);
  p.line(pose.footN[0] - 2, pose.footN[1] + 3, pose.footN[0] + 7, pose.footN[1] + 3, s.boots.shade);

  if (s.robe) {
    // Robe skirt flaring to the ankles; the hem is uneven and trails backwards.
    const hem = Math.max(pose.footN[1], pose.footF[1]) - 1;
    const front = Math.max(pose.footN[0], pose.footF[0]) + 7;
    const back = Math.min(pose.footN[0], pose.footF[0]) - 10;
    const skirt: V[] = [
      add(pose.hipF, [-3, -2]),
      add(pose.hipN, [3, -2]),
      [front - 2, hem - 10],
      [front, hem - 1],
      [front - 6, hem + 1],
      [(front + back) / 2 + 3, hem - 1],
      [(front + back) / 2 - 4, hem + 1],
      [back + 3, hem - 1],
      [back, hem - 4],
      [back + 4, hem - 16],
    ];
    rig.shape(skirt, s.top, (pose.hipF[0] + pose.hipN[0]) / 2 + 3);
    const mid = lerp(pose.hipF, pose.hipN, 0.5);
    p.line(mid[0] - 2, mid[1] + 4, back + 8, hem - 3, s.top.shade);
    p.line(mid[0] + 1, mid[1] + 6, (front + back) / 2, hem - 2, s.top.shade);
    p.line(mid[0] + 4, mid[1] + 2, front - 4, hem - 2, s.top.light);
    p.line(back + 2, hem - 2, front - 2, hem - 1, s.belt);
  }

  // Torso: broad shoulders, a waist, leaning with the pose.
  const torso: V[] = [
    add(pose.shoulderF, [-2.5 * k, -1.5]),
    add(pose.shoulderN, [3 * k, -1.5]),
    add(lerp(pose.shoulderN, pose.hipN, 0.45), [3.2 * k, 0]),
    add(pose.hipN, [2.2 * k, 1]),
    add(pose.hipF, [-2.2 * k, 1]),
    add(lerp(pose.shoulderF, pose.hipF, 0.45), [-2.6 * k, 0]),
  ];
  rig.shape(torso, s.top, lerp(pose.shoulderF, pose.shoulderN, 0.7)[0]);
  const beltA = lerp(pose.shoulderF, pose.hipF, 0.84);
  const beltB = lerp(pose.shoulderN, pose.hipN, 0.84);
  p.line(beltA[0] - 2, beltA[1], beltB[0] + 2, beltB[1], s.belt, 2);
  if (s.plate) {
    const a = lerp(pose.shoulderF, pose.hipF, 0.1);
    const b = lerp(pose.shoulderN, pose.hipN, 0.1);
    const c = lerp(pose.shoulderN, pose.hipN, 0.66);
    const d = lerp(pose.shoulderF, pose.hipF, 0.66);
    rig.shape([add(a, [-1, 0]), add(b, [2, 0]), add(c, [1, 0]), add(d, [0, 0])], s.plate, lerp(a, b, 0.7)[0]);
    p.line(a[0] + 2, a[1] + 2, b[0] - 2, b[1] + 1, s.plate.light);
    p.line(lerp(a, d, 0.5)[0] + 3, lerp(a, d, 0.5)[1], lerp(b, c, 0.5)[0] - 1, lerp(b, c, 0.5)[1], s.plate.shade);
  }

  // Neck and head.
  rig.limb(pose.neck, add(pose.head, [0, s.headR * 0.6]), 5, 5, s.skin);
  if (s.headR > 1) {
    paintHead(rig, pose.head, s);
  }

  // Near arm on top, then its pauldron.
  rig.limb(pose.shoulderN, pose.elbowN, 8 * k, 6.6 * k, s.sleeve);
  if (s.bellSleeves) {
    const cuff = lerp(pose.elbowN, pose.handN, 0.8);
    rig.limb(pose.elbowN, cuff, 7, 12, s.sleeve);
    const d = norm(sub(pose.handN, pose.elbowN));
    const n = perp(d);
    p.line(cuff[0] + n[0] * 5.5, cuff[1] + n[1] * 5.5, cuff[0] - n[0] * 5.5, cuff[1] - n[1] * 5.5, s.belt);
  } else {
    rig.limb(pose.elbowN, pose.handN, 6.6 * k, 5.2 * k, s.sleeve);
  }
  p.ellipse(pose.handN[0], pose.handN[1], 3 * k, 3 * k, s.skin.base);
  p.px(pose.handN[0] - 1, pose.handN[1] - 1, s.skin.light);
  if (s.plate) {
    p.ellipse(pose.shoulderN[0], pose.shoulderN[1], 6.4, 4.6, s.plate.shade);
    p.ellipse(pose.shoulderN[0] - 0.6, pose.shoulderN[1] - 0.8, 5.4, 3.6, s.plate.base);
    p.ellipse(pose.shoulderN[0] - 1.6, pose.shoulderN[1] - 1.6, 2.8, 1.4, s.plate.light);
  }

  extrasFront?.();
}

/** An anime head in three-quarter view facing right. */
export function paintHead(rig: Rig, c: V, s: FigureStyle): void {
  const p = rig.p;
  const r = s.headR;
  const [cx, cy] = c;

  // Hair mass behind the head.
  if (s.hairStyle === 'long') {
    rig.ribbon([[cx - 2, cy - r * 0.4], [cx - r * 0.9, cy + r * 0.9], [cx - r * 1.5, cy + r * 2.4], [cx - r * 2.1, cy + r * 3.2]], [r * 1.8, r * 1.9, r * 1.4, r * 0.6], s.hair);
  }
  p.ellipse(cx - 1, cy - 1, r * 1.12, r * 1.1, s.hair.shade);

  // Face: skin, a soft jaw pointing down-right, cheek shadow on the far side.
  p.ellipse(cx + 0.6, cy + 0.4, r * 0.86, r * 0.92, s.skin.base);
  p.poly([[cx - r * 0.5, cy + r * 0.3], [cx + r * 0.95, cy + r * 0.1], [cx + r * 0.55, cy + r * 0.85], [cx + r * 0.05, cy + r * 1.08], [cx - r * 0.45, cy + r * 0.7]], s.skin.base);
  p.ellipse(cx - r * 0.55, cy + r * 0.25, r * 0.28, r * 0.55, s.skin.shade, s.skin.base);

  // Hair on top: crown and bangs over the forehead (stopping above the eyes).
  p.ellipse(cx - 1, cy - r * 0.45, r * 1.05, r * 0.7, s.hair.base);
  p.poly([
    [cx - r, cy - r * 0.4],
    [cx + r * 0.9, cy - r * 0.62],
    [cx + r * 1.0, cy - r * 0.1],
    [cx + r * 0.66, cy - r * 0.32],
    [cx + r * 0.46, cy - r * 0.05],
    [cx + r * 0.22, cy - r * 0.35],
    [cx - r * 0.02, cy - r * 0.08],
    [cx - r * 0.3, cy - r * 0.32],
    [cx - r * 0.75, cy + r * 0.45],
  ], s.hair.base);
  p.line(cx - r * 0.2, cy - r * 0.9, cx + r * 0.2, cy - r * 0.3, s.hair.shade);
  p.line(cx + r * 0.45, cy - r * 0.85, cx + r * 0.65, cy - r * 0.35, s.hair.shade);
  p.ellipse(cx - r * 0.25, cy - r * 0.62, r * 0.4, r * 0.14, s.hair.light, s.hair.base);

  switch (s.hairStyle) {
    case 'spiky':
      // Spikes swept back by the motion.
      for (const [ax, ay, bx, by] of [
        [-0.6, -0.9, -1.9, -1.4],
        [-0.1, -1.0, -1.1, -1.9],
        [0.4, -0.9, -0.2, -2.0],
        [-1.0, -0.2, -2.3, -0.4],
        [-0.9, 0.3, -2.0, 0.6],
      ]) {
        p.poly([[cx + ax * r, cy + ay * r], [cx + (ax + 0.55) * r, cy + (ay + 0.2) * r], [cx + bx * r, cy + by * r]], s.hair.base);
      }
      break;
    case 'hood':
      p.ellipse(cx - 1.5, cy - r * 0.3, r * 1.25, r * 1.18, s.hat?.base ?? s.hair.base, `.${s.hair.base}${s.hair.shade}${s.hair.light}`);
      p.poly([[cx - r * 1.2, cy - r * 0.2], [cx - r * 2.4, cy + r * 0.8], [cx - r * 0.6, cy + r * 1.2]], s.hat?.shade ?? s.hair.shade);
      p.ellipse(cx + r * 0.45, cy + r * 0.3, r * 0.75, r * 0.85, s.skin.base, s.hat?.base ?? s.hair.base);
      p.line(cx - r * 0.2, cy - r * 0.45, cx + r * 0.95, cy - r * 0.3, s.hair.base, 2);
      break;
    case 'bob':
      p.poly([[cx - r * 1.05, cy - r * 0.3], [cx - r * 0.55, cy - r * 0.3], [cx - r * 0.4, cy + r * 1.1], [cx - r * 1.1, cy + r * 0.95]], s.hair.base);
      break;
    case 'long':
      p.poly([[cx - r * 1.05, cy - r * 0.3], [cx - r * 0.55, cy - r * 0.2], [cx - r * 0.5, cy + r * 1.4], [cx - r * 1.2, cy + r * 1.2]], s.hair.base);
      break;
  }

  // Eyes last, so hair never hides them: big anime eyes, the near one larger.
  const eye = (exf: number, w: number) => {
    const ey = Math.round(cy + r * 0.2);
    const ex = Math.round(exf);
    const x0 = ex - Math.floor(w / 2);
    p.rect(x0 - 1, ey - 3, w + 1, 1, 'k'); // upper lash, flicked out
    p.rect(x0, ey - 2, w, 4, s.eye);
    p.rect(x0, ey - 1, w, 2, s.eyeDark);
    p.px(x0, ey - 2, 'w');
    if (w > 3) p.px(x0 + 1, ey - 2, 'w');
    p.px(x0 + w - 1, ey + 1, 'w');
    p.rect(x0, ey - 5, w, 1, s.hair.shade); // brow
  };
  eye(cx + r * 0.42, 4);
  eye(cx - r * 0.25, 3);
  p.px(cx + r * 0.7, cy + r * 0.62, 'n'); // blush
  p.px(cx + r * 0.62, cy + r * 0.42, s.skin.shade); // nose
  p.rect(cx + r * 0.2, cy + r * 0.84, 3, 1, 'm'); // mouth
}
