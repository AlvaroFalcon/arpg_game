import type { SpriteSpec } from '../lib';

/**
 * Station icons for the town map (scripts/UI/TownMap.cs draws them in a round
 * badge ringed with the station's colour) — docs/design/hideout.md -> "Town map".
 * One 16×16 icon per station, named map_<station id>.
 */
const dir = 'assets/ui/mapicons';
const icon = (id: string, legend: Record<string, string>, rows: string[]): SpriteSpec => ({
  name: `map_${id}`, dir, legend, frames: [{ rows, duration: 100 }],
});
const k = '#0c0810';

// The Mirror of Realities: a gilded oval mirror.
export const mapMirror = icon('mirror', { k, g: '#c8902e', G: '#ffe08a', b: '#3d6fd0', B: '#8fc4ff', w: '#ffffff' }, [
  '.....kkkkkk.....',
  '...kkgGGGGgkk...',
  '..kgGkkkkkkGgk..',
  '..kGkbbbbbbkGk..',
  '.kgkbBwbbbbbkgk.',
  '.kGkbwBbbbbbkGk.',
  '.kGkbBbbbbbbkGk.',
  '.kGkbbbbbbbbkGk.',
  '.kGkbbbbbbBbkGk.',
  '.kgkbbbbbBwbkgk.',
  '..kGkbbbbbbkGk..',
  '..kgGkkkkkkGgk..',
  '...kkgGGGGgkk...',
  '.....kkggkk.....',
  '....kggggggk....',
  '....kkkkkkkk....',
]);

// Odran the Trader: a coin purse with a gold coin on it.
export const mapMerchant = icon('merchant', { k, b: '#9a6a3a', B: '#c8955a', d: '#5e3c1e', Y: '#ffd84a', y: '#c8941e' }, [
  '................',
  '.....kk..kk.....',
  '....kddkkddk....',
  '.....kYYYYk.....',
  '......kddk......',
  '....kkbbbbkk....',
  '...kbbBbbbbbk...',
  '..kbBbbkkkbbbk..',
  '..kbBbkYYYkbbk..',
  '.kbbbbkYyYkbbbk.',
  '.kbbbbkYYYkbbbk.',
  '.kbbbbbkkkbbbbk.',
  '.kdbbbbbbbbbbdk.',
  '..kddbbbbbbddk..',
  '...kkddddddkk...',
  '.....kkkkkk.....',
]);

// Arehucaz, the shard dealer: a Mirror Shard.
export const mapArehucaz = icon('arehucaz', { k, W: '#f2e4ff', p: '#c79aff', P: '#8a52e0', d: '#4e2690' }, [
  '.......kk.......',
  '......kWpk......',
  '......kWpk......',
  '.....kWppPk.....',
  '.....kWpPPk.....',
  '....kWppPPPk....',
  '....kWpPPPPk....',
  '...kWppPPPPPk...',
  '...kWpPPPPPdk...',
  '...kWpPPPPddk...',
  '....kWPPPddk....',
  '....kpPPdddk....',
  '.....kPdddk.....',
  '.....kpddk......',
  '......kdk.......',
  '.......k........',
]);

// Ysolde's Furnishings: a potted plant.
export const mapDecorator = icon('decorator', { k, L: '#8fe07a', l: '#3f9a3c', T: '#e0884a', t: '#a8542a' }, [
  '.......kk.......',
  '......kLLk......',
  '..kk..kLlk..kk..',
  '.kLLk.kLlk.kLLk.',
  '.kLllkkLlkkllLk.',
  '..kLllkLlkllLk..',
  '...kkllLlllkk...',
  '.....kklLlkk....',
  '......klLk......',
  '...kkkkkkkkkk...',
  '...kTTTTTTTTk...',
  '....kTtttttk....',
  '....kTttttTk....',
  '.....kTtttk.....',
  '.....kkkkkk.....',
  '................',
]);

// The Artificer's Forge: an anvil under a flame.
export const mapForge = icon('forge', { k, A: '#c8d0e0', a: '#6a7488', R: '#e0402a', O: '#ff9a30', Y: '#ffe680' }, [
  '.......kk.......',
  '......kRYk......',
  '.....kRYYRk.....',
  '.....kROYRk.....',
  '......kRRk......',
  '................',
  '..kkkkkkkkkkkk..',
  'kkkAAAAAAAAAAAk.',
  '.kaaAAAAAAAAAak.',
  '...kaaaaaaaak...',
  '.....kaaaak.....',
  '.....kaaaak.....',
  '....kaaaaaak....',
  '...kaaaaaaaak...',
  '...kkkkkkkkkk...',
  '................',
]);

// The Stash: an iron-banded chest.
export const mapStash = icon('stash', { k, W: '#d8a060', w: '#a8703a', d: '#6a4220', G: '#e8c050', Y: '#fff0a0' }, [
  '................',
  '................',
  '...kkkkkkkkkk...',
  '..kWWWWWWWWWWk..',
  '.kWwwwwwwwwwwWk.',
  '.kwwwwwwwwwwwwk.',
  '.kGGGGGkkGGGGGk.',
  '.kwwwwkYYkwwwwk.',
  '.kwwwwkYYkwwwwk.',
  '.kwwwwwkkwwwwwk.',
  '.kGGGGGGGGGGGGk.',
  '.kdwwwwwwwwwwdk.',
  '.kddddddddddddk.',
  '.kkkkkkkkkkkkkk.',
  '................',
  '................',
]);

// The Altar of Ascension: an orb floating over a stone pedestal.
export const mapAltar = icon('altar', { k, W: '#f4e8ff', p: '#d2a8ff', P: '#9a60f0', d: '#5a2ea0', S: '#c8c4d4', s: '#8a869a' }, [
  '................',
  '......kkkk......',
  '.....kWppPk.....',
  '....kWpPPPPk....',
  '....kpPPPPdk....',
  '.....kPPddk.....',
  '..W...kkkk...W..',
  '................',
  '...kkkkkkkkkk...',
  '...kSSSSSSSSk...',
  '....kssssssk....',
  '.....kSssSk.....',
  '.....kSssSk.....',
  '....kSSssSSk....',
  '...kkkkkkkkkk...',
  '................',
]);

// The Survival Rift: a swirling tear.
export const mapPortal = icon('portal', { k, P: '#5a2ea0', p: '#8a52e0', V: '#b47aff', w: '#f0dcff' }, [
  '.....kkkkkk.....',
  '...kkPPPPPPkk...',
  '..kPPpppppPPPk..',
  '.kPppVVVVVVppPk.',
  '.kPpVVwwwVVVpPk.',
  'kPpVVwVVVwVVVpPk',
  'kPpVwVVwwVwVVpPk',
  'kPpVwVwVVwVwVpPk',
  'kPpVVwVwwVVwVpPk',
  'kPpVVVwVVVwVVpPk',
  '.kPpVVVwwwVVpPk.',
  '.kPppVVVVVVppPk.',
  '..kPPpppppPPPk..',
  '...kkPPPPPPkk...',
  '.....kkkkkk.....',
  '................',
]);

export const mapIcons: SpriteSpec[] = [mapMirror, mapMerchant, mapArehucaz, mapDecorator, mapForge, mapStash, mapAltar, mapPortal];
