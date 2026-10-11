import { recolor, sym, type SpriteSpec } from '../lib';

// --- Skill icons for the melee classes (same 16x16 frame as icons.ts) ------------------
const frame = { k: '#0e0c14', f: '#2a2438', F: '#3a3350' };
function icon(name: string, legend: Record<string, string>, rows: string[]): SpriteSpec {
  return { name, dir: 'assets/sprites/icons', legend: { ...frame, ...legend }, frames: [{ rows, duration: 100 }] };
}

export const iconCleave = icon('icon_cleave', { s: '#c8d0da', w: '#ffffff', a: '#ffb070' }, [
  'kkkkkkkkkkkkkkkk',
  'kffffffffffffffk',
  'kfffffaaaaffffFk',
  'kffffaawwaaffffk',
  'kfffaawffwaafffk',
  'kffaawffffwaaffk',
  'kffawfffffswaffk',
  'kfawfffffssfwafk',
  'kfawffffssfffafk',
  'kfafffffsffffffk',
  'kffffffsfffffffk',
  'kfffffsffffffFFk',
  'kffffsfffffFFFFk',
  'kfffsffffFFFFFFk',
  'kffsffFFFFFFFFFk',
  'kkkkkkkkkkkkkkkk',
]);

export const iconGroundSlam = icon('icon_ground_slam', { s: '#8a6a4a', S: '#c49a6a', y: '#ffd23f', w: '#ffffff' }, [
  'kkkkkkkkkkkkkkkk',
  'kffffffSSffffffk',
  'kfffffSSSSfffffk',
  'kfffffSSSSfffffk',
  'kffffffssffffffk',
  'kffffffssffffffk',
  'kffffffssffffffk',
  'kffffffssffffffk',
  'kffffffssffffffk',
  'kfffyffssffyfffk',
  'kffyfwfssfwfyffk',
  'kfyffwwssswffyfk',
  'kSSSSSSSSSSSSSSk',
  'kSsSsSsSsSsSsSsk',
  'kffffffffffffffk',
  'kkkkkkkkkkkkkkkk',
]);

export const iconSunder = icon('icon_sunder', { s: '#d0d8e0', y: '#ffb347', o: '#ff6a1a', w: '#fff6d0' }, [
  'kkkkkkkkkkkkkkkk',
  'kffffffffffffffk',
  'kssffffffffffffk',
  'kfsswfffffffffFk',
  'kffsswwfffffffFk',
  'kfffffyyyfffffFk',
  'kffffffyoyyffffk',
  'kfffffffyooyyffk',
  'kffffffffyooyyfk',
  'kfffffffffyoooyk',
  'kffffffffffyyooy',
  'kffffffffffffyyk',
  'kffffffffffffffk',
  'kffffffFFFFFFFFk',
  'kfffFFFFFFFFFFFk',
  'kkkkkkkkkkkkkkkk',
]);

export const iconDoubleStrike = icon('icon_double_strike', { s: '#c8d0da', w: '#ffffff', g: '#7a5a3a' }, [
  'kkkkkkkkkkkkkkkk',
  'kwfffffffffffwfk',
  'kfswffffffffwsfk',
  'kffswffffffwsffk',
  'kfffswffffwsfffk',
  'kffffswffwsffffk',
  'kfffffswwsfffffk',
  'kffffffsssffffFk',
  'kfffffswwsfffffk',
  'kffffgsffsgffffk',
  'kfffggffffggfffk',
  'kffggffffffggffk',
  'kfgffffffffffgfk',
  'kffffffffffffffk',
  'kffffffFFFFFFFFk',
  'kkkkkkkkkkkkkkkk',
]);

export const iconThrowingKnives = icon('icon_throwing_knives', { s: '#c8d0da', w: '#ffffff', g: '#7a5a3a' }, [
  'kkkkkkkkkkkkkkkk',
  'kffffffffffffffk',
  'kfffffffffffwwfk',
  'kffffffffffwssfk',
  'kgfffffffwsssffk',
  'kfgggggwssswfffk',
  'kffffffffffffffk',
  'kgggggwsssssswfk',
  'kffffffffffffffk',
  'kfgggggwssswfffk',
  'kgfffffffwsssffk',
  'kffffffffffwssfk',
  'kfffffffffffwwfk',
  'kffffffffffffffk',
  'kffffffFFFFFFFFk',
  'kkkkkkkkkkkkkkkk',
]);

export const knife: SpriteSpec = {
  name: 'knife', dir: 'assets/sprites/projectiles',
  legend: { k: '#1a1a22', s: '#c8d0da', w: '#ffffff', g: '#7a5a3a' },
  frames: [{ rows: ['..........', 'gg.kkkk...', 'ggksswwwk.', 'gg.kkkk...', '..........'], duration: 100 }],
};

// --- Mirror shard pickup: a jagged glass sliver, tinted per tier band -------------------
const shardRows = [
  '...k..',
  '..kwk.',
  '..kwlk',
  '.kwllk',
  '.kwlck',
  'kwllck',
  'kwlcck',
  '.klcck',
  '.kccck',
  '..kcck',
  '...kk.',
];
function shard(name: string, l: string, c: string): SpriteSpec {
  return {
    name, dir: 'assets/sprites/pickups',
    legend: { k: '#101018', w: '#ffffff', l, c },
    frames: [{ rows: shardRows, duration: 600 }, { rows: recolor(shardRows, { w: 'l' }), duration: 200 }],
    animations: [{ name: 'idle', from: 0, to: 1 }], autoplay: 'idle',
  };
}
// Tiers 1-5 white-blue, 6-10 yellow, 11-15 orange, 16-20 red: PoE map-tier colours.
export const shardLow = shard('shard_low', '#cfe6ff', '#7aa6d8');
export const shardMid = shard('shard_mid', '#fff0a0', '#d8b040');
export const shardHigh = shard('shard_high', '#ffc890', '#e0782a');
export const shardTop = shard('shard_top', '#ff9a9a', '#d02a3a');

// --- The Mirror in the hideout (32x40): an ornate standing mirror ---------------------
const mirrorLegend = { k: '#120c10', g: '#c8962a', G: '#ffd86a', d: '#7a5418', m: '#3a4a6a', M: '#6a8ab8', w: '#d8ecff', s: '#2a3450' };
const mirrorRows = sym([
  '.........kkkkkkk',
  '.......kkGGGGGGG',
  '......kGGggggggg',
  '.....kGggkkkkkkk',
  '....kGgkkmmmmmmm',
  '....kGgkmMMmmmmm',
  '...kGgkmMwMmmmmm',
  '...kGgkmMMmmmmms',
  '...kGgkmmmmmmmms',
  '...kGgkmmmmmmmss',
  '...kGgkmmmmmmmss',
  '...kGgkmmmmmmsss',
  '...kGgkmmmmmssss',
  '...kGgkmmmmsssss',
  '...kGgkmmmsssssm',
  '...kGgkmmssssssm',
  '...kGgkmmsssssmm',
  '...kGgkmssssssmm',
  '...kGgkmsssssmmm',
  '...kGgkmssssmmmm',
  '...kGgkmsssmmmmM',
  '...kGgkmssmmmmMM',
  '...kGgkmsmmmmmMw',
  '...kGgkmmmmmmMMw',
  '...kGgkmmmmmmmMM',
  '...kGgkmmmmmmmmm',
  '....kGgkkkkkkkkk',
  '.....kGggggggggg',
  '......kkGGGGGGGG',
  '........kkkkkkkk',
  '...........kGgkk',
  '...........kGgk.',
  '..........kGggk.',
  '........kkGgggkk',
  '.......kGGggggdd',
  '.......kkkkkkkkk',
]);
export const mirror: SpriteSpec = {
  name: 'mirror', dir: 'assets/sprites/props', legend: mirrorLegend,
  frames: [
    { rows: mirrorRows, duration: 500 },
    { rows: recolor(mirrorRows, { m: 'M', M: 'w' }), duration: 300 },
  ],
  animations: [{ name: 'idle', from: 0, to: 1 }], autoplay: 'idle',
};

export const endgame = [
  iconCleave, iconGroundSlam, iconSunder, iconDoubleStrike, iconThrowingKnives, knife,
  shardLow, shardMid, shardHigh, shardTop, mirror,
];
