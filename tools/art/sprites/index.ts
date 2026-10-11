import type { SpriteSpec } from '../lib';
import { bosses } from './bosses';
import { enemies } from './enemies';
import { icons } from './icons';
import { pickups } from './pickups';
import { player } from './player';
import { projectiles } from './projectiles';
import { tiles } from './tiles';

export const all: SpriteSpec[] = [player, ...enemies, ...bosses, ...projectiles, ...pickups, ...icons, ...tiles];
