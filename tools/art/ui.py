"""The interface skin: framed panels, buttons, inputs, sliders, tooltips and resource icons.

Everything is drawn at 1x and saved at 2x so the UI has the same chunky pixels as the world.
Run: training/.venv/bin/python -W ignore tools/art/ui.py -> assets/ui/*.png
"""
import numpy as np

from lib import ASSETS, P, Canvas, hexc, preview, write_png

OUT = ASSETS / "ui"
INK = P["outline"]
GOLD = P["gold"]


def save2x(c, name):
    big = np.repeat(np.repeat(c.a, 2, 0), 2, 1)
    write_png(OUT / f"{name}.png", big)
    preview(c, f"ui_{name}", 8, bg=(30, 30, 40))


def frame(c, x, y, w, h, col):
    c.rect(x, y, w, 1, col)
    c.rect(x, y + h - 1, w, 1, col)
    c.rect(x, y, 1, h, col)
    c.rect(x + w - 1, y, 1, h, col)


# ---------- panels ----------

def panel():
    """Royal panel: dark slate interior inside a bronze-and-gold frame with rivets. 9-slice margin 8 (1x)."""
    n = 24
    c = Canvas(n, n)
    inner = hexc("1b1e2c", 242)
    c.rect(1, 1, n - 2, n - 2, inner)
    frame(c, 0, 0, n, n, INK)
    frame(c, 1, 1, n - 2, n - 2, GOLD[2])
    c.rect(2, 1, n - 4, 1, GOLD[1])           # light from above
    c.rect(2, n - 2, n - 4, 1, GOLD[3])       # shade below
    c.rect(1, 2, 1, n - 4, GOLD[2])
    c.rect(n - 2, 2, 1, n - 4, GOLD[3])
    frame(c, 2, 2, n - 4, n - 4, INK)
    c.rect(3, 3, n - 6, 1, hexc("2a2f45", 242))  # inner top bevel
    for (x, y) in [(1, 1), (n - 4, 1), (1, n - 4), (n - 4, n - 4)]:
        c.rect(x, y, 3, 3, GOLD[2])
        c.px(x + 1, y + 1, GOLD[0])
        c.px(x + 2, y + 2, GOLD[3])
    return c


def panel_soft():
    """Section inside a panel: recessed slate with a thin stone edge. Margin 4."""
    n = 12
    c = Canvas(n, n)
    c.rect(0, 0, n, n, hexc("141622", 235))
    frame(c, 0, 0, n, n, hexc("0b0c14"))
    c.rect(1, n - 1, n - 2, 1, hexc("343a55"))  # lit lower lip: reads as sunk in
    return c


def tooltip():
    """Parchment note with a brown edge. Margin 4."""
    n = 12
    c = Canvas(n, n)
    c.rect(1, 1, n - 2, n - 2, hexc("f3e4bd"))
    frame(c, 0, 0, n, n, INK)
    frame(c, 1, 1, n - 2, n - 2, hexc("c9a66b"))
    c.rect(2, 2, n - 4, 1, hexc("fff6dc"))
    return c


# ---------- buttons ----------

BTN = {
    "normal": (hexc("3d4870"), hexc("5d6fa6"), hexc("29304e")),
    "hover": (hexc("4b5a8c"), hexc("7d92d0"), hexc("323a5e")),
    "pressed": (hexc("2c3454"), hexc("222842"), hexc("3d4870")),
    "disabled": (hexc("33343f"), hexc("444555"), hexc("26262f")),
}


def button(state):
    """Beveled slate button with a gold hairline on hover. Margin 4."""
    n = 12
    body, light, shade = BTN[state]
    c = Canvas(n, n)
    c.rect(1, 1, n - 2, n - 2, body)
    frame(c, 0, 0, n, n, INK)
    if state == "pressed":
        c.rect(1, 1, n - 2, 2, light)       # pushed in: shadow on top
        c.rect(1, n - 2, n - 2, 1, shade)
    else:
        c.rect(1, 1, n - 2, 1, light)
        c.rect(1, 1, 1, n - 3, light)
        c.rect(1, n - 3, n - 2, 2, shade)
        c.rect(n - 2, 2, 1, n - 4, shade)
    if state == "hover":
        for x, y in [(0, 0), (n - 1, 0), (0, n - 1), (n - 1, n - 1)]:
            c.px(x, y, GOLD[1])
    for x, y in [(0, 0), (n - 1, 0), (0, n - 1), (n - 1, n - 1)]:
        if state != "hover":
            c.px(x, y, (0, 0, 0, 0))   # rounded corners
    return c


def field():
    """Text input: sunk dark well. Margin 4."""
    n = 12
    c = Canvas(n, n)
    c.rect(1, 1, n - 2, n - 2, hexc("0f1019"))
    frame(c, 0, 0, n, n, INK)
    c.rect(1, 1, n - 2, 1, hexc("07070c"))
    c.rect(1, n - 2, n - 2, 1, hexc("2b3048"))
    return c


def slider_track():
    c = Canvas(8, 6)
    c.rect(0, 1, 8, 4, INK)
    c.rect(1, 2, 6, 2, hexc("0f1019"))
    return c


def slider_fill():
    c = Canvas(8, 6)
    c.rect(0, 1, 8, 4, INK)
    c.rect(1, 2, 6, 1, GOLD[1])
    c.rect(1, 3, 6, 1, GOLD[2])
    return c


def grabber(hot=False):
    c = Canvas(8, 10)
    c.rect(1, 1, 6, 8, GOLD[1] if hot else GOLD[2])
    c.rect(1, 1, 6, 1, GOLD[0])
    c.rect(1, 8, 6, 1, GOLD[3])
    c.rect(3, 3, 2, 4, GOLD[3])
    c.outline()
    return c


def scroll_track():
    c = Canvas(6, 6)
    c.rect(0, 0, 6, 6, hexc("0f1019", 200))
    return c


def scroll_grab(hot=False):
    c = Canvas(6, 8)
    c.rect(1, 0, 4, 8, BTN["hover" if hot else "normal"][1])
    c.rect(2, 1, 2, 6, BTN["hover" if hot else "normal"][0])
    return c


def arrow():
    c = Canvas(8, 8)
    c.ascii([
        "........",
        "........",
        "#######.",
        ".#ooo#..",
        "..#o#...",
        "...#....",
        "........",
        "........",
    ], {"#": INK, "o": GOLD[1]})
    return c


def check(on):
    c = Canvas(9, 9)
    c.rect(0, 0, 9, 9, INK)
    c.rect(1, 1, 7, 7, hexc("0f1019"))
    if on:
        c.ascii([
            ".......",
            "......o",
            ".....oo",
            "o...oo.",
            "oo.oo..",
            ".ooo...",
            "..o....",
        ], {"o": GOLD[1]}, 1, 1)
    return c


# ---------- resource icons (10x10) ----------

ICONS = {
    "grain": ([
        "y..y..y...",
        "Yy.Yy.Yy..",
        ".Y.YY.Y...",
        "..YYYY....",
        "...YY.....",
        "..rrrr....",
        "...YY.....",
        "..Y.YY....",
        ".Y..Y.Y...",
        "Y...Y..Y..",
    ], {"y": P["straw"][0], "Y": P["straw"][1], "r": P["red"][2]}),
    "timber": ([
        "..........",
        "..........",
        "..wwwwwoo.",
        ".wwwwwoRRo",
        "wWWWWWoRoR",
        "wWWWWWoRRo",
        "dddddd.oo.",
        "..........",
        "..........",
        "..........",
    ], {"w": P["wood"][0], "W": P["wood"][1], "d": P["wood"][2], "R": P["leather"][1], "o": P["leather"][0]}),
    "stone": ([
        "..........",
        "...ss.....",
        "..sSSs....",
        ".sSSSSs...",
        ".SSSSSdss.",
        "sSSSSddSSs",
        "SSSSdddSSd",
        "dddddddddd",
        "..........",
        "..........",
    ], {"s": P["stone"][0], "S": P["stone"][1], "d": P["stone"][2]}),
    "lumen": ([
        "....w.....",
        "...wlL....",
        "...wlL....",
        "..wllLL...",
        "..wllLL...",
        ".wlllLLd..",
        "..llLLd...",
        "...lLd....",
        "....d.....",
        "..........",
    ], {"w": P["lumen"][0], "l": P["lumen"][1], "L": P["lumen"][2], "d": P["lumen"][3]}),
    "gold": ([
        "..........",
        "...gggg...",
        "..gGGGGg..",
        ".gGgGGGGd.",
        ".gGgGGGGd.",
        ".gGgGGGGd.",
        ".gGGGGGGd.",
        "..dGGGGd..",
        "...dddd...",
        "..........",
    ], {"g": GOLD[0], "G": GOLD[1], "d": GOLD[2]}),
    "pop": ([
        "...ss.....",
        "..ssss....",
        "..ssss.ss.",
        "...ss.ssss",
        ".bbbb.ssss",
        "bbbbbb.ss.",
        "bbbbbbrrrr",
        "bbbbbrrrrr",
        "bbbbbrrrrr",
        "..........",
    ], {"s": P["skin"][1], "b": P["blue"][1], "r": P["red"][1]}),
    "crown": ([
        "..........",
        "g...g...g.",
        "gg.ggg.gg.",
        "gGgGGGgGg.",
        "gGGGrGGGg.",
        "gGGrrrGGg.",
        "gGGGGGGGg.",
        "dddddddddd",
        "..........",
        "..........",
    ], {"g": GOLD[0], "G": GOLD[1], "d": GOLD[2], "r": P["red"][1]}),
    "sun": ([
        "....y.....",
        ".y..y..y..",
        "..yYYYy...",
        "..YYOYY...",
        "yyYOOOYyy.",
        "..YYOYY...",
        "..yYYYy...",
        ".y..y..y..",
        "....y.....",
        "..........",
    ], {"y": GOLD[1], "Y": GOLD[0], "O": P["orange"][0]}),
    "moon": ([
        "...mmm....",
        "..mMm.....",
        ".mMm......",
        ".mMm......",
        ".mMm......",
        ".mMMm...d.",
        "..mMMmmm..",
        "...mmmm...",
        "..........",
        "..........",
    ], {"m": hexc("c9d4ff"), "M": hexc("eef2ff"), "d": hexc("c9d4ff")}),
    "star": ([
        "....g.....",
        "....g.....",
        "...gGg....",
        "ggggGgggg.",
        ".gGGGGGg..",
        "..gGGGg...",
        "..gGgGg...",
        ".gg...gg..",
        "..........",
        "..........",
    ], {"g": GOLD[1], "G": GOLD[0]}),
    "mind": ([
        "..pppp....",
        ".pPPPPp...",
        "pPPpPPPp..",
        "pPpPPpPp..",
        "pPPPpPPp..",
        ".pPPPPp...",
        "..pPPp....",
        "...dd.....",
        "...dd.....",
        "..........",
    ], {"p": P["purple"][1], "P": P["purple"][0], "d": P["purple"][2]}),
}


def icons():
    for name, (rows, legend) in ICONS.items():
        c = Canvas(12, 12)
        c.ascii(rows, legend, 1, 1)
        c.outline()
        save2x(c, f"icon_{name}")


if __name__ == "__main__":
    save2x(panel(), "panel")
    save2x(panel_soft(), "panel_soft")
    save2x(tooltip(), "tooltip")
    for s in BTN:
        save2x(button(s), f"button_{s}")
    save2x(field(), "field")
    save2x(slider_track(), "slider_track")
    save2x(slider_fill(), "slider_fill")
    save2x(grabber(), "grabber")
    save2x(grabber(True), "grabber_hot")
    save2x(scroll_track(), "scroll_track")
    save2x(scroll_grab(), "scroll_grab")
    save2x(scroll_grab(True), "scroll_grab_hot")
    save2x(arrow(), "arrow")
    save2x(check(False), "check_off")
    save2x(check(True), "check_on")
    icons()
    print("ui done")
