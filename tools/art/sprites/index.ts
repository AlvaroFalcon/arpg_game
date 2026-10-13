import type { SpriteSpec } from '../lib';
import { bosses } from './bosses';
import { enemies } from './enemies';
import { icons } from './icons';
import { pickups } from './pickups';
import { projectiles } from './projectiles';
import { props } from './props';
import { town } from './town';
import { animeCharacters, portraits } from './anime';
import { endgame } from './endgame';
import { realms } from './realms';
import { tiles } from './tiles';

export const all: SpriteSpec[] = [...animeCharacters, ...portraits, ...enemies, ...bosses, ...projectiles, ...pickups, ...icons, ...props, ...endgame, ...realms, ...tiles, ...town];
