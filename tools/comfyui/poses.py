"""Pose guides: a grey mannequin per animation beat, drawn as a strip of cells.

FLUX.2 Klein gets the strip as its second reference image ("image 2 shows the
poses") next to the approved character ("image 1"). The guides only carry body
and staff positions; the character's look always comes from image 1.

Angles are in degrees from straight down. Side view: positive swings forward
(to the right, where the character faces). Front/back view: positive swings
outward, away from the body. The back view is the front pose mirrored, so the
staff stays in the same hand.
"""

from __future__ import annotations

import math

from PIL import Image, ImageDraw

CELL_W, CELL_H = 384, 512
GROUND = 488
HIP_Y, SHOULDER_Y, HEAD_Y, HEAD_R = 360, 268, 190, 70
THIGH, SHIN, UPPER_ARM, FOREARM = 66, 60, 50, 46
STAFF_UP, STAFF_DOWN = 150, 56

BODY = (150, 150, 150)
FAR = (196, 196, 196)
LINE = (40, 40, 40)
STAFF = (110, 80, 50)
GLOW = (230, 60, 50)


def _side(legs, arms, staff, dy=0, burst=False):
    return {"kind": "side", "legs": legs, "arms": arms, "staff": staff, "dy": dy, "burst": burst}


def _front(lift=(0, 0), arms=((15, 0), (15, 0)), staff=0, dy=0, burst=False):
    return {"kind": "front", "lift": lift, "arms": arms, "staff": staff, "dy": dy, "burst": burst}


# legs/arms: ((near thigh|upper, near knee|elbow bend), (far ..., ...)); the near
# arm (index 0) holds the staff in the side view, the right-hand arm on screen
# holds it in the front view.
POSES = {
    ("side", "idle"): [
        _side(((4, 0), (-4, 0)), ((20, 70), (-5, 10)), 0),
        _side(((4, 0), (-4, 0)), ((20, 70), (-5, 10)), 0, dy=-4),
    ],
    ("side", "walk"): [
        _side(((28, 0), (-26, -18)), ((20, 70), (22, 15)), 4),
        _side(((-2, 0), (34, -64)), ((16, 70), (0, 10)), 0, dy=-7),
        _side(((-26, -18), (28, 0)), ((24, 70), (-24, 10)), -4),
        _side(((34, -64), (-2, 0)), ((16, 70), (0, 10)), 0, dy=-7),
    ],
    ("side", "attack"): [
        _side(((14, 0), (-22, 0)), ((-150, 20), (-30, 20)), -20),
        _side(((34, -10), (-32, 0)), ((78, 10), (-40, 20)), 65, dy=6),
        _side(((34, -10), (-32, 0)), ((90, 0), (-45, 20)), 85, dy=6, burst=True),
        _side(((12, 0), (-12, 0)), ((40, 50), (-10, 10)), 15),
    ],
    ("front", "idle"): [
        _front(arms=((12, 0), (30, -12)), staff=0),
        _front(arms=((12, 0), (30, -12)), staff=0, dy=-4),
    ],
    ("front", "walk"): [
        _front(arms=((18, 0), (26, -12)), staff=4),
        _front(lift=(0, 28), arms=((12, 0), (30, -12)), staff=0, dy=-7),
        _front(arms=((8, 0), (34, -12)), staff=-4),
        _front(lift=(28, 0), arms=((12, 0), (30, -12)), staff=0, dy=-7),
    ],
    ("front", "attack"): [
        _front(arms=((25, 0), (150, 10)), staff=-5),
        _front(arms=((25, 0), (70, 20)), staff=35, dy=6),
        _front(arms=((25, 0), (80, 10)), staff=50, dy=6, burst=True),
        _front(arms=((15, 0), (35, -20)), staff=10),
    ],
}


def _seg(origin, angle_deg, length, forward=1):
    a = math.radians(angle_deg)
    return (origin[0] + forward * math.sin(a) * length, origin[1] + math.cos(a) * length)


def _limb(draw, points, colour, width):
    draw.line(points, fill=LINE, width=width + 8, joint="curve")
    draw.line(points, fill=colour, width=width, joint="curve")
    for p in points:
        r = width // 2 + 2
        draw.ellipse((p[0] - r, p[1] - r, p[0] + r, p[1] + r), fill=colour, outline=LINE, width=3)


def _staff(draw, hand, angle_deg, forward, burst):
    a = math.radians(angle_deg)
    dx, dy = forward * math.sin(a), -math.cos(a)
    top = (hand[0] + dx * STAFF_UP, hand[1] + dy * STAFF_UP)
    bottom = (hand[0] - dx * STAFF_DOWN, hand[1] - dy * STAFF_DOWN)
    draw.line((bottom, top), fill=LINE, width=16)
    draw.line((bottom, top), fill=STAFF, width=9)
    draw.ellipse((top[0] - 16, top[1] - 16, top[0] + 16, top[1] + 16), fill=GLOW, outline=LINE, width=3)
    if burst:
        for i in range(8):
            b = i * math.pi / 4
            draw.line((top[0] + math.cos(b) * 26, top[1] + math.sin(b) * 26,
                       top[0] + math.cos(b) * 56, top[1] + math.sin(b) * 56), fill=GLOW, width=7)


def _draw_side(draw, cx, pose):
    dy = pose["dy"]
    hip, shoulder = (cx, HIP_Y + dy), (cx, SHOULDER_Y + dy)
    (near_leg, far_leg), (near_arm, far_arm) = pose["legs"], pose["arms"]

    def leg(spec, colour):
        knee = _seg(hip, spec[0], THIGH)
        foot = _seg(knee, spec[0] + spec[1], SHIN)
        _limb(draw, [hip, knee, foot], colour, 24)

    def arm(spec, colour):
        elbow = _seg(shoulder, spec[0], UPPER_ARM)
        hand = _seg(elbow, spec[0] + spec[1], FOREARM)
        _limb(draw, [shoulder, elbow, hand], colour, 18)
        return hand

    arm(far_arm, FAR)
    leg(far_leg, FAR)
    draw.rounded_rectangle((cx - 34, SHOULDER_Y + dy - 14, cx + 30, HIP_Y + dy + 14), 18, fill=BODY, outline=LINE, width=4)
    leg(near_leg, BODY)
    draw.ellipse((cx - HEAD_R, HEAD_Y + dy - HEAD_R, cx + HEAD_R, HEAD_Y + dy + HEAD_R), fill=BODY, outline=LINE, width=4)
    draw.polygon([(cx + HEAD_R - 6, HEAD_Y + dy - 14), (cx + HEAD_R + 22, HEAD_Y + dy + 4), (cx + HEAD_R - 6, HEAD_Y + dy + 22)], fill=BODY, outline=LINE)
    hand = arm(near_arm, BODY)
    _staff(draw, hand, pose["staff"], 1, pose["burst"])


def _draw_front(draw, cx, pose, back):
    dy = pose["dy"]
    side = -1 if back else 1  # screen side of the staff hand
    for i, lift in enumerate(pose["lift"]):
        hx = cx + (-24 if i == 0 else 24)
        hip = (hx, HIP_Y + dy)
        knee = (hx, HIP_Y + dy + THIGH - lift * 0.4)
        foot = (hx, min(GROUND, HIP_Y + dy + THIGH + SHIN) - lift)
        _limb(draw, [hip, knee, foot], BODY, 26)
    draw.rounded_rectangle((cx - 46, SHOULDER_Y + dy - 14, cx + 46, HIP_Y + dy + 14), 20, fill=BODY, outline=LINE, width=4)
    hand = None
    for i, spec in enumerate(pose["arms"]):
        out = -side if i == 0 else side
        shoulder = (cx + out * 48, SHOULDER_Y + dy)
        elbow = _seg(shoulder, spec[0], UPPER_ARM, out)
        h = _seg(elbow, spec[0] + spec[1], FOREARM, out)
        _limb(draw, [shoulder, elbow, h], BODY, 20)
        if i == 1:
            hand = h
    draw.ellipse((cx - HEAD_R, HEAD_Y + dy - HEAD_R, cx + HEAD_R, HEAD_Y + dy + HEAD_R), fill=BODY, outline=LINE, width=4)
    if not back:
        # Eyes say "this side faces the camera".
        for ex in (-24, 24):
            draw.ellipse((cx + ex - 7, HEAD_Y + dy - 4, cx + ex + 7, HEAD_Y + dy + 14), fill=LINE)
    _staff(draw, hand, pose["staff"], side, pose["burst"])


def guide_strip(view: str, anim: str, frames: int) -> Image.Image:
    """The pose guide for one view x animation, `frames` cells wide."""
    kind = "side" if view == "side" else "front"
    poses = POSES[(kind, anim)]
    if len(poses) != frames:
        raise ValueError(f"{kind}/{anim} has {len(poses)} guide poses, the character asks for {frames}")
    img = Image.new("RGB", (CELL_W * frames, CELL_H), (255, 255, 255))
    draw = ImageDraw.Draw(img)
    for i, pose in enumerate(poses):
        cx = CELL_W * i + CELL_W // 2
        if pose["kind"] == "side":
            _draw_side(draw, cx, pose)
        else:
            _draw_front(draw, cx, pose, back=(view == "back"))
        if i:
            draw.line((CELL_W * i, 0, CELL_W * i, CELL_H), fill=(225, 225, 225), width=2)
    return img
