"""16x16 skill icons for the skills of 2026-11-01 (auras, totem, movement, afflictions).

Same frame and grid format as tools/art/sprites/icons.ts ('k' border, 'f'/'F' background,
'.' = background). Run: python3 tools/art/python/skill_icons.py  (writes assets/sprites/icons/).
"""
import sys
from pathlib import Path
from PIL import Image

FRAME = {'k': '#0e0c14', 'f': '#2a2438', 'F': '#3a3350'}

ICONS = {
    'icon_blink': ({'c': '#7ab8ff', 'w': '#ffffff', 'b': '#3a5ac8'}, [
        '..............',
        '.........ww...',
        '........wccw..',
        '........wccw..',
        '.........ww...',
        '.......bc.....',
        '......b.......',
        '.....c........',
        '....b.........',
        '..ww..........',
        '.wccw.........',
        '.wccw.........',
        '..ww..........',
        '..............',
    ]),
    'icon_totem': ({'d': '#4a3020', 'o': '#8a5a30', 'y': '#ffcf5a', 'r': '#e8601c'}, [
        '......yy......',
        '.....yrry.....',
        '....dddddd....',
        '....doyyod....',
        '....dddddd....',
        '.....dood.....',
        '.....dood.....',
        '....dddddd....',
        '....drood.....',
        '.....dood.....',
        '.....dood.....',
        '.....dood.....',
        '...dddddddd...',
        '..............',
    ]),
    'icon_douse': ({'a': '#c89a30', 'A': '#ffd870', 'd': '#5a3a10'}, [
        '..............',
        '......aa......',
        '.....aAaa.....',
        '.....aAaa.....',
        '....aAAaaa....',
        '....aAaaaa....',
        '...aaAaaaad...',
        '...aaaaaaad...',
        '...aaaaaadd...',
        '....aaaadd....',
        '.....dddd.....',
        '..............',
        '.ddaaaaaaaadd.',
        '..dddddddddd..',
    ]),
    'icon_combust': ({'r': '#e8401c', 'o': '#ff8a2a', 'y': '#ffd23f', 'w': '#fff6d0'}, [
        '......o.......',
        '..o...o...o...',
        '...o..r..o....',
        '....r.o.r.....',
        '......y.......',
        'oor..ywy..roo.',
        '....ywwwy.....',
        '.....ywy......',
        '......y.......',
        '....r.o.r.....',
        '...o..r..o....',
        '..o...o...o...',
        '......o.......',
        '..............',
    ]),
    'icon_exsanguinate': ({'r': '#c81818', 'R': '#ff5050', 'd': '#6a0808'}, [
        '......r.......',
        '..r...d...r...',
        '...d..r..d....',
        '......r.......',
        '.....rRr......',
        'rd..rRRrr..dr.',
        '....rRrrr.....',
        '....rrrrd.....',
        '.....rrd......',
        '......d.......',
        '...d.....d....',
        '..r...d...r...',
        '......r.......',
        '..............',
    ]),
    'icon_immolation': ({'r': '#e8401c', 'o': '#ff8a2a', 'y': '#ffd23f'}, [
        '..............',
        '....o..o......',
        '...oroorro....',
        '..or......ro..',
        '..r........o..',
        '.or...yy...ro.',
        '.r...yyyy...r.',
        '.r...yyyy...r.',
        '.or...yy...ro.',
        '..r........r..',
        '..or......ro..',
        '...orroorro...',
        '.....o..o.....',
        '..............',
    ]),
    'icon_elemental_ward': ({'r': '#e8401c', 'b': '#4aa0ff', 'y': '#ffe050', 's': '#c8c0d8', 'd': '#6a6080'}, [
        '..............',
        '..dddddddddd..',
        '..dsssssssd...',
        '..dsrrsbbsd...',
        '..dsrrsbbsd...',
        '..dssssssssd..',
        '..dsyyyyyysd..',
        '..dsyyyyyysd..',
        '...dssssssd...',
        '...dssssssd...',
        '....dssssd....',
        '.....dssd.....',
        '......dd......',
        '..............',
    ]),
    'icon_elemental_storm': ({'r': '#e8401c', 'b': '#4aa0ff', 'y': '#ffe050', 'w': '#ffffff'}, [
        '..............',
        '.....rrr......',
        '....r...r.....',
        '...r.....b....',
        '..r.......b...',
        '..r..www..b...',
        '..y..www..b...',
        '..y..www..b...',
        '..y.......b...',
        '...y.....b....',
        '....y...b.....',
        '.....yyb......',
        '..............',
        '..............',
    ]),
    'icon_precision': ({'w': '#ffffff', 'g': '#e8d070', 'r': '#ff4a4a'}, [
        '......g.......',
        '......g.......',
        '....ggggg.....',
        '...g..g..g....',
        '..g...g...g...',
        '..g...r...g...',
        'gggggrrrggggg.',
        '..g...r...g...',
        '..g...g...g...',
        '...g..g..g....',
        '....ggggg.....',
        '......g.......',
        '......g.......',
        '..............',
    ]),
    'icon_bloodthirst': ({'w': '#f0e8e0', 'r': '#d01818', 'd': '#6a0808'}, [
        '..............',
        '.dddddddddddd.',
        '.dwwwwwwwwwwd.',
        '..dw.wwww.wd..',
        '..dw..ww..wd..',
        '...w......w...',
        '...r......r...',
        '...r......r...',
        '...d......d...',
        '..............',
        '......r.......',
        '.....rrr......',
        '.....rrd......',
        '......d.......',
    ]),
    'icon_titan': ({'s': '#a8a0b8', 'S': '#e0dcea', 'd': '#4a4458', 'g': '#c8a040'}, [
        '..............',
        '....dddddd....',
        '...dSSSSSSd...',
        '..dSssssssSd..',
        '..dSsgggssSd..',
        '..dSsgssgsSd..',
        '..dSsgggssSd..',
        '..dSsgssgsSd..',
        '..dSsgggssSd..',
        '..dSssssssSd..',
        '...dSssssSd...',
        '....dSSSSd....',
        '.....dddd.....',
        '..............',
    ]),
    'icon_vitality': ({'r': '#e83050', 'R': '#ff90a0', 'd': '#801830', 'g': '#5aff7a'}, [
        '..............',
        '..............',
        '..ddd...ddd...',
        '.dRRrd.drrrd..',
        '.dRrrrdrrrrd..',
        '.drrrrrrgrrd..',
        '.drrrrrggggd..',
        '..drrrrrgrd...',
        '...drrrrrd....',
        '....drrrd.....',
        '.....drd......',
        '......d.......',
        '..............',
        '..............',
    ]),
}


def hex_rgba(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4)) + (255,)


def render(legend, rows):
    img = Image.new('RGBA', (16, 16))
    pal = {**FRAME, **legend}
    for y in range(16):
        for x in range(16):
            if x in (0, 15) or y in (0, 15):
                ch = 'k'
            else:
                ch = rows[y - 1][x - 1] if x - 1 < len(rows[y - 1]) else '.'
                if ch == '.':
                    # The family's shading: lighter towards the lower right.
                    ch = 'F' if x + y > 22 else 'f'
            img.putpixel((x, y), hex_rgba(pal[ch]))
    return img


def main(out):
    out = Path(out)
    for name, (legend, rows) in ICONS.items():
        assert len(rows) == 14, name
        render(legend, rows).save(out / f'{name}.png')
        print('wrote', name)


if __name__ == '__main__':
    main(sys.argv[1] if len(sys.argv) > 1 else 'assets/sprites/icons')
