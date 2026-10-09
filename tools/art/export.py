"""Exports every sprite the game loads into assets/. Run after editing any art module."""
import math
import random

import chars
import creatures
import gear
from lib import ASSETS, P, Canvas, hexc, preview, sheet

DIRS = ("down", "up", "right", "left")


def frame_specs():
    for d in DIRS:
        for ph, bob in chars.IDLE + chars.WALK:
            yield d, ph, bob


def layer_sheet(draw):
    """24 frames (rows = down, up, right, left; cols = idle0, idle1, walk0..3) of one un-outlined layer."""
    frames = []
    for d, ph, bob in frame_specs():
        c = Canvas(16, 16)
        draw(c, "right" if d == "left" else d, 1 + bob, ph)
        if d == "left":
            c.a = c.a[:, ::-1].copy()
        frames.append(c)
    return sheet(frames, 6)


def export_humans():
    out = ASSETS / "characters"
    for team in ("blue", "red", "wild"):
        for hair in ("hair", "blond"):
            def body(c, d, head_y, ph, team=team, hair=hair):
                b, _ = chars.body(d, ph, head_y - 1, team, hair)
                c.blit(b, 0, 0)
            layer_sheet(body).save(out / f"body_{team}_{hair}.png")
    for role in chars.ROLES + ["Crown"]:
        def hat(c, d, head_y, ph, role=role):
            rows, dx, dy = gear.HATS[role][chars.view_of(d)]
            gear.draw(c, rows, dx, dy, head_y, P["blue"])
        layer_sheet(hat).save(out / f"hat_{role}.png")
    for role in chars.ROLES:
        for team in ("blue", "red"):
            def tool(c, d, head_y, ph, role=role, team=team):
                rows, dx, dy = gear.TOOLS[role][chars.view_of(d)]
                swing = 1 if ph == 1 else -1 if ph == 2 else 0
                gear.draw(c, rows, dx, dy + (swing if role != "Hoarder" else 0), head_y, P[team])
            layer_sheet(tool).save(out / f"tool_{role}_{team}.png")

    def cape(c, d, head_y, ph):
        v = chars.view_of(d)
        dark, mid = P["purple"][2], P["purple"][1]
        if v == "up":
            for y in range(head_y + 8, 14):
                for x in range(3, 13):
                    c.px(x, y, mid if x < 11 else dark)
        elif v == "down":
            for y in range(head_y + 8, 14):
                c.px(2, y, dark)
                c.px(13, y, dark)
        else:
            for y in range(head_y + 8, 14):
                c.px(4, y, mid)
                c.px(3, y + 1, dark)
    layer_sheet(cape).save(out / "cape.png")


def export_creatures():
    out = ASSETS / "characters"
    for kind in ("Sprite", "Troll", "Fox", "Moth", "Sage", "Aberration", "Sealed"):
        sheet(creatures.frames_for(kind), 6).save(out / f"creature_{kind}.png")


# ---------- effects ----------
def fx():
    frames = {}

    def dust(i):
        c = Canvas(16, 16)
        r = 2 + i * 1.5
        for k in range(5):
            a = k / 5 * math.tau
            c.ellipse(8 + math.cos(a) * r, 10 + math.sin(a) * r * 0.5, 2.2 - i * 0.4, 2.2 - i * 0.4, (230, 220, 200, 220 - i * 50))
        return c

    def sparkle(i):
        c = Canvas(16, 16)
        s = [1, 3, 4, 2][i]
        col = P["lumen"][0] if i != 2 else hexc("ffffff")
        for k in range(-s, s + 1):
            c.px(8 + k, 8, col)
            c.px(8, 8 + k, col)
        c.px(8, 8, hexc("ffffff"))
        return c

    def star(i):
        c = Canvas(16, 16)
        for k in range(3):
            a = (k / 3 + i / 12) * math.tau
            x, y = 8 + math.cos(a) * 5, 6 + math.sin(a) * 2
            c.px(int(x), int(y), P["gold"][0])
            c.px(int(x) + 1, int(y), P["gold"][1])
        return c

    def heart(i):
        c = Canvas(16, 16)
        rows = [".rr.rr.", "rRRrRRr", "rRRRRRr", ".rRRRr.", "..rRr..", "...r..."]
        c.ascii(rows, {"r": P["pink"][2], "R": P["pink"][1]}, 4, 6 - i)
        return c

    def swirl(i):
        c = Canvas(16, 16)
        for k in range(14):
            a = k * 0.45 + i * 0.8
            r = 1 + k * 0.45
            c.px(int(8 + math.cos(a) * r), int(8 + math.sin(a) * r), P["purple"][0] if k % 2 else P["lumen"][1])
        return c

    def coin(i):
        c = Canvas(16, 16)
        w = [3, 2, 1, 2][i]
        c.ellipse(8, 8, w + 0.5, 3.5, P["gold"][1])
        if w > 1:
            c.ellipse(7.5, 7.5, max(0.6, w - 1), 2, P["gold"][0])
        return c

    def leaf(i):
        c = Canvas(16, 16)
        c.px(6 + i, 6 + i * 2, hexc("8fd06a")); c.px(7 + i, 6 + i * 2, hexc("5fa84a"))
        return c

    for name, fn in (("dust", dust), ("sparkle", sparkle), ("star", star), ("heart", heart), ("swirl", swirl), ("coin", coin), ("leaf", leaf)):
        frames[name] = sheet([fn(i) for i in range(4)], 4)
        frames[name].save(ASSETS / "fx" / f"{name}.png")
    return frames


def preview_fusion():
    """Shows how layered minds combine: base roles and a few fused hat/tool mixes."""
    import numpy as np
    combos = [("Farmer", "Farmer"), ("Thief", "Guard"), ("Guard", "Thief"), ("Builder", "Guard"), ("Scout", "Healer"),
              ("Hunter", "Builder"), ("Healer", "Hoarder"), ("Hoarder", "Thief")]
    frames = []
    for hat, tool in combos:
        for d in ("down", "right"):
            frames.append(chars.frame(d, 0, 0, "red", "hair", chars.outfit(hat, tool, "red")))
    preview(sheet(frames, 8), "fusions", 7)


if __name__ == "__main__":
    export_humans()
    export_creatures()
    f = fx()
    preview(sheet([Canvas(1, 1)] if False else [sheet_ for sheet_ in f.values()], 1), "fx", 6)
    preview_fusion()
    print("exported", sum(1 for _ in ASSETS.rglob("*.png")), "png files")
