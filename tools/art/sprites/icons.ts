import { type SpriteSpec } from '../lib';

const dir = 'assets/sprites/icons';

// 16x16 icons for skill slots and level-up cards. Every icon shares the same
// frame so cards and the skill bar read as one family.
const frame = { k: '#0e0c14', f: '#2a2438', F: '#3a3350' };

function icon(name: string, legend: Record<string, string>, rows: string[]): SpriteSpec {
  return { name, dir, legend: { ...frame, ...legend }, frames: [{ rows, duration: 100 }] };
}

export const iconFireball = icon('icon_fireball', { r: '#e8401c', o: '#ff8a2a', y: '#ffd23f', w: '#fff6d0' }, [
  'kkkkkkkkkkkkkkkk',
  'kffffffffffffffk',
  'kffffffffffooffk',
  'kffffffffoorrffk',
  'kfffffffoorrfffk',
  'kffffffoyyrffffk',
  'kfffffoyyyorfffk',
  'kffffoywwyyorffk',
  'kfffoywwwyyorffk',
  'kfffoywwyyyoffFk',
  'kfffoyyyyyoofFFk',
  'kffffooyyoofFFFk',
  'kfffffoooofFFFFk',
  'kffffffffFFFFFFk',
  'kfffffFFFFFFFFFk',
  'kkkkkkkkkkkkkkkk',
]);

export const iconFrostNova = icon('icon_frost_nova', { b: '#2a6fd6', c: '#6ec6ff', w: '#e8fbff' }, [
  'kkkkkkkkkkkkkkkk',
  'kfffffffcffffffk',
  'kffcffffcffffcfk',
  'kfffcfffcfffcffk',
  'kffffcffwffcfffk',
  'kfffffcbwbcffffk',
  'kffffffbwbfffffk',
  'kcccwwwwwwwwcccF',
  'kffffffbwbfffFFk',
  'kfffffcbwbcfFFFk',
  'kffffcffwffcFFFk',
  'kfffcfffcFFFcFFk',
  'kffcffffcFFFFcFk',
  'kffffffFcFFFFFFk',
  'kfffffFFFFFFFFFk',
  'kkkkkkkkkkkkkkkk',
]);

export const iconSpark = icon('icon_spark', { c: '#8ab4ff', w: '#ffffff', y: '#fff27a' }, [
  'kkkkkkkkkkkkkkkk',
  'kfffffffffwfffFk',
  'kffffffffwyffffk',
  'kfffffffwyfffffk',
  'kffffffwyyfffffk',
  'kfffffwyyfffcffk',
  'kffffwyyyyyyffFk',
  'kfffwwwwwyywfFFk',
  'kffffffwyywfFFFk',
  'kffcffwyywfFFFFk',
  'kfffffwyyfFFFFFk',
  'kffffwyyfFFcFFFk',
  'kfffwyyfFFFFFFFk',
  'kffwyfFFFFFFFFFk',
  'kfwFFFFFFFFFFFFk',
  'kkkkkkkkkkkkkkkk',
]);

export const iconBlades = icon('icon_blades', { s: '#b8c0cc', w: '#f2f6ff', g: '#8a6a3a' }, [
  'kkkkkkkkkkkkkkkk',
  'kfffffwfffffffFk',
  'kfffffwsfffffffk',
  'kfffffwsffffwwfk',
  'kffffffsgffwssfk',
  'kfffffffggwsfffk',
  'kffwsffffgsffffk',
  'kfwssgfffffffffk',
  'kfffffgffffgsswk',
  'kffffssgffgffwFk',
  'kfffwsffffffFFFk',
  'kffwsffffgsFFFFk',
  'kffwfffffsgwFFFk',
  'kfffffffsFFwwFFk',
  'kffffffwFFFFFFFk',
  'kkkkkkkkkkkkkkkk',
]);

export const iconMeteor = icon('icon_meteor', { r: '#a8301a', o: '#ff8a2a', y: '#ffd23f', s: '#5a4a44', d: '#3a302c' }, [
  'kkkkkkkkkkkkkkkk',
  'kyfffffffffffffk',
  'kfoyfffffffffffk',
  'kffooyfffffffffk',
  'kfffoooyffffffFk',
  'kffffroooyfffffk',
  'kfffffrrssssfffk',
  'kffffffsssddsffk',
  'kffffffssddddsfk',
  'kfffffsssddddsFk',
  'kffffffssddddsFk',
  'kfffffffssddsFFk',
  'kffffffffssssFFk',
  'kfffffffffFFFFFk',
  'kfffffFFFFFFFFFk',
  'kkkkkkkkkkkkkkkk',
]);

export const iconSupport = icon('icon_support', { g: '#3fd66b', G: '#a8ffb8', d: '#1e7a3a' }, [
  'kkkkkkkkkkkkkkkk',
  'kffffffffffffffk',
  'kffffffkkffffffk',
  'kfffffkGGkfffffk',
  'kffffkGGggkffffk',
  'kfffkGGgggdkfffk',
  'kffkGggggggdkffk',
  'kffkgggggggdkffk',
  'kffkggggggddkffk',
  'kfffkggggddkfffk',
  'kffffkgggdkffffk',
  'kfffffkgdkfffffk',
  'kffffffkkffffffk',
  'kffffffffffffffk',
  'kffffffffffffffk',
  'kkkkkkkkkkkkkkkk',
]);

export const iconPassive = icon('icon_passive', { b: '#4aa0ff', B: '#b8dcff', d: '#1e4aa0' }, [
  'kkkkkkkkkkkkkkkk',
  'kffffffffffffffk',
  'kffffffffffffffk',
  'kffffkkkkkkffffk',
  'kfffkBBBbbbkfffk',
  'kffkBBbbbbbdkffk',
  'kffkBbbbbbbdkffk',
  'kffkbbbbbbbdkffk',
  'kffkbbbbbbbdkffk',
  'kffkbbbbbbddkffk',
  'kfffkbbbbddkfffk',
  'kffffkkkkkkffffk',
  'kffffffffffffffk',
  'kffffffffffffffk',
  'kffffffffffffffk',
  'kkkkkkkkkkkkkkkk',
]);

export const iconKeystone = icon('icon_keystone', { g: '#e8b84a', G: '#fff0a0', d: '#9a6a1a', r: '#ff4a4a' }, [
  'kkkkkkkkkkkkkkkk',
  'kfffffffkffffffk',
  'kffffffkGkfffffk',
  'kffffffkGgkffffk',
  'kkkkkkkGggkkkkkk',
  'kkGGGGgggggggdkk',
  'kfkGgggrrgggdkfk',
  'kffkgggrrggdkffk',
  'kfffkgggggdkfffk',
  'kffkggggggggkffk',
  'kffkgggkkgggdkfk',
  'kfkggdkffkdggdkk',
  'kfkgdkffffkddkfk',
  'kkddkffffffkkkfk',
  'kkkkffffffffffkk',
  'kkkkkkkkkkkkkkkk',
]);

export const iconDodge = icon('icon_dodge', { c: '#7af0ff', w: '#ffffff' }, [
  'kkkkkkkkkkkkkkkk',
  'kffffffffffffffk',
  'kffffffffffcwffk',
  'kfffffffffcwwwfk',
  'kffcccccccwwwwfk',
  'kfffffffffcwwwfk',
  'kffffffffffcwffk',
  'kffffffffffffffk',
  'kffffffffffcwffk',
  'kfffcccccccwwwfk',
  'kffffffffffcwffk',
  'kffffffffffffffk',
  'kfcccccccffcwffk',
  'kffffffffccwwwfk',
  'kffffffffffcwffk',
  'kkkkkkkkkkkkkkkk',
]);

export const icons = [
  iconFireball, iconFrostNova, iconSpark, iconBlades, iconMeteor,
  iconSupport, iconPassive, iconKeystone, iconDodge,
];
