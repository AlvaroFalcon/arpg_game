#!/usr/bin/env python3
"""Character spritesheets from a local ComfyUI (FLUX.2 Klein), approved step by step.

  concept  -> candidates of the front view            -> approve <char> concept <n>
  views    -> back and side views from the concept    -> approve <char> back|side <n>
  anims    -> idle/walk/attack strips per view        -> approve <char> walk_side <n> ...
  pack     -> Godot spritesheet (.png + SpriteFrames .tres + .tscn) from the approved strips

Every stage writes numbered candidates and a contact sheet to out/<char>/; nothing
moves on until a candidate is approved. Approved images live in
characters/<char>/approved/ and are committed: they are the sources of the sheet.

Only needs Python 3.9+ and Pillow (`pip install pillow`); ComfyUI must be running.
See README.md.
"""

from __future__ import annotations

import argparse
import io
import json
import random
import shutil
import statistics
import sys
import time
import urllib.parse
import urllib.request
import uuid
from collections import deque
from pathlib import Path

from PIL import Image, ImageDraw

import poses

ROOT = Path(__file__).resolve().parent
CHARS = ROOT / "characters"
OUT = ROOT / "out"
WORKFLOWS = ROOT / "workflows"

VIEW_ORDER = ("front", "back", "side")


# ---------------------------------------------------------------- config / paths

def load_config(path: Path | None) -> dict:
    return json.loads((path or ROOT / "config.json").read_text())


def load_char(name: str) -> dict:
    path = CHARS / f"{name}.json"
    if not path.exists():
        sys.exit(f"No character spec {path}")
    return json.loads(path.read_text())


def approved_dir(char: dict) -> Path:
    return CHARS / char["name"] / "approved"


def approved(char: dict, key: str) -> Path | None:
    path = approved_dir(char) / f"{key}.png"
    return path if path.exists() else None


def view_key(view: str) -> str:
    """The approved image that stands for a view: the concept is the front view."""
    return "concept" if view == "front" else view


def anim_keys(char: dict) -> list[tuple[str, str, str]]:
    """(key, view, anim) for every strip the sheet needs, in sheet order."""
    return [(f"{anim}_{view}", view, anim) for view in VIEW_ORDER for anim in char["animations"]]


# ---------------------------------------------------------------- ComfyUI graphs

def _model_nodes(cfg: dict, g: dict) -> tuple[list, list, list]:
    m = cfg["model"]
    g["unet"] = {"class_type": "UNETLoader", "inputs": {"unet_name": m["unet"], "weight_dtype": "default"}}
    g["clip"] = {"class_type": "CLIPLoader", "inputs": {"clip_name": m["text_encoder"], "type": "flux2", "device": "default"}}
    g["vae"] = {"class_type": "VAELoader", "inputs": {"vae_name": m["vae"]}}
    model = ["unet", 0]
    for i, lora in enumerate(cfg.get("loras", [])):
        g[f"lora{i}"] = {"class_type": "LoraLoaderModelOnly",
                         "inputs": {"model": model, "lora_name": lora["name"], "strength_model": lora.get("strength", 1.0)}}
        model = [f"lora{i}", 0]
    return model, ["clip", 0], ["vae", 0]


def build_graph(cfg: dict, prompt: str, refs: list[str], width: int, height: int, seed: int, prefix: str) -> dict:
    """API-format graph: FLUX.2 Klein text-to-image, or reference edit when `refs` is
    not empty (the official Klein edit template: each reference is VAE-encoded and
    chained into the conditioning with ReferenceLatent; "image 1" is the first)."""
    g: dict = {}
    model, clip, vae = _model_nodes(cfg, g)
    m = cfg["model"]
    g["pos"] = {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": clip}}
    if m["cfg"] > 1.0:
        g["neg"] = {"class_type": "CLIPTextEncode", "inputs": {"text": m.get("negative", ""), "clip": clip}}
    else:  # distilled model: cfg 1, the negative is ignored
        g["neg"] = {"class_type": "ConditioningZeroOut", "inputs": {"conditioning": ["pos", 0]}}
    pos, neg = ["pos", 0], ["neg", 0]
    for i, name in enumerate(refs):
        g[f"ref{i}_load"] = {"class_type": "LoadImage", "inputs": {"image": name}}
        g[f"ref{i}_scale"] = {"class_type": "ImageScaleToTotalPixels",
                              "inputs": {"image": [f"ref{i}_load", 0], "upscale_method": "nearest-exact",
                                         "megapixels": cfg.get("reference_megapixels", 1.0), "resolution_steps": 1}}
        g[f"ref{i}_enc"] = {"class_type": "VAEEncode", "inputs": {"pixels": [f"ref{i}_scale", 0], "vae": vae}}
        g[f"ref{i}_pos"] = {"class_type": "ReferenceLatent", "inputs": {"conditioning": pos, "latent": [f"ref{i}_enc", 0]}}
        g[f"ref{i}_neg"] = {"class_type": "ReferenceLatent", "inputs": {"conditioning": neg, "latent": [f"ref{i}_enc", 0]}}
        pos, neg = [f"ref{i}_pos", 0], [f"ref{i}_neg", 0]
    g["latent"] = {"class_type": "EmptyFlux2LatentImage", "inputs": {"width": width, "height": height, "batch_size": 1}}
    g["sigmas"] = {"class_type": "Flux2Scheduler", "inputs": {"steps": m["steps"], "width": width, "height": height}}
    g["sampler"] = {"class_type": "KSamplerSelect", "inputs": {"sampler_name": m.get("sampler", "euler")}}
    g["noise"] = {"class_type": "RandomNoise", "inputs": {"noise_seed": seed}}
    g["guider"] = {"class_type": "CFGGuider", "inputs": {"model": model, "positive": pos, "negative": neg, "cfg": m["cfg"]}}
    g["sample"] = {"class_type": "SamplerCustomAdvanced",
                   "inputs": {"noise": ["noise", 0], "guider": ["guider", 0], "sampler": ["sampler", 0],
                              "sigmas": ["sigmas", 0], "latent_image": ["latent", 0]}}
    g["decode"] = {"class_type": "VAEDecode", "inputs": {"samples": ["sample", 0], "vae": vae}}
    g["save"] = {"class_type": "SaveImage", "inputs": {"images": ["decode", 0], "filename_prefix": prefix}}
    return g


class Comfy:
    def __init__(self, server: str):
        self.base = f"http://{server}"
        self.client_id = uuid.uuid4().hex

    def _json(self, path: str, payload: dict | None = None) -> dict:
        data = json.dumps(payload).encode() if payload is not None else None
        req = urllib.request.Request(self.base + path, data=data, headers={"Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req) as r:
                return json.loads(r.read())
        except urllib.error.HTTPError as e:
            sys.exit(f"ComfyUI {path}: HTTP {e.code}\n{e.read().decode(errors='replace')}")
        except urllib.error.URLError as e:
            sys.exit(f"Cannot reach ComfyUI at {self.base} ({e.reason}). Is it running? "
                     "The desktop app uses port 8000: pass --server 127.0.0.1:8000.")

    def upload(self, image: Image.Image, name: str) -> str:
        buf = io.BytesIO()
        image.save(buf, "PNG")
        boundary = uuid.uuid4().hex
        body = b"".join([
            f"--{boundary}\r\nContent-Disposition: form-data; name=\"overwrite\"\r\n\r\ntrue\r\n".encode(),
            f"--{boundary}\r\nContent-Disposition: form-data; name=\"subfolder\"\r\n\r\nspritegen\r\n".encode(),
            f"--{boundary}\r\nContent-Disposition: form-data; name=\"image\"; filename=\"{name}\"\r\n"
            "Content-Type: image/png\r\n\r\n".encode(), buf.getvalue(), f"\r\n--{boundary}--\r\n".encode()])
        req = urllib.request.Request(self.base + "/upload/image", data=body,
                                     headers={"Content-Type": f"multipart/form-data; boundary={boundary}"})
        with urllib.request.urlopen(req) as r:
            res = json.loads(r.read())
        return f"{res['subfolder']}/{res['name']}" if res.get("subfolder") else res["name"]

    def queue(self, graph: dict) -> str:
        res = self._json("/prompt", {"prompt": graph, "client_id": self.client_id})
        if res.get("node_errors"):
            sys.exit(f"ComfyUI rejected the graph: {json.dumps(res['node_errors'], indent=1)}")
        return res["prompt_id"]

    def wait_image(self, prompt_id: str) -> Image.Image:
        while True:
            hist = self._json(f"/history/{prompt_id}").get(prompt_id)
            if hist:
                status = hist.get("status", {})
                if status.get("status_str") == "error":
                    sys.exit(f"ComfyUI failed: {json.dumps(status.get('messages', []))[:2000]}")
                for out in hist.get("outputs", {}).values():
                    for img in out.get("images", []):
                        q = urllib.parse.urlencode({k: img[k] for k in ("filename", "subfolder", "type")})
                        with urllib.request.urlopen(f"{self.base}/view?{q}") as r:
                            return Image.open(io.BytesIO(r.read())).convert("RGB")
                if status.get("completed"):
                    sys.exit("ComfyUI finished without an image.")
            time.sleep(1.0)


def generate(cfg: dict, args, jobs: list[dict]) -> None:
    """jobs: {prompt, refs: [PIL images], size: (w, h), out: dir, key}. Queues
    `args.n` seeds per job, saves <key>_<i>_s<seed>.png and a contact sheet per key."""
    comfy = Comfy(args.server or cfg["server"])
    pending = []
    for job in jobs:
        refs = [comfy.upload(img, f"{job['key']}_ref{i}.png") for i, img in enumerate(job["refs"])]
        job["out"].mkdir(parents=True, exist_ok=True)
        for old in job["out"].glob(f"{job['key']}_*"):
            old.unlink()
        (job["out"] / f"{job['key']}_prompt.txt").write_text(job["prompt"] + "\n")
        for i in range(args.n):
            seed = args.seed + i if args.seed is not None else random.randrange(2**48)
            graph = build_graph(cfg, job["prompt"], refs, *job["size"], seed, f"spritegen/{job['key']}")
            pending.append((job, i + 1, seed, comfy.queue(graph)))
    print(f"Queued {len(pending)} image(s) in ComfyUI.")
    done: dict[str, list[Path]] = {}
    for job, i, seed, pid in pending:
        path = job["out"] / f"{job['key']}_{i}_s{seed}.png"
        comfy.wait_image(pid).save(path)
        done.setdefault(job["key"], []).append(path)
        print(f"  {path.relative_to(ROOT)}")
    for job in jobs:
        sheet = contact_sheet(done[job["key"]])
        sheet.save(job["out"] / f"{job['key']}_contact.png")
        if job.get("frames"):
            for path in done[job["key"]]:
                strip_gif(Image.open(path).convert("RGB"), job["frames"], job["char"], path.with_suffix(".gif"))
    print("Review the *_contact.png files, then: spritegen.py approve <char> <key> <number>")


def contact_sheet(paths: list[Path]) -> Image.Image:
    imgs = [Image.open(p).convert("RGB") for p in paths]
    scale = 360 / max(i.height for i in imgs)
    thumbs = [i.resize((max(1, round(i.width * scale)), 360), Image.LANCZOS) for i in imgs]
    sheet = Image.new("RGB", (sum(t.width for t in thumbs) + 12 * (len(thumbs) + 1), 400), (40, 40, 48))
    draw, x = ImageDraw.Draw(sheet), 12
    for n, t in enumerate(thumbs, 1):
        sheet.paste(t, (x, 30))
        draw.text((x + 4, 8), f"#{n}", fill=(255, 220, 120))
        x += t.width + 12
    return sheet


# ---------------------------------------------------------------- prompts

def concept_prompt(char: dict, from_reference: bool) -> str:
    what = (f"{char['description']}. Front view, facing the camera, standing idle and holding the staff "
            "upright, the whole body visible from the tip of the hat to the boots.")
    if from_reference:
        return (f"Redraw the character from image 1 as a clean game sprite, keeping its design and colours: "
                f"{what} {char['style']}.")
    return f"{char['style']}. {what}"


def view_prompt(char: dict, view: str) -> str:
    label = char["views"][view]["label"]
    return (f"Show the exact same character from image 1 with the same design, colours, proportions, size and "
            f"pixel art style, now in {label}, standing idle and holding the staff upright, the whole body visible. "
            f"{char['style']}.")


def anim_prompt(char: dict, view: str, anim: str, guide: bool) -> str:
    spec = char["animations"][anim]
    n = spec["frames"]
    label = char["views"][view]["label"]
    beats = " ".join(f"Frame {i}: {b}." for i, b in enumerate(spec["beats"], 1))
    text = (f"Create a horizontal sprite sheet with {n} animation frames side by side in {n} equal columns, "
            f"read left to right, of the exact same character from image 1 with the same design, colours, "
            f"proportions, size and pixel art style, in {label}. Every frame shows the whole body at the same "
            f"scale with the feet on the same baseline. ")
    if guide:
        text += ("Copy the body, arm, leg and staff pose of each grey mannequin in image 2, column by column; "
                 "image 2 is only a pose guide, never draw the mannequin. ")
    return text + beats + f" {char['style']}."


# ---------------------------------------------------------------- stages

def cmd_concept(cfg, args):
    char = load_char(args.char)
    refs = [] if args.from_text else [Image.open(CHARS / char["reference"]).convert("RGB")]
    generate(cfg, args, [{"key": "concept", "prompt": concept_prompt(char, not args.from_text), "refs": refs,
                          "size": tuple(cfg["concept_size"]), "out": OUT / char["name"] / "concept"}])


def cmd_views(cfg, args):
    char = load_char(args.char)
    front = approved(char, "concept")
    if not front:
        sys.exit("Approve a concept first: spritegen.py approve <char> concept <n>")
    views = args.only or [v for v in VIEW_ORDER if v != "front"]
    ref = Image.open(front).convert("RGB")
    generate(cfg, args, [{"key": v, "prompt": view_prompt(char, v), "refs": [ref],
                          "size": tuple(cfg["concept_size"]), "out": OUT / char["name"] / "views"} for v in views])


def cmd_anims(cfg, args):
    char = load_char(args.char)
    cw, ch = cfg["anim_cell"]
    jobs = []
    for key, view, anim in anim_keys(char):
        if args.only and key not in args.only and view not in args.only and anim not in args.only:
            continue
        src = approved(char, view_key(view))
        if not src:
            print(f"skip {key}: approve the {view_key(view)} view first")
            continue
        frames = char["animations"][anim]["frames"]
        refs = [Image.open(src).convert("RGB")]
        if not args.no_guide:
            guide = poses.guide_strip(view, anim, frames).resize((cw * frames, ch), Image.LANCZOS)
            refs.append(guide)
        jobs.append({"key": key, "prompt": anim_prompt(char, view, anim, not args.no_guide), "refs": refs,
                     "size": (cw * frames, ch), "out": OUT / char["name"] / "anims", "frames": frames, "char": char})
    if not jobs:
        sys.exit("Nothing to generate.")
    generate(cfg, args, jobs)


def cmd_guides(cfg, args):
    char = load_char(args.char)
    out = OUT / char["name"] / "guides"
    out.mkdir(parents=True, exist_ok=True)
    for key, view, anim in anim_keys(char):
        poses.guide_strip(view, anim, char["animations"][anim]["frames"]).save(out / f"{key}.png")
    print(f"Pose guides in {out.relative_to(ROOT)}/")


def cmd_approve(cfg, args):
    char = load_char(args.char)
    stage = "concept" if args.key == "concept" else "views" if args.key in char["views"] else "anims"
    matches = sorted((OUT / char["name"] / stage).glob(f"{args.key}_{args.number}_s*.png"))
    if not matches:
        sys.exit(f"No candidate #{args.number} for {args.key} in out/{char['name']}/{stage}/")
    dest = approved_dir(char)
    dest.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(matches[0], dest / f"{args.key}.png")
    print(f"Approved {matches[0].name} as {args.key} -> {(dest / (args.key + '.png')).relative_to(ROOT)}")
    cmd_status(cfg, args)


def cmd_status(cfg, args):
    char = load_char(args.char)
    keys = ["concept"] + [v for v in VIEW_ORDER if v != "front"] + [k for k, _, _ in anim_keys(char)]
    missing = [k for k in keys if not approved(char, k)]
    for k in keys:
        print(f"  [{'x' if k not in missing else ' '}] {k}")
    nxt = ("concept" if "concept" in missing else "views" if {"back", "side"} & set(missing)
           else "anims" if missing else "pack")
    print(f"Next: spritegen.py {nxt} {char['name']}")


# ---------------------------------------------------------------- packing

def remove_background(img: Image.Image, tolerance: int = 48, min_hole: int = 400) -> Image.Image:
    """RGBA with the flat background cleared: everything near the border colour that
    touches the border, plus enclosed pockets of it (between an arm and the body)
    bigger than `min_hole` pixels, so small white highlights survive."""
    rgb = img.convert("RGB")
    w, h = rgb.size
    px = rgb.load()
    border = [px[x, y] for x in range(0, w, 4) for y in (0, h - 1)] + [px[x, y] for y in range(0, h, 4) for x in (0, w - 1)]
    bg = tuple(statistics.median(c[i] for c in border) for i in range(3))
    tol2 = tolerance * tolerance

    def is_bg(p):
        return (p[0] - bg[0]) ** 2 + (p[1] - bg[1]) ** 2 + (p[2] - bg[2]) ** 2 <= tol2

    mask = bytearray(w * h)  # 1 = background
    seen = bytearray(w * h)
    for sy in range(h):
        for sx in range(w):
            if seen[sy * w + sx] or not is_bg(px[sx, sy]):
                continue
            comp, touches, queue = [], False, deque([(sx, sy)])
            seen[sy * w + sx] = 1
            while queue:
                x, y = queue.popleft()
                comp.append(y * w + x)
                if x in (0, w - 1) or y in (0, h - 1):
                    touches = True
                for nx, ny in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
                    if 0 <= nx < w and 0 <= ny < h and not seen[ny * w + nx] and is_bg(px[nx, ny]):
                        seen[ny * w + nx] = 1
                        queue.append((nx, ny))
            if touches or len(comp) >= min_hole:
                for i in comp:
                    mask[i] = 1
    alpha = Image.frombytes("L", (w, h), bytes(0 if m else 255 for m in mask))
    out = rgb.convert("RGBA")
    out.putalpha(alpha)
    return out


def split_strip(strip: Image.Image, frames: int) -> list[Image.Image]:
    cw = strip.width // frames
    return [strip.crop((i * cw, 0, (i + 1) * cw, strip.height)) for i in range(frames)]


def fit_frames(cells: list[Image.Image], char: dict) -> list[Image.Image]:
    """One animation's cells (RGBA, background cleared) -> frame-sized images at one
    shared scale, feet on the same baseline, centred on the feet."""
    fr = char["frame"]
    fw, fh = fr["width"], fr["height"]
    target_h = fh - fr.get("foot_margin", 2) - fr.get("head_margin", 2)
    boxes = [c.getchannel("A").getbbox() or (0, 0, c.width, c.height) for c in cells]
    # The shortest frame is the plain standing body (raised staffs and jumps only add
    # height), so it sets the scale for the whole animation.
    scale = target_h / min(b[3] - b[1] for b in boxes)
    out = []
    for cell, box in zip(cells, boxes):
        crop = cell.crop(box)
        small = crop.resize((max(1, round(crop.width * scale)), max(1, round(crop.height * scale))), Image.BOX)
        # Feet anchor: centre of the opaque pixels in the bottom quarter.
        a = small.getchannel("A").load()
        sw, sh = small.size
        xs = [x for y in range(sh * 3 // 4, sh) for x in range(sw) if a[x, y] > 127]
        foot_x = sum(xs) / len(xs) if xs else sw / 2
        frame = Image.new("RGBA", (fw, fh), (0, 0, 0, 0))
        frame.paste(small, (round(fw / 2 - foot_x), fh - fr.get("foot_margin", 2) - sh), small)
        out.append(frame)
    return out


def palette_from(img: Image.Image, colours: int) -> Image.Image:
    rgba = img.convert("RGBA")
    solid = [p[:3] for p in rgba.getdata() if p[3] > 127]
    probe = Image.new("RGB", (len(solid), 1))
    probe.putdata(solid)
    return probe.quantize(colours, method=Image.Quantize.MEDIANCUT)


def snap(frame: Image.Image, palette: Image.Image | None) -> Image.Image:
    """Hard alpha and, with a palette, every colour snapped to it (no dithering)."""
    alpha = frame.getchannel("A").point(lambda v: 255 if v > 127 else 0)
    rgb = frame.convert("RGB")
    if palette is not None:
        rgb = rgb.quantize(palette=palette, dither=Image.Dither.NONE).convert("RGB")
    out = rgb.convert("RGBA")
    out.putalpha(alpha)
    return out


def strip_gif(strip: Image.Image, frames: int, char: dict, path: Path, palette: Image.Image | None = None) -> None:
    fitted = [snap(f, palette) for f in fit_frames([remove_background(c) for c in split_strip(strip, frames)], char)]
    save_gif(fitted, path, 160)


def save_gif(frames: list[Image.Image], path: Path, duration_ms: int) -> None:
    """4x preview animation on a dark backdrop."""
    backed = []
    for f in frames:
        big = f.resize((f.width * 4, f.height * 4), Image.NEAREST)
        b = Image.new("RGBA", big.size, (64, 64, 76, 255))
        b.alpha_composite(big)
        backed.append(b.convert("RGB"))
    backed[0].save(path, save_all=True, append_images=backed[1:], duration=duration_ms, loop=0)


def cmd_pack(cfg, args):
    char = load_char(args.char)
    fr = char["frame"]
    missing = [k for k, _, _ in anim_keys(char) if not approved(char, k)]
    if missing and not args.partial:
        sys.exit(f"Not approved yet: {', '.join(missing)} (use --partial to pack what there is)")
    concept = approved(char, "concept")
    palette = None
    if concept and not args.no_palette:
        hero = fit_frames([remove_background(Image.open(concept))], char)[0]
        palette = palette_from(hero, fr.get("max_colors", 32))

    frames, anims = [], []
    for key, view, anim in anim_keys(char):
        src = approved(char, key)
        if not src:
            continue
        spec = char["animations"][anim]
        cells = [remove_background(c) for c in split_strip(Image.open(src).convert("RGB"), spec["frames"])]
        fitted = [snap(f, palette) for f in fit_frames(cells, char)]
        anims.append((anim + char["views"][view]["suffix"], spec, list(range(len(frames), len(frames) + len(fitted)))))
        frames.extend(fitted)

    out = Path(args.out) if args.out else OUT / char["name"] / "sheet"
    out.mkdir(parents=True, exist_ok=True)
    name = char.get("sprite", char["name"])
    sheet = Image.new("RGBA", (fr["width"] * len(frames), fr["height"]), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        sheet.paste(f, (i * fr["width"], 0))
    sheet.save(out / f"{name}.png")
    res = args.res_dir.rstrip("/")
    write_sprite_frames(out / f"{name}.tres", f"{res}/{name}.png", fr, len(frames), anims)
    (out / f"{name}.tscn").write_text(
        f'[gd_scene load_steps=2 format=3]\n\n[ext_resource type="SpriteFrames" path="{res}/{name}.tres" id="1_frames"]\n\n'
        f'[node name="{name.capitalize()}" type="AnimatedSprite2D"]\ntexture_filter = 1\n'
        f'sprite_frames = ExtResource("1_frames")\nanimation = &"idle"\nautoplay = "idle"\n')
    preview = sheet.resize((sheet.width * 3, sheet.height * 3), Image.NEAREST)
    bg = Image.new("RGBA", preview.size, (64, 64, 76, 255))
    bg.alpha_composite(preview)
    bg.convert("RGB").save(out / f"{name}_preview.png")
    for anim_name, spec, idx in anims:
        save_gif([frames[i] for i in idx], out / f"{name}_{anim_name}.gif", round(1000 / spec["speed"]))
    print(f"{len(frames)} frames of {fr['width']}x{fr['height']} -> {out}/{name}.png, .tres, .tscn, previews")


def write_sprite_frames(path: Path, texture: str, fr: dict, count: int, anims: list) -> None:
    """Same layout as the EasyPixel exporter: one AtlasTexture per frame."""
    lines = [f'[gd_resource type="SpriteFrames" load_steps={count + 2} format=3]', "",
             f'[ext_resource type="Texture2D" path="{texture}" id="1_sheet"]', ""]
    for i in range(count):
        lines += [f'[sub_resource type="AtlasTexture" id="AtlasTexture_f{i}"]', 'atlas = ExtResource("1_sheet")',
                  f"region = Rect2({i * fr['width']}, 0, {fr['width']}, {fr['height']})", ""]
    blocks = []
    for name, spec, idx in anims:
        fr_list = ", ".join('{\n"duration": 1.0,\n"texture": SubResource("AtlasTexture_f%d")\n}' % i for i in idx)
        blocks.append('{\n"frames": [%s],\n"loop": %s,\n"name": &"%s",\n"speed": %s\n}'
                      % (fr_list, "true" if spec["loop"] else "false", name, spec["speed"]))
    lines += ["[resource]", "animations = [" + ", ".join(blocks) + "]", ""]
    path.write_text("\n".join(lines))


# ---------------------------------------------------------------- workflows for the UI

def cmd_export_workflows(cfg, args):
    """Writes the graphs this script queues as API-format JSON, to open in ComfyUI
    (drag the file onto the canvas) and tweak by hand. LoadImage nodes point at
    placeholder names: pick your images in them."""
    WORKFLOWS.mkdir(exist_ok=True)
    char = load_char(args.char)
    cw, ch = cfg["anim_cell"]
    graphs = {
        "1_concept_from_reference": build_graph(cfg, concept_prompt(char, True), ["reference.png"], *cfg["concept_size"], 1, "spritegen/concept"),
        "1_concept_from_text": build_graph(cfg, concept_prompt(char, False), [], *cfg["concept_size"], 1, "spritegen/concept"),
        "2_view_side": build_graph(cfg, view_prompt(char, "side"), ["approved_concept.png"], *cfg["concept_size"], 1, "spritegen/side"),
        "3_anim_walk_side": build_graph(cfg, anim_prompt(char, "side", "walk", True), ["approved_side.png", "guide_walk_side.png"],
                                        cw * 4, ch, 1, "spritegen/walk_side"),
    }
    for name, g in graphs.items():
        (WORKFLOWS / f"{name}.api.json").write_text(json.dumps(g, indent=2) + "\n")
    print(f"Wrote {len(graphs)} workflows to {WORKFLOWS.relative_to(ROOT)}/")


# ---------------------------------------------------------------- CLI

def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--config", type=Path)
    p.add_argument("--server", help="ComfyUI host:port (default from config.json)")
    sub = p.add_subparsers(dest="cmd", required=True)

    def gen(name, fn, help_):
        s = sub.add_parser(name, help=help_)
        s.add_argument("char")
        s.add_argument("-n", type=int, default=4, help="candidates per image (default 4)")
        s.add_argument("--seed", type=int, help="first seed (default random)")
        s.set_defaults(fn=fn)
        return s

    gen("concept", cmd_concept, "front-view candidates").add_argument(
        "--from-text", action="store_true", help="text only, without the reference image")
    gen("views", cmd_views, "back/side candidates from the approved concept").add_argument(
        "--only", nargs="+", choices=["back", "side"])
    s = gen("anims", cmd_anims, "animation strips from the approved views")
    s.add_argument("--only", nargs="+", help="keys (walk_side), views (side) or animations (walk)")
    s.add_argument("--no-guide", action="store_true", help="no mannequin pose guide, prompt only")

    s = sub.add_parser("approve", help="approve candidate <number> of <key>")
    s.add_argument("char"); s.add_argument("key"); s.add_argument("number", type=int)
    s.set_defaults(fn=cmd_approve)
    s = sub.add_parser("status", help="what is approved and what comes next")
    s.add_argument("char"); s.set_defaults(fn=cmd_status)
    s = sub.add_parser("guides", help="write the pose guide strips to out/<char>/guides")
    s.add_argument("char"); s.set_defaults(fn=cmd_guides)
    s = sub.add_parser("pack", help="build the Godot spritesheet from the approved strips")
    s.add_argument("char")
    s.add_argument("--out", help="output folder (default out/<char>/sheet)")
    s.add_argument("--res-dir", default="res://assets/sprites/player", help="res:// folder the .tres/.tscn point at")
    s.add_argument("--partial", action="store_true", help="pack even if some strips are not approved")
    s.add_argument("--no-palette", action="store_true", help="keep the generated colours (no palette lock)")
    s.set_defaults(fn=cmd_pack)
    s = sub.add_parser("export-workflows", help="write API-format workflow JSONs for the ComfyUI canvas")
    s.add_argument("char"); s.set_defaults(fn=cmd_export_workflows)

    args = p.parse_args(argv)
    args.fn(load_config(args.config), args)


if __name__ == "__main__":
    main()
