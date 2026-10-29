"""Minimal .aseprite writer (RGBA, one or more layers, tags, palette) so generated
sheets open in Aseprite as real animations to clean by hand.
Format: https://github.com/aseprite/aseprite/blob/main/docs/ase-file-specs.md"""

from __future__ import annotations

import struct
import zlib
from pathlib import Path

from PIL import Image


def _string(s: str) -> bytes:
    b = s.encode()
    return struct.pack("<H", len(b)) + b


def _chunk(kind: int, data: bytes) -> bytes:
    return struct.pack("<IH", len(data) + 6, kind) + data


def _layer(name: str) -> bytes:
    # flags visible|editable, normal layer, level 0, blend normal, opacity 255
    return _chunk(0x2004, struct.pack("<HHHHHHB3x", 3, 0, 0, 0, 0, 0, 255) + _string(name))


def _cel(layer: int, img: Image.Image) -> bytes:
    box = img.getchannel("A").getbbox()
    if not box:
        return b""
    crop = img.crop(box)
    data = struct.pack("<HhhBHh5x", layer, box[0], box[1], 255, 2, 0)
    data += struct.pack("<HH", crop.width, crop.height) + zlib.compress(crop.tobytes())
    return _chunk(0x2005, data)


def _tags(tags: list[tuple[str, int, int]]) -> bytes:
    data = struct.pack("<H8x", len(tags))
    for name, start, end in tags:
        data += struct.pack("<HHBH6x3Bx", start, end, 0, 0, 0, 0, 0) + _string(name)
    return _chunk(0x2018, data)


def _palette(colours: list[tuple[int, int, int]]) -> bytes:
    data = struct.pack("<III8x", len(colours), 0, len(colours) - 1)
    for r, g, b in colours:
        data += struct.pack("<HBBBB", 0, r, g, b, 255)
    return _chunk(0x2019, data)


def write(path: Path, frames: list[list[Image.Image | None]], layer_names: list[str],
          tags: list[tuple[str, int, int]], duration_ms: int = 150,
          palette: list[tuple[int, int, int]] | None = None) -> None:
    """frames[i][l] is frame i's image on layer l (None = empty cel); all the same size."""
    w, h = next(img.size for fr in frames for img in fr if img is not None)
    body = b""
    for i, layers in enumerate(frames):
        chunks = []
        if i == 0:
            if palette:
                chunks.append(_palette(palette))
            chunks += [_layer(n) for n in layer_names]
            if tags:
                chunks.append(_tags(tags))
        chunks += [c for l, img in enumerate(layers) if img is not None and (c := _cel(l, img.convert("RGBA")))]
        payload = b"".join(chunks)
        n = len(chunks)
        body += struct.pack("<IHHH2xI", 16 + len(payload), 0xF1FA, min(n, 0xFFFF), duration_ms, n) + payload
    header = struct.pack("<IHHHHHIH8xB3xHBBhhHH84x", 128 + len(body), 0xA5E0, len(frames), w, h, 32, 1, 100,
                         0, 0, 1, 1, 0, 0, w, h)
    assert len(header) == 128
    Path(path).write_bytes(header + body)
