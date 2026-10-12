import { patch, recolor, shift, sym, type SpriteSpec } from '../lib';

const dir = 'assets/sprites/bosses';

// --- The Bone Herald: mid-run boss ------------------------------------------------
const heraldLegend = {
  k: '#120c18', w: '#ece4d0', W: '#b8ad96', e: '#c06bff', E: '#f0d0ff',
  p: '#3a1f5a', P: '#5a3488', f: '#a050ff', F: '#e0a0ff', g: '#d9a63a',
};
const heraldA = sym([
  '..........f.',
  '....f....fFf',
  '...fFf...fFf',
  '...fFfgggggg',
  '....kgggkggg',
  '....kwwwwwww',
  '...kwwwwwwww',
  '...kwwkkkkww',
  '...kwkEeekww',
  '...kwkeeekww',
  '...kWwkkkwww',
  '....kWwwwwwk',
  '....kWkwkwkw',
  '.....kkkkkkk',
  '..kkpppPPPPP',
  '.kpppPPPPPPP',
  'kppkpPPPgPPP',
  'kpk.kpPPgPPP',
  'kwk.kpPPgPPP',
  'kwwk.kpPgPPP',
  '.kk.kppPPPPP',
  '....kpppPPPP',
  '...kppppppPP',
  '...kkkkkkkkk',
]);
const heraldB = patch(recolor(heraldA, { f: 'F', F: 'f' }), 8, sym(['...kwkeEekww', '...kweeeekww']));
const heraldCast = recolor(heraldA, { e: 'E', p: 'P', f: 'F' });
export const herald: SpriteSpec = {
  name: 'bone_herald', dir, legend: heraldLegend,
  frames: [{ rows: heraldA, duration: 220 }, { rows: heraldB, duration: 220 }, { rows: heraldCast, duration: 120 }],
  animations: [
    { name: 'idle', from: 0, to: 1 },
    { name: 'cast', from: 2, to: 2 },
  ],
  autoplay: 'idle',
};

// --- The Ember Colossus: final boss ---------------------------------------------------
const colossusLegend = {
  k: '#120806', s: '#4a3f3a', S: '#6e605a', d: '#2e2522',
  m: '#ff5a1f', M: '#ffb02e', y: '#fff1a8', h: '#d8cfc0',
};
const colossusA = sym([
  '................',
  '...kk...........',
  '..khhk..........',
  '..khhhk.........',
  '...khhhk...kkkkk',
  '....khhhkkkSSSSS',
  '.....khhkSSSSSSS',
  '......kkSSSsssss',
  '.......kSSsMyMss',
  '.......kSSssmsss',
  '.......ksssmmmmm',
  '....kkkkkssdddss',
  '..kkSSSSkkssssss',
  '.kSSSSSSSkkkssss',
  'kSSSsssssSkssmmm',
  'kSSssmssssksmmMM',
  'kSsssmmsssksmMyy',
  'kSssssmsssksmMyy',
  'kssssssssskssmMM',
  '.kssssddsskkssmm',
  '.kSsssdkkk.ksssd',
  'kSSsmsk....kssdd',
  'kSmMmsk....ksddd',
  'kssmssk...kssddd',
  '.kkkkk..kSSsdk..',
  '........kSssdk..',
  '........kSssdk..',
  '........kkkkkk..',
]);
const colossusB = recolor(colossusA, { m: 'M', M: 'y' });
// Wind-up pose: fists raised (shifted up), core blazing.
const colossusWind = recolor(patch(colossusA, 20, shift(colossusA.slice(20, 24), 0, -1)), { m: 'y', M: 'y' });
export const colossus: SpriteSpec = {
  name: 'ember_colossus', dir, legend: colossusLegend,
  frames: [{ rows: colossusA, duration: 300 }, { rows: colossusB, duration: 300 }, { rows: colossusWind, duration: 100 }],
  animations: [
    { name: 'idle', from: 0, to: 1 },
    { name: 'windup', from: 2, to: 2 },
  ],
  autoplay: 'idle',
};

export const bosses = [herald, colossus];
