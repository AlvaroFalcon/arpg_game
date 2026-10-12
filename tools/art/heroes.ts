/**
 * Illustration-quality heroes (and the brute they fight) for the launcher key
 * art, painted with the pose rig in `rig.ts`. Each returns char rows, a legend
 * and the feet anchor, ready for `launcher.ts` to light and composite.
 *
 * Shared legend roles (each figure supplies its own colours):
 *   k outline · w white · m mouth · n blush
 *   s/S/1 skin · o/p/O top · v/V/2 sleeves · l/L/3 legs · b/B/4 boots · h/j/H hair
 *   e/E eyes · x/5/X plate · q/Q/6 hat or hood · t belt and trim
 *   g/G/7 steel · y/Y wood · z/Z glow · a/A/8 cape or scarf · u/U pages
 */
import { Paint } from './paint';
import { add, lerp, mul, norm, perp, Rig, sub, paintFigure, type FigureStyle, type Pose, type Tones, type V } from './rig';

export interface Painted {
  rows: string[];
  legend: Record<string, string>;
  /** Feet centre inside the rows (where the figure stands). */
  anchor: V;
}

const T = (base: string, shade: string, light: string): Tones => ({ base, shade, light });
const SKIN = T('s', 'S', '1');
const TOP = T('o', 'p', 'O');
const SLEEVE = T('v', 'V', '2');
const LEGS = T('l', 'L', '3');
const BOOTS = T('b', 'B', '4');
const HAIR = T('h', 'j', 'H');
const PLATE = T('x', '5', 'X');
const HAT = T('q', 'Q', '6');
const STEEL = T('g', 'G', '7');
const CAPE = T('a', 'A', '8');

const common = { k: '#140c1a', w: '#ffffff', m: '#9a3a34', n: '#f29a9a', s: '#ffdcc2', S: '#d8a68a', 1: '#fff0e2', g: '#c8d2de', G: '#7a8698', 7: '#ffffff', y: '#8a5a32', Y: '#5a3820', t: '#d8a838' };

function style(extra: Partial<FigureStyle>): FigureStyle {
  return {
    skin: SKIN,
    top: TOP,
    sleeve: SLEEVE,
    legs: LEGS,
    boots: BOOTS,
    hair: HAIR,
    eye: 'e',
    eyeDark: 'E',
    headR: 9,
    hairStyle: 'spiky',
    belt: 't',
    ...extra,
  };
}

function finish(p: Paint, legend: Record<string, string>, anchor: V): Painted {
  p.outline('k');
  return { rows: p.rows(), legend: { ...common, ...legend }, anchor };
}

// --- weapons -------------------------------------------------------------------------------

function sword(rig: Rig, hand: V, dir: V, length: number): void {
  const p = rig.p;
  const d = norm(dir);
  const n = perp(d);
  const tip = add(hand, mul(d, length));
  const base = add(hand, mul(d, 4));
  p.poly([add(base, mul(n, 1.8)), add(lerp(base, tip, 0.85), mul(n, 1.5)), tip, add(lerp(base, tip, 0.85), mul(n, -1.5)), add(base, mul(n, -1.8))], STEEL.base);
  p.line(base[0] + n[0] * 0.6, base[1] + n[1] * 0.6, tip[0] - d[0] * 3, tip[1] - d[1] * 3, STEEL.light);
  p.line(base[0] - n[0] * 1.2, base[1] - n[1] * 1.2, tip[0] - d[0] * 5 - n[0], tip[1] - d[1] * 5 - n[1], STEEL.shade);
  // Crossguard and grip.
  const g1 = add(add(hand, mul(d, 3)), mul(n, 4));
  const g2 = add(add(hand, mul(d, 3)), mul(n, -4));
  p.line(g1[0], g1[1], g2[0], g2[1], 't', 2);
  const pommel = add(hand, mul(d, -4));
  p.line(hand[0], hand[1], pommel[0], pommel[1], 'Y', 2);
  p.ellipse(pommel[0], pommel[1], 1.4, 1.4, 't');
}

function dagger(rig: Rig, hand: V, dir: V): void {
  sword(rig, hand, dir, 13);
}

function shield(rig: Rig, at: V, tilt: number, colors: Tones, emblem: string): void {
  const p = rig.p;
  const pts: V[] = [[-8, -10], [8, -10], [8, 2], [0, 12], [-8, 2]];
  const rot = (q: V): V => [at[0] + q[0] * Math.cos(tilt) - q[1] * Math.sin(tilt) * 0.3 + q[1] * 0, at[1] + q[1] + q[0] * Math.sin(tilt) * 0.3];
  const outer = pts.map(rot);
  const inner = pts.map((q) => rot([q[0] * 0.78, q[1] * 0.8]));
  p.poly(outer, PLATE.shade);
  p.poly(inner, colors.base);
  p.poly(inner.map((q) => [q[0] - 1, q[1] - 1] as V).slice(0, 3).concat([rot([0, -2])]), colors.light, colors.base);
  // Emblem: a cross.
  const c = rot([0, -1]);
  p.rect(c[0] - 1, c[1] - 6, 2, 12, emblem);
  p.rect(c[0] - 4, c[1] - 2, 8, 2, emblem);
}

// --- heroes --------------------------------------------------------------------------------

/** The warrior lunging into an overhead slash, shield up, scarf streaming back. */
export function warriorLunge(): Painted {
  const p = new Paint(130, 130);
  const rig = new Rig(p);
  const pose: Pose = {
    head: [70, 30],
    neck: [66, 40],
    shoulderN: [70, 46],
    shoulderF: [59, 45],
    elbowN: [80, 36],
    handN: [88, 26],
    elbowF: [64, 58],
    handF: [76, 60],
    hipN: [61, 78],
    hipF: [55, 77],
    kneeN: [79, 94],
    footN: [86, 121],
    kneeF: [42, 98],
    footF: [24, 117],
  };
  const s = style({ hairStyle: 'spiky', plate: PLATE, headR: 9.5 });
  paintFigure(
    rig,
    pose,
    s,
    () => {
      // Scarf tails whipping back.
      rig.ribbon([[62, 42], [46, 38], [30, 42], [14, 36]], [6, 5, 4, 2], CAPE);
      rig.ribbon([[61, 44], [44, 46], [30, 52]], [5, 4, 2], CAPE);
    },
    () => {
      shield(rig, [78, 62], 0.2, T('o', 'p', 'O'), 't');
      sword(rig, pose.handN, [1, -0.55], 38);
      p.rect(60, 42, 12, 3, 'a'); // scarf wrap at the neck
    },
  );
  return finish(p, {
    o: '#b8382a', p: '#7a2018', O: '#e05a44',
    v: '#b8382a', V: '#7a2018', 2: '#e05a44',
    l: '#3a3550', L: '#262238', 3: '#565070',
    b: '#4a3428', B: '#2c1e16', 4: '#6a4a38',
    h: '#c8452a', j: '#7a2414', H: '#ff8a58',
    e: '#e07a2a', E: '#8a3810',
    x: '#a8b4c4', 5: '#6a7686', X: '#eef4fc',
    a: '#e03a2a', A: '#8a1a14', 8: '#ff6a50',
  }, [70, 121]);
}

/** The rogue mid-leap, one dagger driving forward, the other reversed behind, cloak flying. */
export function rogueLeap(): Painted {
  const p = new Paint(120, 110);
  const rig = new Rig(p);
  const pose: Pose = {
    head: [70, 26],
    neck: [66, 36],
    shoulderN: [68, 42],
    shoulderF: [58, 40],
    elbowN: [82, 46],
    handN: [95, 46],
    elbowF: [48, 34],
    handF: [37, 27],
    hipN: [57, 64],
    hipF: [51, 62],
    kneeN: [74, 72],
    footN: [66, 92],
    kneeF: [42, 78],
    footF: [28, 90],
  };
  const s = style({ hairStyle: 'hood', hat: HAT, headR: 8.5 });
  paintFigure(
    rig,
    pose,
    s,
    () => {
      rig.ribbon([[60, 40], [44, 34], [28, 40], [12, 32], [2, 38]], [12, 13, 11, 7, 3], HAT);
      dagger(rig, pose.handF, [-0.6, 1]);
    },
    () => {
      dagger(rig, pose.handN, [1, 0.12]);
      // Leather straps across the chest.
      p.line(60, 44, 66, 60, 't');
    },
  );
  return finish(p, {
    o: '#3a6a3a', p: '#244a26', O: '#5a8a52',
    v: '#3a6a3a', V: '#244a26', 2: '#5a8a52',
    l: '#4a3a2a', L: '#30261a', 3: '#6a5440',
    b: '#3a2a20', B: '#22180f', 4: '#5a4030',
    h: '#7a5a3a', j: '#4a3420', H: '#a8805a',
    e: '#3ab060', E: '#1a6a34',
    q: '#2e5a30', Q: '#1c3a1e', 6: '#4a7a46',
  }, [50, 96]);
}

/** The Arcanist casting: staff raised high, the other hand thrust forward, robe and hair in the spell's wind. */
export function arcanistCast(): Painted {
  const p = new Paint(110, 140);
  const rig = new Rig(p);
  const pose: Pose = {
    head: [52, 38],
    neck: [50, 48],
    shoulderN: [55, 55],
    shoulderF: [45, 54],
    elbowN: [68, 56],
    handN: [80, 52],
    elbowF: [38, 44],
    handF: [36, 32],
    hipN: [53, 84],
    hipF: [46, 84],
    kneeN: [60, 106],
    footN: [64, 130],
    kneeF: [40, 106],
    footF: [34, 130],
  };
  const s = style({ hairStyle: 'long', robe: true, headR: 9.5, sleeve: TOP, bellSleeves: true });
  paintFigure(
    rig,
    pose,
    s,
    () => {
      // The staff, held high behind the body.
      p.line(30, 4, 44, 126, 'y', 3);
      p.line(29, 4, 43, 126, 'Y');
      p.ellipse(30, 6, 4, 4, 'z');
      p.ellipse(29, 5, 1.8, 1.8, 'Z');
    },
    () => {
      // Wizard hat: wide brim, cone bending back in the wind.
      const [hx, hy] = pose.head;
      p.ellipse(hx + 1, hy - 7, 15, 3.2, 'q');
      p.poly([[hx - 8, hy - 8], [hx + 8, hy - 8], [hx + 3, hy - 18], [hx - 12, hy - 27], [hx - 4, hy - 17]], 'q');
      p.poly([[hx - 3, hy - 9], [hx + 8, hy - 8], [hx + 3, hy - 18]], 'Q');
      p.rect(hx - 7, hy - 10, 15, 2, 't');
      // Open palm glowing.
      p.ellipse(pose.handN[0] + 2, pose.handN[1], 2.4, 2.4, 'z');
      p.line(48, 60, 52, 82, 't');
    },
  );
  return finish(p, {
    o: '#5a48b8', p: '#3a2c80', O: '#8070e0',
    v: '#5a48b8', V: '#3a2c80', 2: '#8070e0',
    b: '#3a2a3a', B: '#22182a', 4: '#5a4258',
    h: '#f2a65a', j: '#c0703a', H: '#ffd8a8',
    e: '#7a5cff', E: '#3a2a9a',
    q: '#4a3a9a', Q: '#2e2468', 6: '#6a5ac0',
    z: '#7ae8ff', Z: '#ffffff',
  }, [50, 131]);
}

/** The Sage reading a floating tome whose pages glow, a hand raised to shape the spell. */
export function sageRead(): Painted {
  const p = new Paint(100, 130);
  const rig = new Rig(p);
  const pose: Pose = {
    head: [46, 36],
    neck: [44, 46],
    shoulderN: [49, 52],
    shoulderF: [39, 51],
    elbowN: [58, 64],
    handN: [66, 60],
    elbowF: [32, 42],
    handF: [30, 30],
    hipN: [47, 80],
    hipF: [40, 80],
    kneeN: [52, 102],
    footN: [55, 124],
    kneeF: [36, 102],
    footF: [30, 124],
  };
  const s = style({ hairStyle: 'bob', robe: true, headR: 9, sleeve: TOP, bellSleeves: true });
  paintFigure(rig, pose, s, undefined, () => {
    // The tome, open and floating above the near hand.
    p.poly([[62, 46], [72, 42], [74, 52], [64, 56]], 'q');
    p.poly([[72, 42], [82, 44], [82, 54], [74, 52]], 'Q');
    p.poly([[63, 45], [72, 41.5], [73, 50], [64, 53.5]], 'u');
    p.poly([[73, 42.5], [81, 44], [81, 52], [74, 50.5]], 'U');
    for (let i = 0; i < 3; i++) {
      p.line(65, 46 + i * 2.5, 70, 44 + i * 2.5, 'q');
      p.line(75, 45 + i * 2.5, 79, 46 + i * 2.5, 'q');
    }
    // Glasses and circlet.
    const [hx, hy] = pose.head;
    p.rect(hx + 1, hy, 5, 1, 't');
    p.rect(hx - 4, hy - 6, 11, 1, 't');
    p.px(hx + 2, hy - 7, 'z');
    p.line(40, 58, 46, 78, 't');
  });
  return finish(p, {
    o: '#2e7a60', p: '#1a5040', O: '#4aa486',
    b: '#2a3a30', B: '#18241c', 4: '#3a5040',
    h: '#7ad0a0', j: '#3a9068', H: '#c0f4d8',
    e: '#2a8a5a', E: '#14583a',
    q: '#8a5a2a', Q: '#5a3a18',
    u: '#fff4cc', U: '#e8d8a0',
    z: '#fff0a0',
  }, [43, 125]);
}

/** A demon brute rearing back from the warrior's blow, fist raised. */
export function bruteRear(): Painted {
  const p = new Paint(120, 120);
  const rig = new Rig(p);
  const skin = T('o', 'p', 'O');
  const pose: Pose = {
    head: [50, 34],
    neck: [58, 42],
    shoulderN: [52, 46],
    shoulderF: [70, 44],
    elbowN: [40, 60],
    handN: [34, 74],
    elbowF: [82, 30],
    handF: [86, 14],
    hipN: [50, 82],
    hipF: [72, 82],
    kneeN: [42, 98],
    footN: [36, 116],
    kneeF: [76, 100],
    footF: [84, 116],
  };
  // Mirrored character (faces left): the rig draws right-facing heads, so the
  // brute gets its own head below and an outsized, muscular build.
  const s: FigureStyle = style({ skin, top: skin, sleeve: skin, legs: T('l', 'L', '3'), boots: skin, headR: 0.01, bulk: 1.7 });
  paintFigure(rig, pose, s, undefined, () => {
    // Huge forearms and fists.
    rig.limb(pose.elbowN, pose.handN, 9, 8, skin);
    rig.limb(pose.elbowF, pose.handF, 9, 8, skin);
    p.ellipse(pose.handN[0], pose.handN[1], 5.5, 5, 'o');
    p.ellipse(pose.handF[0], pose.handF[1], 5.5, 5, 'o');
    // Chest muscles and belly.
    rig.shape([[46, 44], [76, 42], [74, 76], [48, 78]], skin, 64);
    p.line(61, 48, 61, 66, 'p');
    p.line(50, 56, 72, 54, 'p');
    // Loincloth.
    p.poly([[46, 76], [76, 76], [72, 98], [50, 98]], 'l');
    p.line(46, 77, 76, 77, 't', 2);
    // Hunched trapezius, thick neck, then the head thrust forward (left).
    p.ellipse(62, 44, 15, 7, 'o');
    p.ellipse(60, 42, 11, 4, 'O');
    rig.limb([58, 44], [50, 36], 12, 11, skin);
    p.ellipse(48, 33, 12, 11, 'o');
    p.ellipse(45, 30, 7, 5, 'O');
    p.poly([[36, 34], [52, 37], [50, 46], [37, 43]], 'p');
    p.poly([[42, 25], [33, 8], [47, 21]], 'x');
    p.poly([[54, 23], [64, 6], [58, 25]], 'x');
    p.line(42, 24, 35, 11, 'X');
    p.rect(39, 30, 4, 2, 'z');
    p.rect(47, 30, 4, 2, 'z');
    p.rect(38, 38, 10, 2, 'w');
    p.px(39, 40, 'w');
    p.px(46, 40, 'w');
  });
  return finish(p, {
    o: '#c0482a', p: '#7a2414', O: '#e8704a',
    l: '#3a2418', L: '#24160e', 3: '#5a3a24',
    x: '#e8dcc8', 5: '#a89a88', X: '#fffaf0',
    z: '#ffe040',
  }, [60, 117]);
}
