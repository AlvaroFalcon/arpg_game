import { recolor, sym, type SpriteSpec } from '../lib';

// Training dummy for the hideout: a straw-stuffed sack on a post.
const legend = { k: '#1a120c', s: '#c9a25a', S: '#e8c87a', d: '#8a6a32', w: '#6a4a2a', W: '#8a5a2b', r: '#a33a2a', R: '#d4553a' };
const dummy = sym([
  '....kkk',
  '...kSSs',
  '..kSSss',
  '..kSrRs',
  '..kSsss',
  '...kdss',
  'kkkkkkk',
  'kSSsssW',
  'kkkksss',
  '..kSsss',
  '..kSrRs',
  '..kSsss',
  '..ksdds',
  '...kkkW',
  '.....kW',
  '.....kW',
  '.....kW',
  '...kkkW',
  '..kwwww',
  '..kkkkk',
], true);
export const trainingDummy: SpriteSpec = {
  name: 'training_dummy',
  dir: 'assets/sprites/props',
  legend,
  frames: [{ rows: dummy, duration: 500 }, { rows: recolor(dummy, { r: 'R', R: 'r' }), duration: 500 }],
  animations: [{ name: 'idle', from: 0, to: 1 }],
  autoplay: 'idle',
};

export const props = [trainingDummy];
