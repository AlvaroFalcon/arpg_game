/**
 * Higher-definition player characters in an anime / JRPG style — owner brief
 * (2026-10-12): "more definition, more Japanese-anime-like art, and a portrait of
 * each character in the selection screen".
 *
 * Every class and ascendancy is a `Look` (hair style and colours, eyes, outfit,
 * armour type, weapon, accessories). From a look the generator paints:
 *  - a 32×48 chibi sprite (about three heads tall: big head, big eyes with
 *    highlights, expressive hair) with the same animations as before — idle (2),
 *    walk (4), attack (1) — drawn at twice the old resolution;
 *  - a 64×64 bust portrait for character select, creation and the altar.
 *
 * Everything goes through the same EasyPixel document + exporters as the rest of
 * the art (`draw_grid` rows + legend), so the voxel export picks them up too.
 */
import { Paint } from '../paint';
import type { SpriteSpec } from '../lib';

type HairStyle = 'spiky' | 'long' | 'wild' | 'topknot' | 'bob' | 'hood' | 'swept';
type Armor = 'plate' | 'leather' | 'robe' | 'fur' | 'gi';
type Weapon = 'sword_shield' | 'holy' | 'axe' | 'katana' | 'daggers' | 'staff' | 'tome';
type Extra = 'wizard_hat' | 'headband' | 'horns' | 'halo' | 'circlet' | 'mask' | 'scarf' | 'cape' | 'glasses' | 'hood_up';

export interface Look {
  name: string;
  hair: HairStyle;
  armor: Armor;
  weapon: Weapon;
  extras: Extra[];
  colors: Record<string, string>;
}

// Shared legend chars:
// k outline · s skin · S skin shade · n blush · h/H/j hair (base/light/dark) · e/E iris (base/dark) · w white
// m mouth · o/O/p outfit (base/light/dark) · t/T trim · l/L pants · b/B boots · c/C cape · x/X metal
// y handle/wood · z/Z glow · a/A accent (scarf, band) · f fur · q/Q hat or hood
const baseColors: Record<string, string> = {
  k: '#1a1220', s: '#ffe0c8', S: '#e8b49a', n: '#f4a0a0', w: '#ffffff', m: '#a8483a',
  b: '#3a2a26', B: '#5a4038', l: '#3a3550', L: '#2a2640', x: '#a8b4c4', X: '#e6eef8', y: '#7a4a28',
  t: '#d8a838', T: '#ffe080', f: '#c8a878',
};

const looks: Look[] = [
  // --- Arcanist family -----------------------------------------------------------
  {
    name: 'player', hair: 'long', armor: 'robe', weapon: 'staff', extras: ['wizard_hat'],
    colors: { h: '#f2a65a', H: '#ffd29a', j: '#c0703a', e: '#7a5cff', E: '#3a2a9a', o: '#5a48b8', O: '#7a68d8', p: '#3a2c80', q: '#4a3a9a', Q: '#2e2468', z: '#ff8a3a', Z: '#ffe0a0', a: '#e8b84a', A: '#a07820' },
  },
  {
    name: 'ascendant', hair: 'long', armor: 'robe', weapon: 'staff', extras: ['halo'],
    colors: { h: '#eef2ff', H: '#ffffff', j: '#b8c0e8', e: '#3ad8ff', E: '#1a7ab8', o: '#e8eeff', O: '#ffffff', p: '#a8b4e0', z: '#6af0ff', Z: '#e8ffff', a: '#ffe080', A: '#c8a040', t: '#8ad8ff', T: '#e0f8ff' },
  },
  {
    name: 'warlock', hair: 'swept', armor: 'robe', weapon: 'staff', extras: ['horns', 'cape'],
    colors: { h: '#2a1a3a', H: '#4a3060', j: '#140a20', e: '#ff3a4a', E: '#901020', o: '#3a1030', O: '#5a2048', p: '#200818', c: '#5a0a20', C: '#300410', z: '#b03aff', Z: '#f0b0ff', x: '#e8dcc8', X: '#fff8ec', t: '#a03aff', T: '#e0a0ff' },
  },
  {
    name: 'sage', hair: 'bob', armor: 'robe', weapon: 'tome', extras: ['circlet', 'glasses'],
    colors: { h: '#7ad0a0', H: '#b8f0d0', j: '#3a9068', e: '#2a8a5a', E: '#14583a', o: '#2e7a60', O: '#48a080', p: '#1a5040', z: '#fff0a0', Z: '#ffffff', t: '#e8d080', T: '#fff4c0', q: '#c8a050', Q: '#8a6a30' },
  },
  // --- Warrior family ------------------------------------------------------------
  {
    name: 'warrior', hair: 'spiky', armor: 'plate', weapon: 'sword_shield', extras: ['headband', 'scarf'],
    colors: { h: '#c0452a', H: '#f07848', j: '#7a2414', e: '#d8682a', E: '#8a3010', o: '#b03a2a', O: '#d85a40', p: '#7a2018', a: '#d83a2a', A: '#8a1a14' },
  },
  {
    name: 'barbarian', hair: 'wild', armor: 'fur', weapon: 'axe', extras: [],
    colors: { h: '#5a3020', H: '#8a5034', j: '#2e170e', e: '#e8a020', E: '#8a5a08', o: '#6a4428', O: '#8a6038', p: '#40281a', s: '#f0c8a0', S: '#c89870', f: '#d8c098', l: '#5a3a28', L: '#3a2418' },
  },
  {
    name: 'paladin', hair: 'swept', armor: 'plate', weapon: 'holy', extras: ['cape'],
    colors: { h: '#f8d870', H: '#fff4c0', j: '#c89a30', e: '#3a8ae8', E: '#1a4a9a', o: '#f2f2f8', O: '#ffffff', p: '#b8bccc', x: '#e8e0c8', X: '#fffaf0', c: '#2a4a9a', C: '#18306a', z: '#ffe060', Z: '#ffffff' },
  },
  {
    name: 'samurai', hair: 'topknot', armor: 'gi', weapon: 'katana', extras: [],
    colors: { h: '#1c1c28', H: '#3c3c52', j: '#0a0a12', e: '#c0302a', E: '#701410', o: '#2a2a3a', O: '#40405a', p: '#16161f', t: '#c8302a', T: '#f05a48', l: '#a82a24', L: '#701812', x: '#d8e0ec', X: '#ffffff' },
  },
  // --- Rogue family --------------------------------------------------------------
  {
    name: 'rogue', hair: 'hood', armor: 'leather', weapon: 'daggers', extras: ['scarf'],
    colors: { h: '#7a5a3a', H: '#a8805a', j: '#4a3420', e: '#3ab060', E: '#1a6a34', o: '#3a6a3a', O: '#548a50', p: '#244a26', q: '#2e5a30', Q: '#1c3a1e', a: '#c8a060', A: '#8a6a38', l: '#4a3a2a', L: '#30261a' },
  },
  {
    name: 'assassin', hair: 'hood', armor: 'leather', weapon: 'daggers', extras: ['mask'],
    colors: { h: '#d8d8e8', H: '#ffffff', j: '#9a9ab0', e: '#8a3aff', E: '#4a1aa0', o: '#3a1a3a', O: '#5a2a58', p: '#220c22', q: '#2a0e2a', Q: '#180618', a: '#4a1a4a', A: '#2a0a2a', x: '#6aff8a', X: '#d0ffe0', l: '#2a1a2a', L: '#180c18' },
  },
  {
    name: 'ninja', hair: 'spiky', armor: 'gi', weapon: 'daggers', extras: ['mask', 'scarf', 'headband'],
    colors: { h: '#2a2a3a', H: '#4a4a62', j: '#12121c', e: '#ff4a3a', E: '#a01a10', o: '#1e1e2a', O: '#30303f', p: '#101018', t: '#3a3a4a', T: '#5a5a70', a: '#d82a2a', A: '#801010', l: '#1e1e2a', L: '#101018' },
  },
];

const walkLegs: [number, number, number, number][] = [
  // left dx, left dy, right dx, right dy
  [-1, -1, 1, 0],
  [0, 0, 0, 0],
  [1, 0, -1, -1],
  [0, 0, 0, 0],
];

interface Pose {
  bob: number;
  legs: [number, number, number, number];
  armL: number;
  armR: number;
  attack: boolean;
}

function has(look: Look, e: Extra): boolean {
  return look.extras.includes(e);
}

// ---------------------------------------------------------------------------
// The chibi sprite (32×48)
// ---------------------------------------------------------------------------

function paintChibi(look: Look, pose: Pose): string[] {
  const p = new Paint(32, 48);
  const b = pose.bob;
  const robe = look.armor === 'robe';

  // Back layer: cape and long hair.
  if (has(look, 'cape')) {
    p.poly([[9, 23 + b], [23, 23 + b], [26, 45], [6, 45]], 'c');
    p.line(12, 28 + b, 10, 44, 'C');
    p.line(20, 28 + b, 22, 44, 'C');
  }

  if (look.hair === 'long') {
    p.poly([[7, 9 + b], [25, 9 + b], [27, 31 + b], [24, 33 + b], [16, 30 + b], [8, 33 + b], [5, 31 + b]], 'h');
    p.line(8, 14 + b, 7, 30 + b, 'j');
    p.line(24, 14 + b, 25, 30 + b, 'j');
  } else if (look.hair === 'wild') {
    p.poly([[5, 8 + b], [27, 8 + b], [29, 22 + b], [25, 26 + b], [20, 22 + b], [12, 22 + b], [7, 26 + b], [3, 22 + b]], 'h');
  }

  // Legs and boots.
  const [lx, ly, rx, ry] = pose.legs;
  if (!robe) {
    p.rect(10, 34 + b, 12, 2, 'l');
    p.rect(11 + lx, 35 + b, 4, 8 + ly - b, 'l');
    p.rect(17 + rx, 35 + b, 4, 8 + ry - b, 'l');
    p.rect(13 + lx, 36 + b, 2, 6 + ly - b, 'L');
    p.rect(19 + rx, 36 + b, 2, 6 + ry - b, 'L');
  }
  p.rect(10 + lx, 42 + ly, 5, 4, 'b');
  p.rect(17 + rx, 42 + ry, 5, 4, 'b');
  p.rect(10 + lx, 42 + ly, 5, 1, 'B');
  p.rect(17 + rx, 42 + ry, 5, 1, 'B');

  // Robe skirt.
  if (robe) {
    p.poly([[10, 31 + b], [22, 31 + b], [24, 43], [8, 43]], 'o');
    p.line(16, 33 + b, 16, 42, 'p');
    p.rect(8, 42, 17, 1, 't');
  }

  // Arms (sleeves) and hands.
  const sleeve = look.armor === 'fur' ? 's' : 'o';
  const al = pose.armL;
  const ar = pose.attack ? -3 : pose.armR;
  p.rect(6, 24 + b + al, 3, 8, sleeve);
  if (pose.attack) {
    p.poly([[23, 23 + b], [26, 23 + b], [29, 27 + b], [27, 29 + b]], sleeve);
  } else {
    p.rect(23, 24 + b + ar, 3, 8, sleeve);
  }
  p.ellipse(7.5, 32.5 + b + al, 1.8, 1.8, 's');
  const handR: [number, number] = pose.attack ? [28.5, 28.5 + b] : [24.5, 32.5 + b + ar];
  p.ellipse(handR[0], handR[1], 1.8, 1.8, 's');

  // Torso.
  p.poly([[9, 23 + b], [23, 23 + b], [22, 34 + b], [10, 34 + b]], look.armor === 'fur' ? 's' : 'o');
  switch (look.armor) {
    case 'plate':
      p.poly([[11, 24 + b], [21, 24 + b], [20, 32 + b], [12, 32 + b]], 'x');
      p.rect(13, 25 + b, 6, 1, 'X');
      p.rect(14, 27 + b, 4, 5, 'o');
      p.ellipse(9.5, 24.5 + b, 3, 2.4, 'x');
      p.ellipse(22.5, 24.5 + b, 3, 2.4, 'x');
      p.ellipse(9, 24 + b, 1.6, 1, 'X');
      p.ellipse(22, 24 + b, 1.6, 1, 'X');
      break;
    case 'leather':
      p.line(10, 24 + b, 21, 33 + b, 't');
      p.rect(12, 23 + b, 8, 2, 'O');
      break;
    case 'robe':
      p.poly([[13, 23 + b], [19, 23 + b], [16, 28 + b]], 'O');
      p.line(16, 28 + b, 16, 33 + b, 't');
      break;
    case 'fur':
      p.ellipse(10, 24 + b, 3.5, 2.2, 'f');
      p.ellipse(22, 24 + b, 3.5, 2.2, 'f');
      p.line(10, 25 + b, 21, 33 + b, 'y');
      p.rect(14, 28 + b, 1, 1, 'S');
      p.rect(17, 28 + b, 1, 1, 'S');
      break;
    case 'gi':
      p.poly([[12, 23 + b], [20, 23 + b], [16, 29 + b]], 'p');
      p.line(12, 23 + b, 16, 29 + b, 't');
      p.line(20, 23 + b, 16, 29 + b, 't');
      break;
  }
  p.rect(10, 32 + b, 12, 2, 't');
  p.rect(15, 32 + b, 2, 2, 'T');

  // Shading: light from the top-left.
  p.recolorWhere((x, y, c) => c === 'o' && x <= 11, 'O');
  p.recolorWhere((x, y, c) => c === 'o' && x >= 21, 'p');

  // Neck and head.
  p.rect(14, 20 + b, 4, 3, 'S');
  p.ellipse(16, 12 + b, 8.4, 8.2, 's');
  p.poly([[8, 12 + b], [24, 12 + b], [23, 17 + b], [19.5, 20.5 + b], [16, 21.5 + b], [12.5, 20.5 + b], [9, 17 + b]], 's');

  // Face: big anime eyes, brows, nose, mouth, blush.
  p.stamp(10, 13 + b, ['kkk', 'Eew', 'EeE', '.k.']);
  p.stamp(19, 13 + b, ['kkk', 'weE', 'EeE', '.k.']);
  p.px(16, 17 + b, 'S');
  if (!has(look, 'mask')) {
    p.rect(15, 19 + b, 2, 1, 'm');
    p.px(9, 17 + b, 'n');
    p.px(22, 17 + b, 'n');
  } else {
    p.poly([[8, 17 + b], [24, 17 + b], [22, 21 + b], [16, 22.5 + b], [10, 21 + b]], 'a');
  }
  if (has(look, 'glasses')) {
    p.line(9, 13 + b, 13, 13 + b, 't');
    p.line(18, 13 + b, 22, 13 + b, 't');
    p.px(9, 14 + b, 't');
    p.px(13, 14 + b, 't');
    p.px(18, 14 + b, 't');
    p.px(22, 14 + b, 't');
  }

  paintHairFront(p, look, b);
  paintHeadgear(p, look, b);
  if (has(look, 'scarf')) {
    p.rect(11, 21 + b, 10, 2, 'a');
    p.poly([[19, 22 + b], [22, 22 + b], [25, 28 + b], [22, 28 + b]], 'a');
    p.line(12, 22 + b, 20, 22 + b, 'A');
  }

  paintWeapon(p, look, pose, handR);
  p.outline('k');
  return p.rows();
}

function paintHairFront(p: Paint, look: Look, b: number): void {
  if (look.hair === 'hood') {
    // Hood frames the face; a few bangs show underneath.
    p.ellipse(16, 10 + b, 10.4, 9.6, 'q', '.hs');
    p.poly([[9, 10 + b], [23, 10 + b], [22, 18 + b], [10, 18 + b]], 's');
    p.ellipse(16, 12 + b, 7.4, 7.4, 's', 'q');
    p.poly([[9, 9 + b], [23, 9 + b], [22, 12 + b], [19, 10 + b], [16, 13 + b], [13, 10 + b], [10, 12 + b]], 'h');
    p.recolorWhere((x, y, c) => c === 'q' && x >= 21, 'Q');
    redrawEyes(p, b);
    return;
  }

  const wide = look.hair === 'wild' ? 10.6 : 9.4;
  p.ellipse(16, 9 + b, wide, 7.4, 'h');
  // Bangs: a zigzag over the forehead.
  p.poly([[7, 8 + b], [25, 8 + b], [24, 12 + b], [22, 10 + b], [20, 13 + b], [18, 10 + b], [16, 12 + b], [14, 10 + b], [12, 13 + b], [10, 10 + b], [8, 12 + b]], 'h');
  // Side locks frame the face.
  p.poly([[7, 9 + b], [9, 9 + b], [9.5, 18 + b], [7, 16 + b]], 'h');
  p.poly([[23, 9 + b], [25, 9 + b], [25, 16 + b], [22.5, 18 + b]], 'h');

  switch (look.hair) {
    case 'spiky':
      p.poly([[8, 6 + b], [5, 1 + b], [11, 3 + b]], 'h');
      p.poly([[12, 3 + b], [13, -1 + b], [17, 2 + b]], 'h');
      p.poly([[17, 2 + b], [21, -1 + b], [21, 4 + b]], 'h');
      p.poly([[21, 4 + b], [27, 2 + b], [24, 7 + b]], 'h');
      break;
    case 'wild':
      p.poly([[6, 6 + b], [2, 3 + b], [8, 3 + b]], 'h');
      p.poly([[24, 3 + b], [30, 3 + b], [26, 7 + b]], 'h');
      p.poly([[11, 2 + b], [13, -1 + b], [16, 2 + b]], 'h');
      p.poly([[16, 2 + b], [20, -1 + b], [21, 3 + b]], 'h');
      break;
    case 'topknot':
      p.ellipse(16, 2.5 + b, 3, 2.4, 'h');
      p.rect(15, 4 + b, 2, 1, 't');
      break;
    case 'bob':
      p.poly([[7, 10 + b], [9, 10 + b], [10, 20 + b], [7, 19 + b]], 'h');
      p.poly([[23, 10 + b], [25, 10 + b], [25, 19 + b], [22, 20 + b]], 'h');
      break;
    case 'swept':
      p.poly([[8, 5 + b], [20, 2 + b], [27, 6 + b], [24, 9 + b]], 'h');
      break;
    default:
      break;
  }

  // Highlights and shade.
  p.ellipse(12.5, 5 + b, 3.2, 1.3, 'H', 'h');
  p.ellipse(18, 4.5 + b, 1.6, 0.9, 'H', 'h');
  p.recolorWhere((x, y, c) => c === 'h' && x >= 22, 'j');
  redrawEyes(p, b);
  // Brows peek under the bangs.
  p.rect(10, 12 + b, 3, 1, 'j');
  p.rect(19, 12 + b, 3, 1, 'j');
}

function redrawEyes(p: Paint, b: number): void {
  p.stamp(10, 13 + b, ['kkk', 'Eew', 'EeE', '.k.']);
  p.stamp(19, 13 + b, ['kkk', 'weE', 'EeE', '.k.']);
}

function paintHeadgear(p: Paint, look: Look, b: number): void {
  if (has(look, 'wizard_hat')) {
    p.ellipse(16, 6.5 + b, 12.5, 2.4, 'q');
    p.poly([[10, 6 + b], [22, 6 + b], [20, 1 + b], [24, -1 + b], [16, 0 + b]], 'q');
    p.rect(10, 5 + b, 12, 1, 'a');
    p.recolorWhere((x, y, c) => c === 'q' && x >= 20, 'Q');
  }
  if (has(look, 'headband')) {
    p.rect(8, 8 + b, 17, 2, 'a');
    p.poly([[24, 8 + b], [29, 9 + b], [28, 13 + b], [25, 10 + b]], 'a');
    p.rect(15, 8 + b, 2, 2, 'x');
  }
  if (has(look, 'horns')) {
    p.poly([[9, 6 + b], [11, 4 + b], [7, -1 + b], [5, 1 + b]], 'x');
    p.poly([[23, 6 + b], [21, 4 + b], [25, -1 + b], [27, 1 + b]], 'x');
  }
  if (has(look, 'halo')) {
    p.ellipse(16, 0.8 + b, 6.5, 1.4, 'z');
    p.ellipse(16, 0.8 + b, 4.5, 0.6, '.', 'z');
  }
  if (has(look, 'circlet')) {
    p.line(8, 9 + b, 24, 9 + b, 't');
    p.px(16, 9 + b, 'z');
    p.px(16, 8 + b, 'z');
  }
}

function paintWeapon(p: Paint, look: Look, pose: Pose, hand: [number, number]): void {
  const [hx, hy] = hand;
  const b = pose.bob;
  switch (look.weapon) {
    case 'sword_shield':
    case 'holy': {
      const holy = look.weapon === 'holy';
      // Shield on the left arm.
      p.poly([[2, 26 + b], [10, 26 + b], [10, 33 + b], [6, 37 + b], [2, 33 + b]], holy ? 'O' : 'x');
      p.poly([[3, 27 + b], [9, 27 + b], [9, 32 + b], [6, 35 + b], [3, 32 + b]], holy ? 'o' : 'X', 'xO');
      if (holy) {
        p.rect(5, 28 + b, 2, 6, 'z');
        p.rect(3, 30 + b, 6, 2, 'z');
      } else {
        p.ellipse(6, 30.5 + b, 1.5, 1.5, 't');
      }
      if (pose.attack) {
        p.line(hx + 1, hy - 1, hx + 4, hy - 12, 'X', 2);
        p.line(hx + 1, hy, hx + 4, hy - 11, 'x');
        p.rect(hx - 1, hy - 1, 4, 1, 't');
      } else {
        p.rect(hx + 1, hy - 18, 2, 17, 'X');
        p.rect(hx + 2, hy - 17, 1, 15, 'x');
        p.rect(hx - 1, hy - 1, 6, 1, 't');
        p.rect(hx + 1, hy, 2, 3, 'y');
      }
      if (holy) p.px(hx + 1, hy - 18, 'z');
      break;
    }
    case 'axe': {
      const top = pose.attack ? hy - 14 : hy - 16;
      const ox = pose.attack ? 2 : 0;
      p.line(hx + ox, top, hx, hy + 6, 'y', 2);
      p.poly([[hx + ox + 1, top], [hx + ox + 6, top - 3], [hx + ox + 7, top + 6], [hx + ox + 1, top + 5]], 'x');
      p.line(hx + ox + 6, top - 2, hx + ox + 7, top + 5, 'X');
      break;
    }
    case 'katana':
      if (pose.attack) {
        p.line(hx, hy, hx + 3, hy - 17, 'X', 2);
      } else {
        p.line(hx - 1, hy + 2, hx + 5, hy - 18, 'X');
        p.line(hx, hy + 2, hx + 6, hy - 18, 'x');
      }
      p.rect(hx - 1, hy - 1, 3, 2, 't');
      break;
    case 'daggers':
      p.line(hx, hy + 1, hx + (pose.attack ? 5 : 3), hy + (pose.attack ? -4 : 6), 'X', 2);
      p.line(7, 34 + b + pose.armL, 4, 40 + b + pose.armL, 'X', 2);
      p.px(hx, hy, 'y');
      break;
    case 'staff':
    case 'tome': {
      const sx = pose.attack ? hx + 2 : hx + 2;
      const topY = pose.attack ? hy - 20 : 5 + b;
      p.line(sx, topY, sx, Math.min(45, hy + 12), 'y', 2);
      p.ellipse(sx + 0.5, topY - 1, 2.8, 2.8, 'z');
      p.ellipse(sx, topY - 1.6, 1.2, 1.2, 'Z');
      if (look.weapon === 'tome') {
        // A floating book by the left hand.
        p.rect(1, 25 + b, 6, 7, 'q');
        p.rect(2, 26 + b, 4, 5, 'w');
        p.line(4, 26 + b, 4, 30 + b, 'Q');
      }
      break;
    }
  }
}

// ---------------------------------------------------------------------------
// Back view (walking away, "up") — owner brief: characters should face up, down
// and sideways for the fake-3D look. Painted with the weapon hand on the image's
// right like the front, then mirrored, so the weapon ends up on the correct side.
// ---------------------------------------------------------------------------

function paintChibiBack(look: Look, pose: Pose): string[] {
  const p = new Paint(32, 48);
  const b = pose.bob;
  const robe = look.armor === 'robe';
  const handR: [number, number] = pose.attack ? [28.5, 24.5 + b] : [24.5, 32.5 + b + pose.armR];

  // The weapon is in front of the body, so from behind it sits behind it.
  paintWeapon(p, look, { ...pose, attack: pose.attack }, handR);

  // Legs and boots (the same stride as the front).
  const [lx, ly, rx, ry] = pose.legs;
  if (!robe) {
    p.rect(10, 34 + b, 12, 2, 'l');
    p.rect(11 + lx, 35 + b, 4, 8 + ly - b, 'l');
    p.rect(17 + rx, 35 + b, 4, 8 + ry - b, 'l');
    p.rect(11 + lx, 36 + b, 1, 6 + ly - b, 'L');
    p.rect(17 + rx, 36 + b, 1, 6 + ry - b, 'L');
  }
  p.rect(10 + lx, 42 + ly, 5, 4, 'b');
  p.rect(17 + rx, 42 + ry, 5, 4, 'b');
  if (robe) {
    p.poly([[10, 31 + b], [22, 31 + b], [24, 43], [8, 43]], 'o');
    p.line(16, 33 + b, 16, 42, 'p');
    p.rect(8, 42, 17, 1, 't');
  }

  // Arms.
  const sleeve = look.armor === 'fur' ? 's' : 'o';
  p.rect(6, 24 + b + pose.armL, 3, 8, sleeve);
  if (pose.attack) {
    p.poly([[23, 23 + b], [26, 22 + b], [29, 23 + b], [27, 26 + b]], sleeve);
  } else {
    p.rect(23, 24 + b + pose.armR, 3, 8, sleeve);
  }
  p.ellipse(7.5, 32.5 + b + pose.armL, 1.8, 1.8, 'S');
  p.ellipse(handR[0], handR[1], 1.8, 1.8, 'S');

  // Back of the torso: a seam down the spine, the belt, armour from behind.
  p.poly([[9, 23 + b], [23, 23 + b], [22, 34 + b], [10, 34 + b]], look.armor === 'fur' ? 'f' : 'o');
  p.line(16, 24 + b, 16, 31 + b, 'p');
  if (look.armor === 'plate') {
    p.poly([[11, 24 + b], [21, 24 + b], [20, 31 + b], [12, 31 + b]], 'x');
    p.line(16, 25 + b, 16, 30 + b, 'X');
    p.ellipse(9.5, 24.5 + b, 3, 2.4, 'x');
    p.ellipse(22.5, 24.5 + b, 3, 2.4, 'x');
  }
  p.rect(10, 32 + b, 12, 2, 't');
  p.recolorWhere((x, y, c) => c === 'o' && x >= 20, 'p');

  // Cape over the back.
  if (has(look, 'cape')) {
    p.poly([[9, 22 + b], [23, 22 + b], [26, 45], [6, 45]], 'c');
    p.line(13, 26 + b, 11, 44, 'C');
    p.line(19, 26 + b, 21, 44, 'C');
  }

  // Head from behind: all hair (or hood), the nape, the ears' hint.
  p.rect(14, 20 + b, 4, 3, 'S');
  if (look.hair === 'hood') {
    p.ellipse(16, 11 + b, 9.6, 9.4, 'q');
    p.poly([[9, 14 + b], [23, 14 + b], [20, 25 + b], [16, 27 + b], [12, 25 + b]], 'q');
    p.recolorWhere((x, y, c) => c === 'q' && x >= 20, 'Q');
    p.line(16, 5 + b, 16, 24 + b, 'Q');
  } else {
    p.ellipse(16, 11 + b, look.hair === 'wild' ? 10 : 8.8, 8.8, 'h');
    p.rect(8, 15 + b, 1, 2, 's'); // ears peeking
    p.rect(23, 15 + b, 1, 2, 's');
    switch (look.hair) {
      case 'long':
        p.poly([[8, 12 + b], [24, 12 + b], [25, 33 + b], [16, 35 + b], [7, 33 + b]], 'h');
        p.line(12, 18 + b, 11, 32 + b, 'j');
        p.line(20, 18 + b, 21, 32 + b, 'j');
        break;
      case 'wild':
        p.poly([[5, 10 + b], [27, 10 + b], [28, 24 + b], [22, 21 + b], [16, 25 + b], [10, 21 + b], [4, 24 + b]], 'h');
        break;
      case 'spiky':
        p.poly([[8, 6 + b], [5, 1 + b], [11, 3 + b]], 'h');
        p.poly([[12, 3 + b], [13, -1 + b], [17, 2 + b]], 'h');
        p.poly([[17, 2 + b], [21, -1 + b], [21, 4 + b]], 'h');
        p.poly([[21, 4 + b], [27, 2 + b], [24, 7 + b]], 'h');
        p.poly([[9, 16 + b], [12, 21 + b], [14, 17 + b], [17, 22 + b], [19, 17 + b], [22, 20 + b], [23, 16 + b]], 'h');
        break;
      case 'topknot':
        p.ellipse(16, 3 + b, 3, 2.4, 'h');
        p.rect(15, 4 + b, 2, 1, 't');
        break;
      case 'bob':
        p.poly([[7, 12 + b], [25, 12 + b], [25, 20 + b], [7, 20 + b]], 'h');
        break;
      case 'swept':
        p.poly([[8, 5 + b], [20, 2 + b], [27, 6 + b], [24, 9 + b]], 'h');
        break;
    }
    // Strands and a shine on the crown.
    p.line(12, 6 + b, 11, 16 + b, 'j');
    p.line(16, 4 + b, 16, 17 + b, 'j');
    p.line(20, 6 + b, 21, 16 + b, 'j');
    p.ellipse(13, 6 + b, 3, 1.2, 'H', 'h');
    p.recolorWhere((x, y, c) => c === 'h' && x >= 22, 'j');
  }

  // Accessories from behind.
  paintHeadgear(p, look, b);
  if (has(look, 'headband')) {
    p.poly([[14, 10 + b], [18, 10 + b], [20, 16 + b], [12, 16 + b]], 'a'); // knot tails
  }
  if (has(look, 'scarf')) {
    p.rect(11, 21 + b, 10, 2, 'a');
    p.poly([[14, 22 + b], [18, 22 + b], [19, 29 + b], [13, 29 + b]], 'a');
  }
  if (has(look, 'mask')) {
    p.rect(15, 16 + b, 2, 2, 'a'); // the tie
  }

  p.outline('k');
  return p.rows().map((r) => [...r].reverse().join(''));
}

// ---------------------------------------------------------------------------
// Side view (facing right; left is the same frames mirrored by the game).
// ---------------------------------------------------------------------------

function paintChibiSide(look: Look, pose: Pose, step: number): string[] {
  const p = new Paint(32, 48);
  const b = pose.bob;
  const robe = look.armor === 'robe';
  // Stride: the near leg forward when step > 0, back when step < 0.
  const near = step * 3;
  const far = -step * 3;
  const sleeve = look.armor === 'fur' ? 's' : 'o';

  // Behind everything: cape and long hair streaming back.
  if (has(look, 'cape')) {
    p.poly([[11, 22 + b], [16, 22 + b], [13, 45], [4, 44]], 'c');
    p.line(10, 28 + b, 7, 43, 'C');
  }
  if (look.hair === 'long') {
    p.poly([[8, 8 + b], [16, 8 + b], [14, 32 + b], [6, 33 + b], [5, 20 + b]], 'h');
    p.line(9, 14 + b, 8, 31 + b, 'j');
  } else if (look.hair === 'wild') {
    p.poly([[5, 6 + b], [16, 6 + b], [14, 22 + b], [3, 24 + b]], 'h');
  }

  // Far arm and far leg (behind the body, darker).
  p.rect(14 - Math.round(near / 2), 24 + b, 3, 8, look.armor === 'fur' ? 'S' : 'p');
  p.ellipse(15.5 - near / 2, 32.5 + b, 1.6, 1.6, 'S');
  if (!robe) {
    p.rect(14 + far, 35 + b, 4, 8 - b, 'L');
  }
  p.rect(14 + far, 42, 5, 4, 'B');
  if (look.weapon === 'sword_shield' || look.weapon === 'holy') {
    // The shield rides on the far arm: its edge shows behind the body.
    p.poly([[7, 25 + b], [11, 25 + b], [11, 35 + b], [9, 37 + b], [7, 35 + b]], look.weapon === 'holy' ? 'O' : 'x');
    p.line(8, 26 + b, 8, 35 + b, 'X');
  }

  // Near leg.
  if (!robe) {
    p.rect(13, 34 + b, 7, 2, 'l');
    p.rect(14 + near, 35 + b, 4, 8 - b, 'l');
    p.rect(16 + near, 36 + b, 1, 6 - b, 'L');
  }
  p.rect(14 + near, 42, 6, 4, 'b');
  p.rect(14 + near, 42, 6, 1, 'B');

  if (robe) {
    p.poly([[12, 31 + b], [21, 31 + b], [23 + Math.max(0, near), 43], [10 + Math.min(0, near), 43]], 'o');
    p.line(17, 33 + b, 18 + near / 2, 42, 'p');
    p.rect(10 + Math.min(0, near), 42, 14 + Math.abs(near), 1, 't');
  }

  // Torso in profile: chest forward, back straight.
  p.poly([[11, 23 + b], [20, 23 + b], [21, 30 + b], [20, 34 + b], [12, 34 + b]], look.armor === 'fur' ? 's' : 'o');
  switch (look.armor) {
    case 'plate':
      p.poly([[13, 24 + b], [20, 24 + b], [21, 30 + b], [14, 31 + b]], 'x');
      p.line(18, 25 + b, 20, 29 + b, 'X');
      p.ellipse(15, 24 + b, 3.4, 2.4, 'x');
      break;
    case 'fur':
      p.ellipse(15, 24 + b, 4, 2.2, 'f');
      break;
    case 'leather':
      p.line(13, 24 + b, 19, 33 + b, 't');
      break;
    case 'robe':
    case 'gi':
      p.line(19, 23 + b, 18, 29 + b, look.armor === 'gi' ? 't' : 'O');
      break;
  }
  p.rect(12, 32 + b, 9, 2, 't');
  p.recolorWhere((x, y, c) => c === 'o' && x <= 12, 'p');

  // Neck and head in profile: the face on the right, hair over the back.
  p.rect(15, 20 + b, 3, 3, 'S');
  p.ellipse(16, 12 + b, 8, 8.2, 's');
  p.poly([[17, 14 + b], [24, 13 + b], [23.5, 17 + b], [21, 20.5 + b], [16, 21 + b]], 's');
  p.px(24, 15 + b, 's'); // the nose
  if (look.hair === 'hood') {
    p.ellipse(15, 10 + b, 10, 9.5, 'q', '.hs');
    p.poly([[6, 10 + b], [15, 8 + b], [17, 22 + b], [8, 25 + b]], 'q');
    p.ellipse(19.5, 13 + b, 5, 6, 's', 'q');
    p.poly([[15, 6 + b], [24, 7 + b], [23, 10 + b], [16, 11 + b]], 'h');
    p.recolorWhere((x, y, c) => c === 'q' && x <= 9, 'Q');
  } else {
    p.ellipse(14, 9 + b, look.hair === 'wild' ? 10 : 9, 7.6, 'h');
    p.poly([[6, 8 + b], [17, 8 + b], [16, 18 + b], [12, 21 + b], [7, 17 + b]], 'h'); // back of the head
    p.poly([[14, 4 + b], [24, 6 + b], [25, 10 + b], [22, 9 + b], [21, 12 + b], [19, 9 + b], [17, 11 + b]], 'h'); // bangs over the brow
    switch (look.hair) {
      case 'spiky':
        p.poly([[8, 5 + b], [2, 2 + b], [9, 1 + b]], 'h');
        p.poly([[12, 2 + b], [10, -2 + b], [16, 1 + b]], 'h');
        p.poly([[17, 2 + b], [20, -2 + b], [21, 4 + b]], 'h');
        p.poly([[7, 12 + b], [1, 12 + b], [7, 16 + b]], 'h');
        break;
      case 'topknot':
        p.ellipse(13, 2.5 + b, 3, 2.4, 'h');
        p.rect(12, 4 + b, 2, 1, 't');
        break;
      case 'bob':
        p.poly([[7, 10 + b], [14, 10 + b], [14, 20 + b], [7, 19 + b]], 'h');
        break;
      case 'swept':
        p.poly([[9, 4 + b], [20, 1 + b], [27, 5 + b], [22, 8 + b]], 'h');
        break;
      default:
        break;
    }
    p.rect(14, 13 + b, 2, 3, 's'); // ear
    p.px(15, 14 + b, 'S');
    p.ellipse(11, 5 + b, 3, 1.2, 'H', 'h');
    p.recolorWhere((x, y, c) => c === 'h' && x <= 9, 'j');
  }

  // One big eye, brow, mouth, blush.
  p.stamp(20, 13 + b, ['kkk', 'eEw', 'EEe', '.k.']);
  p.rect(20, 12 + b, 3, 1, 'j');
  if (!has(look, 'mask')) {
    p.px(22, 19 + b, 'm');
    p.px(21, 17 + b, 'n');
  } else {
    p.poly([[16, 17 + b], [24, 16 + b], [23, 20 + b], [17, 21 + b]], 'a');
  }
  if (has(look, 'glasses')) {
    p.rect(19, 13 + b, 5, 1, 't');
    p.line(15, 13 + b, 19, 13 + b, 't');
  }

  // Headgear in profile.
  if (has(look, 'wizard_hat')) {
    p.ellipse(16, 6.5 + b, 11, 2.2, 'q');
    p.poly([[10, 6 + b], [21, 6 + b], [16, 0 + b], [7, -1 + b], [12, 1 + b]], 'q');
    p.rect(10, 5 + b, 11, 1, 'a');
    p.recolorWhere((x, y, c) => c === 'q' && x <= 11, 'Q');
  }
  if (has(look, 'headband')) {
    p.rect(8, 8 + b, 16, 2, 'a');
    p.poly([[8, 8 + b], [2, 11 + b], [3, 14 + b], [8, 10 + b]], 'a');
  }
  if (has(look, 'horns')) {
    p.poly([[16, 5 + b], [19, 4 + b], [16, -1 + b], [13, 1 + b]], 'x');
  }
  if (has(look, 'halo')) {
    p.ellipse(15, 0.8 + b, 6.5, 1.4, 'z');
    p.ellipse(15, 0.8 + b, 4.5, 0.6, '.', 'z');
  }
  if (has(look, 'circlet')) {
    p.line(8, 9 + b, 24, 9 + b, 't');
    p.px(23, 9 + b, 'z');
  }
  if (has(look, 'scarf')) {
    p.rect(13, 21 + b, 8, 2, 'a');
    p.poly([[13, 22 + b], [8, 24 + b], [5, 30 + b], [10, 27 + b]], 'a');
  }

  // Near arm swinging opposite to the near leg; forward on attack.
  const handN: [number, number] = pose.attack ? [27.5, 27.5 + b] : [17.5 - near / 2, 32.5 + b];
  if (pose.attack) {
    p.poly([[15, 23 + b], [18, 23 + b], [28, 26 + b], [26, 29 + b]], sleeve);
  } else {
    p.poly([[15, 23 + b], [18, 23 + b], [19 - near / 2, 31 + b], [16 - near / 2, 31 + b]], sleeve);
  }
  p.ellipse(handN[0], handN[1], 1.8, 1.8, 's');
  paintSideWeapon(p, look, pose, handN);

  p.outline('k');
  return p.rows();
}

function paintSideWeapon(p: Paint, look: Look, pose: Pose, hand: [number, number]): void {
  const [hx, hy] = hand;
  switch (look.weapon) {
    case 'sword_shield':
    case 'holy':
    case 'katana':
      if (pose.attack) {
        p.line(hx + 1, hy - 1, hx + 4, hy - 1 - 14, 'X', 2);
        p.line(hx, hy - 2, hx + 3, hy - 14, 'x');
      } else {
        p.line(hx, hy, hx + 3, hy - 16, 'X', 2);
        p.line(hx - 1, hy, hx + 2, hy - 16, 'x');
      }
      p.rect(hx - 2, hy - 1, 5, 1, 't');
      if (look.weapon === 'holy') p.px(hx + 3, hy - 16, 'z');
      break;
    case 'axe':
      p.line(hx, hy + 5, hx + 2, hy - 13, 'y', 2);
      p.poly([[hx + 3, hy - 13], [hx + 9, hy - 15], [hx + 9, hy - 6], [hx + 3, hy - 8]], 'x');
      p.line(hx + 9, hy - 15, hx + 9, hy - 6, 'X');
      break;
    case 'daggers':
      p.line(hx, hy, hx + (pose.attack ? 6 : 3), hy + (pose.attack ? -1 : 6), 'X', 2);
      p.px(hx, hy, 'y');
      break;
    case 'staff':
    case 'tome': {
      const topY = pose.attack ? hy - 18 : 5 + pose.bob;
      const sx = pose.attack ? hx + 1 : hx + 1;
      p.line(sx, topY, sx, Math.min(45, hy + 12), 'y', 2);
      p.ellipse(sx + 0.5, topY - 1, 2.8, 2.8, 'z');
      p.ellipse(sx, topY - 1.6, 1.2, 1.2, 'Z');
      if (look.weapon === 'tome') {
        p.rect(20, 24 + pose.bob, 6, 7, 'q');
        p.rect(21, 25 + pose.bob, 4, 5, 'w');
      }
      break;
    }
  }
}

function chibiSpec(look: Look): SpriteSpec {
  const poses: Pose[] = [
    { bob: 0, legs: [0, 0, 0, 0], armL: 0, armR: 0, attack: false },
    { bob: 1, legs: [0, 0, 0, 0], armL: 0, armR: 0, attack: false },
    ...walkLegs.map((legs, i) => ({ bob: i % 2, legs, armL: i === 0 ? 1 : i === 2 ? -1 : 0, armR: i === 0 ? -1 : i === 2 ? 1 : 0, attack: false })),
    { bob: 0, legs: [0, 0, 0, 0], armL: 0, armR: 0, attack: true },
  ];
  // Side strides: contact, pass, contact (other leg), pass.
  const sideSteps = [0, 0, 1, 0, -1, 0, 0];
  const durations = (i: number) => (i < 2 ? 420 : i < 6 ? 120 : 140);
  const frames = [
    ...poses.map((pose, i) => ({ rows: paintChibi(look, pose), duration: durations(i) })),
    ...poses.map((pose, i) => ({ rows: paintChibiBack(look, pose), duration: durations(i) })),
    ...poses.map((pose, i) => ({ rows: paintChibiSide(look, pose, sideSteps[i]), duration: durations(i) })),
  ];
  // "idle"/"walk"/"attack" stay the front (down) view so existing callers keep
  // working; "_up" is the back view and "_side" the profile facing right.
  const animations = ['', '_up', '_side'].flatMap((suffix, d) => [
    { name: `idle${suffix}`, from: d * 7, to: d * 7 + 1 },
    { name: `walk${suffix}`, from: d * 7 + 2, to: d * 7 + 5 },
    { name: `attack${suffix}`, from: d * 7 + 6, to: d * 7 + 6, loop: false },
  ]);
  return {
    name: look.name,
    dir: 'assets/sprites/player',
    legend: { ...baseColors, ...look.colors },
    frames,
    animations,
    autoplay: 'idle',
  };
}

// ---------------------------------------------------------------------------
// The portrait (64×64 bust)
// ---------------------------------------------------------------------------

function paintPortrait(look: Look): string[] {
  const p = new Paint(64, 64);

  if (look.hair === 'long' || look.hair === 'wild') {
    p.poly([[11, 20], [53, 20], [57, 60], [44, 63], [20, 63], [7, 60]], 'h');
    p.line(14, 30, 12, 58, 'j', 2);
    p.line(50, 30, 52, 58, 'j', 2);
  }
  if (has(look, 'cape')) {
    p.poly([[6, 52], [58, 52], [64, 64], [0, 64]], 'c');
  }

  // Shoulders and outfit.
  p.poly([[8, 64], [12, 52], [24, 47], [40, 47], [52, 52], [56, 64]], look.armor === 'fur' ? 's' : 'o');
  p.rect(27, 41, 10, 8, 'S');
  switch (look.armor) {
    case 'plate':
      p.ellipse(13, 55, 8, 6, 'x');
      p.ellipse(51, 55, 8, 6, 'x');
      p.ellipse(11, 52, 4, 2, 'X');
      p.ellipse(49, 52, 4, 2, 'X');
      p.poly([[22, 50], [42, 50], [40, 64], [24, 64]], 'x');
      p.rect(25, 52, 14, 2, 'X');
      break;
    case 'fur':
      p.ellipse(16, 51, 10, 5, 'f');
      p.ellipse(48, 51, 10, 5, 'f');
      p.line(20, 54, 44, 64, 'y', 2);
      break;
    case 'leather':
      p.line(14, 54, 40, 64, 't', 2);
      break;
    case 'robe':
    case 'gi':
      p.poly([[24, 47], [40, 47], [32, 60]], 'p');
      p.line(24, 47, 32, 60, 't', 2);
      p.line(40, 47, 32, 60, 't', 2);
      break;
  }
  p.recolorWhere((x, y, c) => c === 'o' && x < 20, 'O');
  p.recolorWhere((x, y, c) => c === 'o' && x > 46, 'p');

  // Face: soft round head, pointed anime chin.
  p.ellipse(32, 26, 16.5, 16.5, 's');
  p.poly([[16, 26], [48, 26], [46, 36], [39, 43], [32, 45.5], [25, 43], [18, 36]], 's');
  p.recolorWhere((x, y, c) => c === 's' && x >= 44 && y >= 28, 'S');


  // Hair.
  if (look.hair === 'hood') {
    p.ellipse(32, 24, 22, 21, 'q', '.h');
    p.poly([[13, 20], [51, 20], [50, 40], [44, 46], [20, 46], [14, 40]], 'q', '.');
    p.poly([[16, 14], [48, 14], [47, 24], [42, 20], [37, 25], [32, 20], [27, 25], [22, 20], [17, 24]], 'h');
    p.recolorWhere((x, y, c) => c === 'q' && x > 44, 'Q');
  } else {
    const rx = look.hair === 'wild' ? 21 : 19;
    p.ellipse(32, 19, rx, 15, 'h');
    p.poly([[13, 16], [51, 16], [50, 26], [46, 21], [42, 27], [38, 21], [34, 26], [30, 21], [26, 27], [22, 21], [18, 26], [14, 22]], 'h');
    p.poly([[13, 18], [18, 18], [19, 38], [13, 33]], 'h');
    p.poly([[46, 18], [51, 18], [51, 33], [45, 38]], 'h');
    switch (look.hair) {
      case 'spiky':
        p.poly([[16, 12], [8, 2], [24, 7]], 'h');
        p.poly([[24, 7], [28, 0], [34, 5]], 'h');
        p.poly([[34, 5], [42, -1], [42, 8]], 'h');
        p.poly([[42, 8], [56, 3], [49, 14]], 'h');
        break;
      case 'wild':
        p.poly([[12, 12], [2, 6], [14, 6]], 'h');
        p.poly([[50, 6], [62, 6], [52, 14]], 'h');
        p.poly([[22, 5], [26, 0], [32, 4]], 'h');
        p.poly([[32, 4], [40, -1], [42, 6]], 'h');
        break;
      case 'topknot':
        p.ellipse(32, 4, 6, 4.5, 'h');
        p.rect(29, 7, 6, 2, 't');
        break;
      case 'bob':
        p.poly([[12, 20], [18, 20], [19, 44], [12, 42]], 'h');
        p.poly([[46, 20], [52, 20], [52, 42], [45, 44]], 'h');
        break;
      case 'swept':
        p.poly([[15, 12], [36, 3], [54, 11], [48, 18]], 'h');
        break;
      case 'long':
        p.poly([[12, 20], [18, 20], [17, 50], [11, 48]], 'h');
        p.poly([[46, 20], [52, 20], [53, 48], [47, 50]], 'h');
        break;
    }
    p.ellipse(24, 9, 7, 2.6, 'H', 'h');
    p.ellipse(36, 8, 3, 1.4, 'H', 'h');
    p.recolorWhere((x, y, c) => c === 'h' && x >= 46, 'j');
    // Brows under the bangs.
    p.rect(19, 23, 7, 1, 'j');
    p.rect(38, 23, 7, 1, 'j');
  }

  // Face features go over the hair so bangs never hide the eyes.
  // Big eyes: lash line, iris with a darker top, two highlights.
  for (const [cx, flip] of [[24, false], [40, true]] as [number, boolean][]) {
    p.ellipse(cx, 31, 4.6, 5.6, 'w');
    p.ellipse(cx, 31.5, 3.4, 5, 'e');
    p.ellipse(cx, 29.5, 3.4, 2.8, 'E', 'e');
    p.ellipse(cx, 32, 1.4, 2, 'E');
    p.rect(cx - 5, 25, 10, 2, 'k');
    p.px(flip ? cx + 5 : cx - 6, 26, 'k');
    p.px(flip ? cx + 5 : cx - 6, 27, 'k');
    p.rect(cx - (flip ? 0 : 2), 28, 2, 2, 'w');
    p.px(cx + (flip ? -2 : 2), 34, 'w');
    p.rect(cx - 2, 37, 5, 1, 'S');
  }
  // Brows, nose, mouth, blush.
  if (!has(look, 'mask')) {
    p.rect(30, 41, 4, 1, 'm');
    p.px(29, 40, 'm');
    p.ellipse(19, 38, 2.6, 1.2, 'n');
    p.ellipse(45, 38, 2.6, 1.2, 'n');
  } else {
    p.poly([[15, 37], [49, 37], [46, 43], [32, 48], [18, 43]], 'a');
    p.line(18, 40, 46, 40, 'A');
  }
  p.px(33, 37, 'S');
  p.px(33, 38, 'S');
  if (has(look, 'glasses')) {
    p.ellipse(24, 31, 6.4, 6, 't');
    p.ellipse(24, 31, 5.2, 4.8, '.', 't');
    p.ellipse(40, 31, 6.4, 6, 't');
    p.ellipse(40, 31, 5.2, 4.8, '.', 't');
    p.rect(30, 30, 4, 1, 't');
    // Re-draw the eyes inside the frames.
    for (const cx of [24, 40]) {
      p.ellipse(cx, 31, 4.6, 4.6, 'w', '.');
      p.ellipse(cx, 31.5, 3.4, 4.2, 'e', 'w.');
      p.ellipse(cx, 29.5, 3.4, 2.4, 'E', 'e');
      p.rect(cx - 1, 28, 2, 2, 'w');
    }
  }

  // Headgear.
  if (has(look, 'wizard_hat')) {
    p.ellipse(32, 13, 27, 4.6, 'q');
    p.poly([[18, 12], [46, 12], [42, 3], [52, -1], [36, 0]], 'q');
    p.rect(19, 10, 26, 2, 'a');
    p.recolorWhere((x, y, c) => c === 'q' && x > 42, 'Q');
  }
  if (has(look, 'headband')) {
    p.rect(13, 16, 38, 3, 'a');
    p.rect(29, 16, 6, 3, 'x');
    p.poly([[50, 16], [60, 19], [57, 26], [51, 20]], 'a');
  }
  if (has(look, 'horns')) {
    p.poly([[16, 10], [20, 6], [12, -1], [8, 3]], 'x');
    p.poly([[48, 10], [44, 6], [52, -1], [56, 3]], 'x');
  }
  if (has(look, 'halo')) {
    p.ellipse(32, 3, 14, 2.6, 'z');
    p.ellipse(32, 3, 11, 1.2, '.', 'z');
  }
  if (has(look, 'circlet')) {
    p.line(14, 18, 50, 18, 't', 2);
    p.ellipse(32, 18, 2, 2, 'z');
  }
  if (has(look, 'scarf') && !has(look, 'mask')) {
    p.poly([[22, 46], [42, 46], [44, 51], [20, 51]], 'a');
  }

  p.outline('k');
  return p.rows();
}

function portraitSpec(look: Look): SpriteSpec {
  return {
    name: look.name,
    dir: 'assets/portraits',
    legend: { ...baseColors, ...look.colors },
    frames: [{ rows: paintPortrait(look) }],
    animations: [{ name: 'idle', from: 0, to: 0 }],
    autoplay: 'idle',
  };
}

export const animeCharacters: SpriteSpec[] = looks.map(chibiSpec);
export const portraits: SpriteSpec[] = looks.map(portraitSpec);
