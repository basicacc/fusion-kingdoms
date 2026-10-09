"""Terrain tiles (16x16, several variants each), trees, decorations and resource nodes."""
import random

from lib import P, Canvas, hexc, preview, sheet, ASSETS

T = 16
GRASS = [hexc("7fb85a"), hexc("6aa34c"), hexc("5a9143"), hexc("487a3a")]
FOREST = [hexc("5d9446"), hexc("4a7c3b"), hexc("3a6531"), hexc("2c4f28")]
DIRT = [hexc("d6b07a"), hexc("bb935e"), hexc("9a7449"), hexc("745637")]
SOIL = [hexc("9a6e45"), hexc("7d5636"), hexc("62422a"), hexc("4a301f")]
HILL = [hexc("9ccb6a"), hexc("7fb455"), hexc("5f9343"), hexc("47733a")]
MARSH = [hexc("6f9a6e"), hexc("4f7d5f"), hexc("3a6152"), hexc("2a4a42")]
WATER = [hexc("8fd2f0"), hexc("5aa9e0"), hexc("3a7fc4"), hexc("2a5a9a")]
ROCK = [hexc("b9b6c4"), hexc("8f8ca0"), hexc("6a687e"), hexc("4a485c"), hexc("33313f")]


def noise_fill(c, ramp, rng, weights=(0.15, 0.6, 0.2, 0.05)):
    for y in range(T):
        for x in range(T):
            r = rng.random()
            acc = 0
            for i, w in enumerate(weights):
                acc += w
                if r < acc:
                    c.px(x, y, ramp[i])
                    break


def clusters(c, ramp_idx_color, rng, n, size=2):
    for _ in range(n):
        x, y = rng.randrange(T), rng.randrange(T)
        for _ in range(size):
            c.px((x + rng.randint(-1, 1)) % T, (y + rng.randint(-1, 1)) % T, ramp_idx_color)


def grass_tile(rng, flowers=False):
    c = Canvas(T, T)
    c.rect(0, 0, T, T, GRASS[1])
    clusters(c, GRASS[0], rng, 4, 3)
    clusters(c, GRASS[2], rng, 6, 3)
    for _ in range(2):  # tufts
        x, y = rng.randrange(1, T - 2), rng.randrange(2, T - 1)
        c.px(x, y, GRASS[3]); c.px(x + 1, y - 1, GRASS[0]); c.px(x + 2, y, GRASS[3])
    if flowers:
        for _ in range(1):
            x, y = rng.randrange(2, T - 2), rng.randrange(2, T - 2)
            col = rng.choice([hexc("ffffff"), hexc("ffe36e"), hexc("f59ad0"), hexc("a8c8ff")])
            c.px(x, y, col); c.px(x - 1, y, col); c.px(x + 1, y, col); c.px(x, y - 1, col); c.px(x, y, hexc("ffd23f"))
    return c


def forest_floor(rng):
    c = Canvas(T, T)
    c.rect(0, 0, T, T, FOREST[2])
    clusters(c, FOREST[1], rng, 7, 3)
    clusters(c, FOREST[3], rng, 6, 3)
    for _ in range(3):
        x, y = rng.randrange(T), rng.randrange(T)
        c.px(x, y, rng.choice([hexc("b0723a"), hexc("c99a3e"), hexc("8e5a2e")]))
    return c


def dirt_tile(rng):
    c = Canvas(T, T)
    c.rect(0, 0, T, T, DIRT[1])
    clusters(c, DIRT[0], rng, 6, 3)
    clusters(c, DIRT[2], rng, 5, 2)
    for _ in range(3):
        x, y = rng.randrange(1, T - 1), rng.randrange(1, T - 1)
        c.px(x, y, DIRT[3]); c.px(x + 1, y, DIRT[0])
    return c


def farmland_tile(rng):
    c = Canvas(T, T)
    c.rect(0, 0, T, T, SOIL[1])
    for y in range(0, T, 4):  # furrows
        for x in range(T):
            c.px(x, y + 2, SOIL[2]); c.px(x, y + 3, SOIL[3]); c.px(x, y, SOIL[0] if rng.random() < 0.35 else SOIL[1])
    for y in range(1, T, 4):
        for x in range(rng.randrange(0, 3), T, 3):
            if rng.random() < 0.6:
                c.px(x, y, hexc("8fcf5a")); c.px(x, y - 1, hexc("b8e27a"))
    return c


def hill_tile(rng):
    c = Canvas(T, T)
    c.rect(0, 0, T, T, HILL[1])
    clusters(c, HILL[0], rng, 6, 3)
    clusters(c, HILL[2], rng, 4, 3)
    # a soft contour line suggests the slope
    y0 = rng.randrange(8, 13)
    for x in range(T):
        y = y0 + (1 if (x // 5) % 2 else 0)
        c.px(x, y, HILL[3]); c.px(x, y - 1, HILL[0])
    if rng.random() < 0.5:
        x, y = rng.randrange(2, 13), rng.randrange(2, 7)
        c.px(x, y, ROCK[1]); c.px(x + 1, y, ROCK[2]); c.px(x, y - 1, ROCK[0])
    return c


def marsh_tile(rng):
    c = Canvas(T, T)
    c.rect(0, 0, T, T, MARSH[2])
    clusters(c, MARSH[3], rng, 6, 3)
    clusters(c, MARSH[1], rng, 5, 2)
    for _ in range(rng.randint(1, 2)):  # lily pad
        x, y = rng.randrange(2, 13), rng.randrange(2, 13)
        c.rect(x, y, 3, 2, hexc("6fb04a")); c.px(x + 1, y, hexc("a6d96a"))
    for _ in range(rng.randint(2, 4)):  # reeds
        x, y = rng.randrange(1, 15), rng.randrange(4, 15)
        for k in range(3):
            c.px(x, y - k, hexc("9ab35a") if k < 2 else hexc("c9a24a"))
    return c


def water_tile(rng, frame=0):
    c = Canvas(T, T)
    c.rect(0, 0, T, T, WATER[2])
    clusters(c, WATER[3], rng, 5, 3)
    r = random.Random(rng.random())
    for _ in range(4):
        x, y = r.randrange(T), r.randrange(T)
        x = (x + frame * 2) % T
        for k in range(3):
            c.px((x + k) % T, y, WATER[1])
        c.px((x + 1) % T, y, WATER[0])
    return c


def rock_tile(rng):
    c = Canvas(T, T)
    c.rect(0, 0, T, T, ROCK[3])
    for _ in range(4):  # boulders, lit from the top-left
        cx, cy, rx, ry = rng.uniform(3, 13), rng.uniform(3, 13), rng.uniform(3, 5.5), rng.uniform(2.5, 4.5)
        c.ellipse(cx, cy, rx, ry, ROCK[2])
        c.ellipse(cx - 0.6, cy - 0.6, rx - 1.2, ry - 1.2, ROCK[1])
        c.ellipse(cx - 1.4, cy - 1.4, max(1, rx - 3), max(1, ry - 3), ROCK[0])
    for _ in range(6):
        x, y = rng.randrange(T), rng.randrange(T)
        c.px(x, y, ROCK[4])
    return c


TERRAINS = ["Grass", "Road", "Farmland", "Forest", "Hills", "Marsh", "River", "Rock"]
VARIANTS = 6


def tileset(seed=11):
    """Rows follow the sim's Terrain enum order; columns are variants."""
    rng = random.Random(seed)
    makers = {
        "Grass": lambda i: grass_tile(rng, flowers=i == 5),
        "Road": lambda i: dirt_tile(rng),
        "Farmland": lambda i: farmland_tile(rng),
        "Forest": lambda i: forest_floor(rng),
        "Hills": lambda i: hill_tile(rng),
        "Marsh": lambda i: marsh_tile(rng),
        "River": lambda i: water_tile(rng, i % 3),
        "Rock": lambda i: rock_tile(rng),
    }
    return sheet([makers[t](i) for t in TERRAINS for i in range(VARIANTS)], VARIANTS)


# ---------- trees ----------
LEAF = [hexc("8fd06a"), hexc("5fa84a"), hexc("3f8039"), hexc("2a5a2e"), hexc("1d3f24")]
PINE = [hexc("6fb07a"), hexc("458a5a"), hexc("2f6648"), hexc("1f4636")]


def tree(rng, kind="oak"):
    """Returns (trunk, canopy) canvases of 24x32. The trunk is drawn under units, the canopy above them."""
    trunk, canopy = Canvas(24, 32), Canvas(24, 32)
    # shadow and trunk
    trunk.ellipse(12, 29, 8, 2.5, (20, 40, 25, 110))
    trunk.rect(10, 20, 4, 9, P["wood"][2])
    trunk.rect(10, 20, 1, 9, P["wood"][1])
    trunk.px(13, 26, P["wood"][3]); trunk.px(9, 28, P["wood"][2]); trunk.px(14, 28, P["wood"][2])
    if kind == "oak":
        blobs = [(12, 11, 9, 8), (7, 15, 5, 5), (17, 15, 5, 5), (12, 6, 6, 5)]
        for cx, cy, rx, ry in blobs:
            canopy.ellipse(cx, cy + 1, rx, ry, LEAF[3])
        for cx, cy, rx, ry in blobs:
            canopy.ellipse(cx - 0.5, cy, rx - 1, ry - 1, LEAF[2])
        for cx, cy, rx, ry in blobs:
            canopy.ellipse(cx - 1.5, cy - 1.5, max(1, rx - 3), max(1, ry - 3), LEAF[1])
        for _ in range(14):
            x, y = rng.randrange(4, 19), rng.randrange(3, 18)
            if canopy.get(x, y)[3]:
                canopy.px(x, y, LEAF[0] if y < 11 else LEAF[3])
        if rng.random() < 0.3:
            for _ in range(3):
                x, y = rng.randrange(6, 18), rng.randrange(6, 16)
                if canopy.get(x, y)[3]:
                    canopy.px(x, y, hexc("e2504a"))
    else:
        for i, (w, y) in enumerate([(4, 3), (6, 7), (8, 11), (10, 15), (11, 19)]):
            for yy in range(y, y + 5):
                half = w * (yy - y + 2) / 6
                for x in range(int(12 - half), int(12 + half) + 1):
                    canopy.px(x, yy, PINE[2] if x > 12 else PINE[1])
            for x in range(int(12 - w * 0.5), 12):
                canopy.px(x, y + 2, PINE[0])
        for _ in range(8):
            x, y = rng.randrange(6, 18), rng.randrange(6, 23)
            if canopy.get(x, y)[3]:
                canopy.px(x, y, PINE[3])
    canopy.outline(hexc("163020"))
    trunk.outline()
    return trunk, canopy


def trees(seed=5):
    rng = random.Random(seed)
    trunks, canopies = [], []
    for i in range(8):
        t, c = tree(rng, "pine" if i >= 5 else "oak")
        trunks.append(t)
        canopies.append(c)
    return sheet(trunks, 8), sheet(canopies, 8)


# ---------- small decorations baked onto the ground ----------
def decorations(seed=9):
    rng = random.Random(seed)
    out = []
    for i in range(12):
        c = Canvas(T, T)
        kind = i % 6
        if kind == 0:  # bush
            c.ellipse(8, 10, 5, 4, LEAF[3]); c.ellipse(7.5, 9.5, 4, 3, LEAF[2]); c.ellipse(6.5, 8.5, 2, 1.5, LEAF[1])
        elif kind == 1:  # small rocks
            c.ellipse(6, 11, 3, 2, ROCK[2]); c.ellipse(5.5, 10.5, 2, 1.2, ROCK[0]); c.ellipse(11, 12, 2, 1.5, ROCK[1])
        elif kind == 2:  # mushrooms
            for x in (5, 9):
                c.rect(x, 10, 1, 3, hexc("f0e6d0")); c.rect(x - 1, 9, 3, 1, hexc("d9483f")); c.px(x, 9, hexc("ffffff"))
        elif kind == 3:  # stump
            c.ellipse(8, 11, 4, 2.5, P["wood"][2]); c.ellipse(8, 10, 3.5, 2, P["wood"][0]); c.px(8, 10, P["wood"][2])
        elif kind == 4:  # tall grass
            for x in range(4, 12, 2):
                h = rng.randint(3, 5)
                for k in range(h):
                    c.px(x + (k == h - 1), 13 - k, GRASS[3] if k < 2 else GRASS[0])
        else:  # flower patch
            for _ in range(4):
                x, y = rng.randrange(4, 12), rng.randrange(7, 13)
                col = rng.choice([hexc("ffffff"), hexc("ffe36e"), hexc("f59ad0")])
                c.px(x, y, col); c.px(x, y + 1, GRASS[3])
        if kind in (0, 1, 2, 3):
            c.outline()
        out.append(c)
    return sheet(out, 6)


# ---------- resource nodes ----------
def wheat(frame):
    c = Canvas(T, T)
    for x in range(1, 16, 2):
        lean = (1 if frame == 1 else 0) if x % 4 == 1 else 0
        for k in range(8):
            y = 14 - k
            col = hexc("c99a2f") if k < 4 else hexc("f0cf5a")
            c.px(x + (lean if k > 4 else 0), y, col)
        c.px(x + lean, 6, hexc("fff0a0")); c.px(x + lean - 1, 7, hexc("e9c85a")); c.px(x + lean + 1, 7, hexc("e9c85a"))
    return c


def stone_node():
    c = Canvas(T, T)
    c.ellipse(8, 10, 7, 5, ROCK[2]); c.ellipse(7.3, 9.3, 5.5, 3.8, ROCK[1]); c.ellipse(6, 8, 3, 2, ROCK[0])
    for x, y in ((10, 9), (5, 11), (11, 12)):
        c.px(x, y, hexc("e8e2ff")); c.px(x + 1, y, hexc("a8a0d8"))
    c.outline()
    return c


def lumen_node(frame):
    c = Canvas(24, 24)
    c.ellipse(12, 17, 10, 5, hexc("2a4f6a"))
    c.ellipse(12, 16.5, 8.5, 3.8, hexc("3cc4e0"))
    c.ellipse(11, 16, 5, 2, hexc("8ff3ff"))
    glow = frame % 3
    for cx, h, w in ((8, 9, 2), (12, 13, 3), (16, 8, 2), (14, 6, 1)):
        top = 16 - h
        for y in range(top, 16):
            half = w * (y - top + 1) / h
            for x in range(int(cx - half), int(cx + half) + 1):
                c.px(x, y, P["lumen"][2] if x >= cx else P["lumen"][1])
        c.px(cx, top, P["lumen"][0])
        if glow == 1:
            c.px(cx - 1, top + 2, hexc("ffffff"))
    c.outline(hexc("123448"))
    return c


def timber_node():
    """A felled log pile, placed on timber nodes."""
    c = Canvas(T, T)
    for i, y in enumerate((11, 8)):
        x0 = 2 + i * 2
        c.rect(x0, y, 12 - i * 3, 3, P["wood"][1]); c.rect(x0, y, 12 - i * 3, 1, P["wood"][0])
        c.ellipse(x0 + 0.5, y + 1.5, 1.5, 1.5, P["wood"][0]); c.px(x0, y + 1, P["wood"][2])
    c.outline()
    return c


def resources():
    small = [wheat(0), wheat(1), stone_node(), timber_node()]
    return sheet(small, 4), sheet([lumen_node(i) for i in range(3)], 3)


def build():
    out = ASSETS / "terrain"
    ts = tileset()
    ts.save(out / "tiles.png")
    tr, ca = trees()
    tr.save(out / "tree_trunks.png")
    ca.save(out / "tree_canopies.png")
    decorations().save(out / "decor.png")
    small, lum = resources()
    small.save(out / "resources.png")
    lum.save(out / "lumen.png")
    return ts, tr, ca, small, lum


if __name__ == "__main__":
    ts, tr, ca, small, lum = build()
    preview(ts, "tiles", 5)
    both = Canvas(tr.w, tr.h)
    both.blit(tr, 0, 0)
    both.blit(ca, 0, 0)
    preview(both, "trees", 5)
    preview(decorations(), "decor", 6)
    preview(small, "resources", 8)
    preview(lum, "lumen", 8)
