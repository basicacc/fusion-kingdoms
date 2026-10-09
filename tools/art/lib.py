"""Tiny pixel-art toolkit: palette, canvases, ASCII sprites, outlines, PNG output and preview sheets."""
import struct
import zlib
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ROOT / "assets"
PREVIEW = Path("/tmp/claude-1000/-home-eyes-Documents-fusion-kingdoms/3b6e6766-2f03-4acc-a144-3baa2d38c3a3/scratchpad/art")


def hexc(h, a=255):
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


# A cozy dark-fantasy palette. Ramps go light -> dark.
P = {
    "outline": hexc("1d1626"),
    "skin": [hexc("f6d0ad"), hexc("e3a985"), hexc("b97a5e"), hexc("7d4d3e")],
    "hair": [hexc("8d5634"), hexc("6a3d26"), hexc("47281b")],
    "blond": [hexc("f3d27a"), hexc("d4a548"), hexc("9c7130")],
    "blue": [hexc("8fc0ff"), hexc("4f86e6"), hexc("2f55b0"), hexc("1f3570")],
    "red": [hexc("ff9a86"), hexc("e05a4f"), hexc("a8333c"), hexc("6b1f2c")],
    "wild": [hexc("b5e07a"), hexc("6fb04a"), hexc("3f7a35"), hexc("26502a")],
    "leather": [hexc("c58d55"), hexc("945f36"), hexc("63402a")],
    "steel": [hexc("eef2f8"), hexc("b4bfd0"), hexc("7a86a0"), hexc("4b5268")],
    "gold": [hexc("fff0a0"), hexc("f6c544"), hexc("c88a24"), hexc("7d5418")],
    "straw": [hexc("f8e39a"), hexc("dcb45c"), hexc("a8813a")],
    "white": [hexc("ffffff"), hexc("e6e1d8"), hexc("b9b1a6")],
    "dark": [hexc("5a5370"), hexc("3a344c"), hexc("252033")],
    "green": [hexc("8ad67a"), hexc("4e9e52"), hexc("2f6a3a")],
    "fur": [hexc("d8b07e"), hexc("a77d52"), hexc("6e4f33")],
    "lumen": [hexc("e8ffff"), hexc("8ff3ff"), hexc("3cc4e0"), hexc("1f7f9e")],
    "wood": [hexc("c9925f"), hexc("94653f"), hexc("63432c"), hexc("3f2a1e")],
    "stone": [hexc("c4c3cf"), hexc("9796a8"), hexc("6b6a7e"), hexc("47465a")],
    "purple": [hexc("d6a6ff"), hexc("9a5ce0"), hexc("6136a8"), hexc("3a2070")],
    "pink": [hexc("ffc0e0"), hexc("f07ac0"), hexc("b04a8a")],
    "orange": [hexc("ffc27a"), hexc("f08a3a"), hexc("b0581f")],
    "black": [hexc("2a2533")],
}
CLEAR = (0, 0, 0, 0)


class Canvas:
    def __init__(self, w, h):
        self.a = np.zeros((h, w, 4), np.uint8)

    @property
    def w(self):
        return self.a.shape[1]

    @property
    def h(self):
        return self.a.shape[0]

    def px(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h and c is not None and c[3] > 0:
            if c[3] == 255:
                self.a[y, x] = c
            else:
                src = np.array(c, np.float32)
                dst = self.a[y, x].astype(np.float32)
                al = src[3] / 255.0
                out = src * al + dst * (1 - al)
                out[3] = min(255, dst[3] + src[3] * (1 - dst[3] / 255.0))
                self.a[y, x] = out.astype(np.uint8)

    def get(self, x, y):
        if 0 <= x < self.w and 0 <= y < self.h:
            return tuple(int(v) for v in self.a[y, x])
        return CLEAR

    def rect(self, x, y, w, h, c):
        for yy in range(y, y + h):
            for xx in range(x, x + w):
                self.px(xx, yy, c)

    def ellipse(self, cx, cy, rx, ry, c):
        for y in range(int(cy - ry - 1), int(cy + ry + 2)):
            for x in range(int(cx - rx - 1), int(cx + rx + 2)):
                if ((x + 0.5 - cx) / rx) ** 2 + ((y + 0.5 - cy) / ry) ** 2 <= 1.0:
                    self.px(x, y, c)

    def line(self, x0, y0, x1, y1, c):
        n = max(abs(x1 - x0), abs(y1 - y0), 1)
        for i in range(n + 1):
            self.px(round(x0 + (x1 - x0) * i / n), round(y0 + (y1 - y0) * i / n), c)

    def blit(self, other, ox, oy, flip=False):
        src = other.a[:, ::-1] if flip else other.a
        for y in range(src.shape[0]):
            for x in range(src.shape[1]):
                c = src[y, x]
                if c[3]:
                    self.px(ox + x, oy + y, tuple(int(v) for v in c))

    def ascii(self, rows, legend, ox=0, oy=0, flip=False):
        """Draw an ASCII sprite. legend maps characters to colors; '.' and ' ' are transparent."""
        h = len(rows)
        w = max(len(r) for r in rows)
        for y, row in enumerate(rows):
            for x, ch in enumerate(row):
                if ch in ". ":
                    continue
                xx = (w - 1 - x) if flip else x
                self.px(ox + xx, oy + y, legend[ch])

    def outline(self, color=None, diagonal=False):
        """Add a 1px outline around every opaque pixel, in empty neighbours."""
        color = color or P["outline"]
        # Only fully opaque-ish pixels cast an outline (soft shadows don't), and nothing wraps around the edges.
        solid = self.a[:, :, 3] > 160
        padded = np.pad(solid, 1)
        h, w = solid.shape
        out = np.zeros_like(solid)
        shifts = [(0, 1), (0, -1), (1, 0), (-1, 0)]
        if diagonal:
            shifts += [(1, 1), (1, -1), (-1, 1), (-1, -1)]
        for dy, dx in shifts:
            out |= padded[1 + dy:1 + dy + h, 1 + dx:1 + dx + w]
        edge = out & ~solid & (self.a[:, :, 3] < 160)
        self.a[edge] = color
        return self

    def save(self, path):
        write_png(path, self.a)


def write_png(path, arr):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    h, w = arr.shape[:2]
    raw = b"".join(b"\x00" + arr[y].tobytes() for y in range(h))

    def chunk(tag, data):
        c = struct.pack(">I", len(data)) + tag + data
        return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    path.write_bytes(png)


def sheet(frames, cols):
    """Pack equally sized canvases into one sheet canvas, row-major."""
    fw, fh = frames[0].w, frames[0].h
    rows = (len(frames) + cols - 1) // cols
    s = Canvas(fw * cols, fh * rows)
    for i, f in enumerate(frames):
        s.a[(i // cols) * fh:(i // cols + 1) * fh, (i % cols) * fw:(i % cols + 1) * fw] = f.a
    return s


def preview(canvas, name, scale=6, bg=(48, 64, 52)):
    """Upscaled copy on a background with a light grid, for inspecting art."""
    a = canvas.a.astype(np.float32)
    h, w = a.shape[:2]
    big = np.repeat(np.repeat(a, scale, 0), scale, 1)
    back = np.zeros_like(big)
    back[:, :, :3] = bg
    back[:, :, 3] = 255
    al = big[:, :, 3:4] / 255.0
    out = big * al + back * (1 - al)
    out[:, :, 3] = 255
    write_png(PREVIEW / f"{name}.png", out.astype(np.uint8))
