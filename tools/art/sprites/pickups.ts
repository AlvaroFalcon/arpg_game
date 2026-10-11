import { recolor, sym, type SpriteSpec } from '../lib';

const dir = 'assets/sprites/pickups';

const gemRows = sym([
  '..k',
  '.kw',
  'kwl',
  'kll',
  'kld',
  '.kd',
  '..k',
], true);
const gemLegend = (l: string, d: string) => ({ k: '#101018', w: '#ffffff', l, d });
const gem = (name: string, l: string, d: string): SpriteSpec => ({
  name, dir, legend: gemLegend(l, d),
  frames: [{ rows: gemRows, duration: 500 }, { rows: recolor(gemRows, { w: 'l' }), duration: 300 }],
  animations: [{ name: 'idle', from: 0, to: 1 }], autoplay: 'idle',
});
export const xpSmall = gem('xp_small', '#5fe36b', '#2a9a3a');
export const xpMedium = gem('xp_medium', '#5fb4ff', '#2a5ad0');
export const xpLarge = gem('xp_large', '#ff6b8a', '#c0204a');

const heartLegend = { k: '#2a0a10', r: '#e8304a', R: '#ff7a8a', w: '#ffffff' };
export const lifeOrb: SpriteSpec = {
  name: 'life_orb', dir, legend: heartLegend,
  frames: [{
    rows: sym([
      '.kk.',
      'kRwk',
      'kRRr',
      'krrr',
      '.krr',
      '..kr',
      '...k',
    ]),
    duration: 100,
  }],
};

const chestLegend = { k: '#1a0e06', w: '#8a5a2b', W: '#b07a3e', g: '#e8b84a', G: '#fff0a0', d: '#5a3818' };
export const chest: SpriteSpec = {
  name: 'chest', dir, legend: chestLegend,
  frames: [{
    rows: sym([
      '.kkkkkkk',
      'kWWWWWWW',
      'kWwwwwww',
      'kgggggga',
      'kwwwwwwk',
      'kwwwwwkG',
      'kwwwwwkg',
      'kdwwwwwk',
      'kdddddww',
      'kddddddd',
      '.kkkkkkk',
    ].map((r) => r.replace('a', 'g'))),
    duration: 100,
  }],
};

export const pickups = [xpSmall, xpMedium, xpLarge, lifeOrb, chest];
