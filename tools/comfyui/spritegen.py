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


SHEET_DIRECTIONS = ("facing down", "facing left", "facing right", "back view facing up")
SHEET_ROWS = ("first", "second", "third", "fourth")


def sheet_prompt(char: dict) -> str:
    """The svntax 32x48 combat LoRA's own prompt (model card): a 6x4 grid, one row
    per direction, 3 walk + 2 arm-swing attack + 1 hurt frame per row."""
    rows = ", ".join(f"{SHEET_ROWS[i]} row is 3 walking frames {d}, 2 arm swing attack frames {d} and 1 hurt frame {d}"
                     for i, d in enumerate(SHEET_DIRECTIONS))
    return ("Create a pixel art spritesheet of the character in the image. Keep the appearance the same. "
            f"The spritesheet is a 6 by 4 grid of four rows and six columns of frames - {rows}.")


def square_reference(img: Image.Image, size: int) -> Image.Image:
    """The reference fitted (whole body, aspect kept) on a white size x size canvas:
    the LoRA was trained on 768x768 images."""
    img = img.convert("RGB")
    scale = size / max(img.size)
    small = img.resize((round(img.width * scale), round(img.height * scale)), Image.LANCZOS)
    canvas = Image.new("RGB", (size, size), (255, 255, 255))
    canvas.paste(small, ((size - small.width) // 2, (size - small.height) // 2))
    return canvas


def build_qwen_sheet_graph(cfg: dict, prompt: str, ref: str, seed: int, prefix: str) -> dict:
    """API-format graph of ComfyUI's Qwen Image Edit 2511 template (GGUF unet, no
    Lightning LoRA) with the spritesheet LoRA; the reference is also the latent, so
    the output has its size."""
    q = cfg["qwen_sheet"]
    g: dict = {
        "unet": {"class_type": "UnetLoaderGGUF", "inputs": {"unet_name": q["unet_gguf"]}},
        "lora": {"class_type": "LoraLoaderModelOnly",
                 "inputs": {"model": ["unet", 0], "lora_name": q["lora"]["name"], "strength_model": q["lora"].get("strength", 1.0)}},
        "shift": {"class_type": "ModelSamplingAuraFlow", "inputs": {"model": ["lora", 0], "shift": q.get("shift", 3.1)}},
        "norm": {"class_type": "CFGNorm", "inputs": {"model": ["shift", 0], "strength": 1.0}},
        "clip": {"class_type": "CLIPLoader", "inputs": {"clip_name": q["text_encoder"], "type": "qwen_image", "device": "default"}},
        "vae": {"class_type": "VAELoader", "inputs": {"vae_name": q["vae"]}},
        "ref": {"class_type": "LoadImage", "inputs": {"image": ref}},
        "pos_raw": {"class_type": "TextEncodeQwenImageEditPlus",
                    "inputs": {"clip": ["clip", 0], "prompt": prompt, "vae": ["vae", 0], "image1": ["ref", 0]}},
        "neg_raw": {"class_type": "TextEncodeQwenImageEditPlus",
                    "inputs": {"clip": ["clip", 0], "prompt": "", "vae": ["vae", 0], "image1": ["ref", 0]}},
        "pos": {"class_type": "FluxKontextMultiReferenceLatentMethod",
                "inputs": {"conditioning": ["pos_raw", 0], "reference_latents_method": "index_timestep_zero"}},
        "neg": {"class_type": "FluxKontextMultiReferenceLatentMethod",
                "inputs": {"conditioning": ["neg_raw", 0], "reference_latents_method": "index_timestep_zero"}},
        "latent": {"class_type": "VAEEncode", "inputs": {"pixels": ["ref", 0], "vae": ["vae", 0]}},
        "sample": {"class_type": "KSampler",
                   "inputs": {"model": ["norm", 0], "positive": ["pos", 0], "negative": ["neg", 0], "latent_image": ["latent", 0],
                              "seed": seed, "steps": q["steps"], "cfg": q["cfg"], "sampler_name": q.get("sampler", "euler"),
                              "scheduler": q.get("scheduler", "simple"), "denoise": 1.0}},
        "decode": {"class_type": "VAEDecode", "inputs": {"samples": ["sample", 0], "vae": ["vae", 0]}},
        "save": {"class_type": "SaveImage", "inputs": {"images": ["decode", 0], "filename_prefix": prefix}},
    }
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
            prefix = f"spritegen/{job['key']}"
            graph = (job["build"](refs, seed, prefix) if job.get("build")
                     else build_graph(cfg, job["prompt"], refs, *job["size"], seed, prefix))
            pending.append((job, i + 1, seed, comfy.queue(graph)))
    print(f"Queued {len(pending)} image(s) in ComfyUI.")
    done: dict[str, list[Path]] = {}
    for job, i, seed, pid in pending:
        path = job["out"] / f"{job['key']}_{i}_s{seed}.png"
        comfy.wait_image(pid).save(path)
        done.setdefault(job["key"], []).append(path)
        print(f"  {path.relative_to(ROOT)}")
    for job in jobs:
        if job.get("after"):
            done[job["key"]] = [job["after"](path) for path in done[job["key"]]]
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
    spec = char["views"][view]
    text = (f"Show the exact same character from image 1 with the same design, colours, proportions, size and "
            f"pixel art style, now in {spec['label']}, standing idle and holding the staff upright, the whole body visible. ")
    if spec.get("notes"):
        text += spec["notes"] + " "
    if char.get("keep"):
        text += char["keep"] + " "
    return text + f"{char['style']}."


def view_reference(char: dict, view: str, front: Image.Image) -> Image.Image:
    """The image a view is drawn from: the approved front, mirrored for views that
    ask for it (the back: seen from behind, the staff hand and the bent hat tip
    sit on the other side of the image, which the mirror already gets right)."""
    if char["views"][view].get("mirror_reference"):
        return front.transpose(Image.FLIP_LEFT_RIGHT)
    return front


def anim_prompt(char: dict, view: str, anim: str, guide: bool) -> str:
    spec = char["animations"][anim]
    n = spec["frames"]
    label = char["views"][view]["label"]
    beats = " ".join(f"Frame {i}: {b}." for i, b in enumerate(spec["beats"], 1))
    text = (f"Create a horizontal sprite sheet with {n} animation frames side by side in {n} equal columns, "
            f"read left to right, of the exact same character from image 1 with the same design, colours, "
            f"proportions, size and pixel art style, in {label}. Every frame shows the whole body at the same "
            f"scale with the feet on the same baseline. ")
    if char.get("keep"):
        text += char["keep"] + " "
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
    generate(cfg, args, [{"key": v, "prompt": view_prompt(char, v), "refs": [view_reference(char, v, ref)],
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


SHEET_TAG_DIRS = ("down", "left", "right", "up")


def sheet_tags(cfg: dict) -> list[tuple[str, int, int]]:
    """Aseprite tags of a 6-column sheet row: walk 0-2, attack 3-4, hurt 5."""
    cols = cfg["qwen_sheet"]["columns"]
    tags = []
    for r, d in enumerate(SHEET_TAG_DIRS):
        b = r * cols
        tags += [(f"walk_{d}", b, b + 2), (f"attack_{d}", b + 3, b + 4), (f"hurt_{d}", b + 5, b + 5)]
    return tags


def sheet_outputs(cfg: dict, raw_path: Path, char_name: str) -> Path:
    """A generated 768 sheet -> next to it: the cleaned game-size sheet (what
    `approve` takes), a x4 preview, a GIF of all directions and an .aseprite file
    with tags and an empty "staff" layer for hand cleanup. Returns the preview."""
    import aseprite_io
    q = cfg["qwen_sheet"]
    cols, rows_n, f = q["columns"], q["rows"], q["downscale"]
    rows = sheet_cells(Image.open(raw_path).convert("RGB"), cols, rows_n, f)
    raw = [fr for row in rows for fr in row]
    fw, fh = raw[0].size
    grid = Image.new("RGBA", (cols * fw, rows_n * fh), (0, 0, 0, 0))
    for i, fr in enumerate(raw):
        grid.alpha_composite(fr, ((i % cols) * fw, (i // cols) * fh))
    pal_img = palette_from(grid, 32)  # one palette for the whole sheet
    frames = [despeckle(snap(fr, pal_img)) for fr in raw]
    grid = Image.new("RGBA", grid.size, (0, 0, 0, 0))
    for i, fr in enumerate(frames):
        grid.alpha_composite(fr, ((i % cols) * fw, (i // cols) * fh))
    base = raw_path.with_name(raw_path.name.replace("_raw", ""))
    grid.save(base)
    prev = Image.new("RGBA", grid.size, (64, 64, 76, 255))
    prev.alpha_composite(grid)
    preview = base.with_name(base.stem + "_x4.png")
    prev.convert("RGB").resize((grid.width * 4, grid.height * 4), Image.NEAREST).save(preview)
    gif = []
    for c in [0, 1, 2, 1, 0, 1, 2, 1, 3, 4, 4, 5, 5]:
        strip = Image.new("RGBA", (fw * rows_n, fh), (0, 0, 0, 0))
        for r in range(rows_n):
            strip.alpha_composite(frames[r * cols + c], (r * fw, 0))
        gif.append(strip)
    save_gif(gif, base.with_suffix(".gif"), 170)
    pal = pal_img.getpalette()[:96]
    aseprite_io.write(base.with_name(f"{char_name}_{base.stem}.aseprite"), [[fr, None] for fr in frames],
                      ["sprite", "staff"], sheet_tags(cfg), 150, [tuple(pal[i:i + 3]) for i in range(0, 96, 3)])
    return preview


def cmd_sheet(cfg, args):
    """Whole-character sheet in one image (Qwen Image Edit 2511 + svntax's 32x48
    combat LoRA): 4 directions x (3 walk, 2 attack, 1 hurt), from the approved
    concept. Each candidate also gets a cleaned 32x48 sheet and an .aseprite file."""
    char = load_char(args.char)
    front = approved(char, "concept")
    if not front:
        sys.exit("Approve a concept first: spritegen.py approve <char> concept <n>")
    q = cfg["qwen_sheet"]
    prompt = sheet_prompt(char) + (" " + char["sheet_extra"] if char.get("sheet_extra") else "")
    if args.extra:
        prompt += " " + args.extra
    ref = square_reference(Image.open(front), q["size"])
    generate(cfg, args, [{
        "key": "sheet", "prompt": prompt, "refs": [ref], "out": OUT / char["name"] / "sheet_gen",
        "build": lambda refs, seed, prefix: build_qwen_sheet_graph(cfg, prompt, refs[0], seed, prefix),
        "after": lambda path: sheet_outputs(cfg, path.rename(path.with_name(path.stem + "_raw.png")), char["name"]),
    }])


def cmd_guides(cfg, args):
    char = load_char(args.char)
    out = OUT / char["name"] / "guides"
    out.mkdir(parents=True, exist_ok=True)
    for key, view, anim in anim_keys(char):
        poses.guide_strip(view, anim, char["animations"][anim]["frames"]).save(out / f"{key}.png")
    print(f"Pose guides in {out.relative_to(ROOT)}/")


def cmd_approve(cfg, args):
    char = load_char(args.char)
    stage = ("concept" if args.key == "concept" else "views" if args.key in char["views"]
             else "sheet_gen" if args.key == "sheet" else "anims")
    matches = sorted(p for p in (OUT / char["name"] / stage).glob(f"{args.key}_{args.number}_s*.png")
                     if not p.stem.endswith(("_raw", "_x4")))
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


def dominant_downscale(img: Image.Image, factor: int, colours: int = 64) -> Image.Image:
    """Pixel-art downscale (k-centroid style): each factor x factor block becomes its
    most common colour after a light quantize, so outlines and flat fills stay crisp
    instead of averaging into mud. Alpha: a block is opaque if most of it is."""
    rgba = img.convert("RGBA")
    w, h = rgba.width // factor, rgba.height // factor
    q = rgba.convert("RGB").quantize(colours, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)
    pal = q.getpalette()
    qp, ap = q.load(), rgba.getchannel("A").load()
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    op = out.load()
    for by in range(h):
        for bx in range(w):
            counts: dict[int, int] = {}
            opaque = 0
            for y in range(by * factor, (by + 1) * factor):
                for x in range(bx * factor, (bx + 1) * factor):
                    if ap[x, y] > 127:
                        opaque += 1
                        counts[qp[x, y]] = counts.get(qp[x, y], 0) + 1
            if opaque * 2 > factor * factor:
                i = max(counts, key=counts.get)
                op[bx, by] = (pal[3 * i], pal[3 * i + 1], pal[3 * i + 2], 255)
    return out


def sheet_cells(sheet: Image.Image, columns: int, rows: int, factor: int) -> list[list[Image.Image]]:
    """A generated grid sheet -> rows of game-sized RGBA frames (background cleared
    on the full-size sheet, then each cell downscaled)."""
    clean = remove_background(sheet)
    cw, ch = clean.width // columns, clean.height // rows
    return [[dominant_downscale(clean.crop((c * cw, r * ch, (c + 1) * cw, (r + 1) * ch)), factor)
             for c in range(columns)] for r in range(rows)]


def despeckle(frame: Image.Image, white: int = 225) -> Image.Image:
    """Clears what background removal leaves behind on a small frame: lone opaque
    pixels (at most one opaque 4-neighbour) and near-white pixels on the edge of
    the sprite. Repeats until nothing changes."""
    out = frame.convert("RGBA").copy()
    px = out.load()
    w, h = out.size

    def opaque(x, y):
        return 0 <= x < w and 0 <= y < h and px[x, y][3] > 127

    changed = True
    while changed:
        changed = False
        for y in range(h):
            for x in range(w):
                p = px[x, y]
                if p[3] <= 127:
                    continue
                n = sum(opaque(x + dx, y + dy) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)))
                if n <= 1 or (n < 4 and min(p[:3]) >= white):
                    px[x, y] = (0, 0, 0, 0)
                    changed = True
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

# Widget order (and output types) of every node class these graphs use, for the
# canvas ("UI") format, which stores widget values as a positional list.
UI_WIDGETS = {
    "UNETLoader": ["unet_name", "weight_dtype"], "UnetLoaderGGUF": ["unet_name"],
    "CLIPLoader": ["clip_name", "type", "device"], "VAELoader": ["vae_name"],
    "LoraLoaderModelOnly": ["lora_name", "strength_model"], "CLIPTextEncode": ["text"],
    "ConditioningZeroOut": [], "LoadImage": ["image", "#upload"],
    "ImageScaleToTotalPixels": ["upscale_method", "megapixels", "resolution_steps"],
    "VAEEncode": [], "VAEDecode": [], "ReferenceLatent": [],
    "EmptyFlux2LatentImage": ["width", "height", "batch_size"], "Flux2Scheduler": ["steps", "width", "height"],
    "KSamplerSelect": ["sampler_name"], "RandomNoise": ["noise_seed", "#fixed"], "CFGGuider": ["cfg"],
    "SamplerCustomAdvanced": [], "SaveImage": ["filename_prefix"], "ModelSamplingAuraFlow": ["shift"],
    "CFGNorm": ["strength"], "TextEncodeQwenImageEditPlus": ["prompt"],
    "FluxKontextMultiReferenceLatentMethod": ["reference_latents_method"],
    "KSampler": ["seed", "#fixed", "steps", "cfg", "sampler_name", "scheduler", "denoise"],
}
UI_OUTPUTS = {
    "UNETLoader": ["MODEL"], "UnetLoaderGGUF": ["MODEL"], "CLIPLoader": ["CLIP"], "VAELoader": ["VAE"],
    "LoraLoaderModelOnly": ["MODEL"], "CLIPTextEncode": ["CONDITIONING"], "ConditioningZeroOut": ["CONDITIONING"],
    "LoadImage": ["IMAGE", "MASK"], "ImageScaleToTotalPixels": ["IMAGE"], "VAEEncode": ["LATENT"],
    "VAEDecode": ["IMAGE"], "ReferenceLatent": ["CONDITIONING"], "EmptyFlux2LatentImage": ["LATENT"],
    "Flux2Scheduler": ["SIGMAS"], "KSamplerSelect": ["SAMPLER"], "RandomNoise": ["NOISE"], "CFGGuider": ["GUIDER"],
    "SamplerCustomAdvanced": ["LATENT", "LATENT"], "SaveImage": [], "ModelSamplingAuraFlow": ["MODEL"],
    "CFGNorm": ["MODEL"], "TextEncodeQwenImageEditPlus": ["CONDITIONING"],
    "FluxKontextMultiReferenceLatentMethod": ["CONDITIONING"], "KSampler": ["LATENT"],
}
UI_INPUT_TYPES = {"model": "MODEL", "clip": "CLIP", "vae": "VAE", "conditioning": "CONDITIONING",
                  "positive": "CONDITIONING", "negative": "CONDITIONING", "latent": "LATENT",
                  "latent_image": "LATENT", "samples": "LATENT", "pixels": "IMAGE", "image": "IMAGE",
                  "images": "IMAGE", "image1": "IMAGE", "image2": "IMAGE", "image3": "IMAGE", "noise": "NOISE",
                  "guider": "GUIDER", "sampler": "SAMPLER", "sigmas": "SIGMAS"}


def api_to_ui(graph: dict, titles: dict[str, str] | None = None, note: str = "") -> dict:
    """An API-format graph -> a canvas workflow (nodes laid out left to right by
    depth), so it can live in ComfyUI's Workflows sidebar."""
    titles = titles or {}
    ids = {k: i + 1 for i, k in enumerate(graph)}
    depth: dict[str, int] = {}

    def d(k):
        if k not in depth:
            ins = [v[0] for v in graph[k]["inputs"].values() if isinstance(v, list)]
            depth[k] = 1 + max((d(i) for i in ins), default=-1)
        return depth[k]

    for k in graph:
        d(k)
    rows: dict[int, int] = {}
    nodes, links = [], []
    out_links: dict[tuple[str, int], list[int]] = {}
    for k, n in graph.items():
        for name, v in n["inputs"].items():
            if isinstance(v, list):
                lid = len(links) + 1
                links.append([lid, ids[v[0]], v[1], ids[k], None, None])
                out_links.setdefault((v[0], v[1]), []).append(lid)
    by_id = {l[0]: l for l in links}
    for order, (k, n) in enumerate(sorted(graph.items(), key=lambda kv: depth[kv[0]])):
        cls = n["class_type"]
        col = depth[k]
        row = rows.get(col, 0)
        tall = 260 if cls in ("TextEncodeQwenImageEditPlus", "CLIPTextEncode", "LoadImage", "SaveImage") else 130
        rows[col] = row + tall + 30
        inputs = []
        for name, v in n["inputs"].items():
            if isinstance(v, list):
                lid = next(l for l in out_links[(v[0], v[1])] if by_id[l][3] == ids[k] and by_id[l][4] is None)
                by_id[lid][4] = len(inputs)
                by_id[lid][5] = UI_INPUT_TYPES.get(name, "*")
                inputs.append({"name": name, "type": UI_INPUT_TYPES.get(name, "*"), "link": lid})
        widgets = []
        for w in UI_WIDGETS[cls]:
            widgets.append({"#upload": "image", "#fixed": "fixed"}[w] if w.startswith("#") else n["inputs"][w])
        outputs = [{"name": t, "type": t, "links": out_links.get((k, i), []), "slot_index": i}
                   for i, t in enumerate(UI_OUTPUTS[cls])]
        node = {"id": ids[k], "type": cls, "pos": [40 + col * 360, 40 + row], "size": [320, tall - 20],
                "flags": {}, "order": order, "mode": 0, "inputs": inputs, "outputs": outputs,
                "properties": {"Node name for S&R": cls}, "widgets_values": widgets}
        if k in titles:
            node["title"] = titles[k]
        nodes.append(node)
    if note:
        nodes.append({"id": len(nodes) + 1, "type": "MarkdownNote", "pos": [40, -380], "size": [700, 340],
                      "flags": {}, "order": len(nodes), "mode": 0, "inputs": [], "outputs": [],
                      "properties": {}, "widgets_values": [note], "title": "Cómo usar"})
    return {"last_node_id": len(nodes), "last_link_id": len(links), "nodes": nodes, "links": links,
            "groups": [], "config": {}, "extra": {"ds": {"scale": 0.7, "offset": [80, 420]}}, "version": 0.4}


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
    graphs["4_qwen_sheet"] = build_qwen_sheet_graph(cfg, sheet_prompt(char), "approved_concept_768.png", 1,
                                                    "spritegen/sheet")
    notes = {
        "1_concept_from_reference": "**Concepto (frente)** desde tu imagen de referencia, FLUX.2 Klein 4B. "
                                    "Elige la referencia en *Load Image* y cambia la semilla para más candidatos.",
        "1_concept_from_text": "**Concepto (frente)** solo desde texto, FLUX.2 Klein 4B.",
        "2_view_side": "**Vista de lado** desde el concepto aprobado (imagen 1). Para la espalda, usa el "
                       "concepto en espejo y cambia el prompt.",
        "3_anim_walk_side": "**Tira de animación** (4 frames): imagen 1 = vista aprobada, imagen 2 = guía de "
                            "poses (`spritegen.py guides`).",
        "4_qwen_sheet": "**Hoja completa 32x48** (Qwen Image Edit 2511 + LoRA pixel_4walk_32x48). Referencia: el "
                        "concepto centrado en 768x768 (`arcanist_concept_768.png`). Sale una hoja de 6x4: por fila "
                        "(abajo, izquierda, derecha, arriba) 3 andar, 2 ataque, 1 daño. ~4 min. Para reducir a "
                        "32x48, limpiar y sacar el .aseprite: `spritegen.py sheet`.",
    }
    titles = {"ref0_load": "Imagen 1 (referencia)", "ref1_load": "Imagen 2 (guía de poses)", "ref": "Referencia 768x768",
              "pos": "Prompt", "pos_raw": "Prompt", "neg_raw": "Prompt negativo (vacío)", "lora": "LoRA",
              "sample": "Muestreo", "save": "Guardar"}
    for name, g in graphs.items():
        (WORKFLOWS / f"{name}.api.json").write_text(json.dumps(g, indent=2) + "\n")
        ui = api_to_ui(g, titles, notes.get(name, ""))
        (WORKFLOWS / f"spritegen_{name}.json").write_text(json.dumps(ui, indent=1, ensure_ascii=False) + "\n")
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

    gen("sheet", cmd_sheet, "whole 32x48 sheet (Qwen Edit 2511 + combat LoRA) from the concept").add_argument(
        "--extra", help="text appended to the sheet prompt (e.g. ask for the staff)")

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
