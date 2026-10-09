"""Wild species. Each has front/back/side ASCII frames; animation comes from frame variants and bob."""
from lib import P, Canvas, hexc, preview, sheet

# ---------- Grove Sprite: a tiny leaf-winged fairy that glows ----------
SPRITE_LEGEND = {
    "l": P["lumen"][0], "L": P["lumen"][1], "i": P["lumen"][2],
    "e": P["outline"], "g": P["wild"][0], "G": P["wild"][1], "x": P["wild"][2],
    "w": hexc("d8f5b0"), "W": hexc("9fd977"),
}
SPRITE = {
    "down": [[
        "................",
        "................",
        ".......ll.......",
        "......lLLl......",
        "......LeLe......",
        ".......LL.......",
        "..ww..gGGg..ww..",
        ".wWWw.GGGG.wWWw.",
        ".wWWW.GGGG.WWWw.",
        "..wW..xGGx..Ww..",
        ".......gg.......",
        ".......g.g......",
    ], [
        "................",
        "................",
        ".......ll.......",
        "......lLLl......",
        "......LeLe......",
        "..w....LL....w..",
        ".wWw..gGGg..wWw.",
        ".wWWw.GGGG.wWWw.",
        "..wW..GGGG..Ww..",
        "......xGGx......",
        ".......gg.......",
        ".......g.g......",
    ]],
    "up": [[
        "................",
        "................",
        ".......ll.......",
        "......lLLl......",
        "......LLLL......",
        ".......LL.......",
        "..ww..gGGg..ww..",
        ".wWWw.GGGG.wWWw.",
        ".wWWW.GGGG.WWWw.",
        "..wW..xGGx..Ww..",
        ".......gg.......",
        ".......g.g......",
    ], [
        "................",
        "................",
        ".......ll.......",
        "......lLLl......",
        "......LLLL......",
        "..w....LL....w..",
        ".wWw..gGGg..wWw.",
        ".wWWw.GGGG.wWWw.",
        "..wW..GGGG..Ww..",
        "......xGGx......",
        ".......gg.......",
        ".......g.g......",
    ]],
    "side": [[
        "................",
        "................",
        ".......ll.......",
        "......lLLL......",
        "......LLLe......",
        ".......LL.......",
        "...ww.gGG.......",
        "..wWWwGGGg......",
        "..wWWWGGGG......",
        "...wW.xGGx......",
        ".......gg.......",
        "........g.......",
    ], [
        "................",
        "................",
        ".......ll.......",
        "......lLLL......",
        "...w..LLLe......",
        "..wWw..LL.......",
        "..wWWwgGG.......",
        "...wWWGGGg......",
        "......GGGG......",
        "......xGGx......",
        ".......gg.......",
        "........g.......",
    ]],
}

# ---------- Stone Troll: big, slow, territorial (24x24) ----------
TROLL_LEGEND = {
    "t": hexc("a3b38f"), "T": hexc("7a8c6c"), "d": hexc("566650"), "D": hexc("3b4738"),
    "y": hexc("ffe26a"), "a": P["white"][0], "e": P["leather"][1], "E": P["leather"][2],
    "s": P["stone"][1], "S": P["stone"][2],
}
TROLL = {
    "down": [
        "........................",
        "..........tttt..........",
        "........ttTTTTdd........",
        ".......tTTTTTTTTd.......",
        ".......TyyTTTTyyd.......",
        ".......TTTTTTTTTd.......",
        ".......TaTTddTTad.......",
        "........TTddddTd........",
        "....tttTTTTTTTTTTddd....",
        "...tTTTTTTTTTTTTTTTTd...",
        "..tTTTTTTTTTTTTTTTTTTd..",
        "..TTTT.TTTTTTTTTT.TTTd..",
        "..TTT..TTTTTTTTTT..TTd..",
        "..TTd..TTTTTTTTTd..TTd..",
        ".tTTd..eeeeeeeeee..dTTd.",
        ".dTd...EeeeeeeeeE...dTd.",
        ".......TTTTd.TTTTd......",
        ".......TTTTd.TTTTd......",
        "......dDDDD..DDDDd......",
    ],
    "up": [
        "........................",
        "..........tttt..........",
        "........ttTTTTdd........",
        ".......tTTTTTTTTd.......",
        ".......TTTTTTTTTd.......",
        ".......TTTTTTTTTd.......",
        ".......TTTTTTTTTd.......",
        "........TTTTTTTd........",
        "....tttTTTTTTTTTTddd....",
        "...tTTTTTTTTTTTTTTTTd...",
        "..tTTTTTTTsTTTTTTTTTTd..",
        "..TTTT.TTTsSTTTTTT.TTd..",
        "..TTT..TTTTsTTTTTT..TTd.",
        "..TTd..TTTTTTTTTTd..TTd.",
        ".tTTd..eeeeeeeeee..dTTd.",
        ".dTd...EeeeeeeeeE...dTd.",
        ".......TTTTd.TTTTd......",
        ".......TTTTd.TTTTd......",
        "......dDDDD..DDDDd......",
    ],
    "side": [
        "........................",
        "...........tttt.........",
        ".........ttTTTTd........",
        "........tTTTTTTTd.......",
        "........TTTTTTyyT.......",
        "........TTTTTTTTTT......",
        "........dTTTTTTaTT......",
        ".........dTTTTddd.......",
        "......tttTTTTTTTd.......",
        ".....tTTTTTTTTTTTd......",
        "....tTTTTTTTTTTTTTd.....",
        "....TTTTTTTTTTTTTTd.....",
        "....TTTTTTTTTTTTTTd.....",
        "....dTTTTTTTTTTTTd......",
        ".....eeeeeeeeeeeeTTd....",
        ".....EeeeeeeeeeeEdTd....",
        "......TTTTd..TTTd.......",
        "......TTTTd..TTTd.......",
        ".....dDDDD...DDDDd......",
    ],
}

# ---------- Mire Fox: a sly upright fox ----------
FOX_LEGEND = {
    "o": P["orange"][0], "O": P["orange"][1], "q": P["orange"][2], "a": P["white"][0], "A": P["white"][1],
    "e": P["outline"], "k": P["dark"][1], "n": hexc("2a2533"),
}
FOX = {
    "down": [
        "................",
        "...O.......O....",
        "...OO.....OO....",
        "...OOOOOOOOO....",
        "...OOeOOOeOO....",
        "....OaaaaaO.....",
        ".....aanaa......",
        "......aaa.......",
        "....OOaaaOO.....",
        "...OOOaaaOOO....",
        "...qOOaaaOOq.qq.",
        "....OOOOOOO.qOOq",
        "....qOO.OOq..qOa",
        "....kk...kk...a.",
    ],
    "up": [
        "................",
        "...O.......O....",
        "...OO.....OO....",
        "...OOOOOOOOO....",
        "...OOOOOOOOO....",
        "....OOOOOOO.....",
        ".....OOOOO......",
        "......OOO.......",
        "....OOOOOOO.....",
        "...OOOOOOOOO....",
        "...qOOOOOOOq....",
        "....OOOOOOO.....",
        "....qOOqOOq.....",
        "....kk.OOkk.....",
        ".......OOa......",
        "........aa......",
    ],
    "side": [
        "................",
        "......O.O.......",
        "......OOOO......",
        "......OOOOO.....",
        ".....OOOOeOO....",
        ".....OOOOaaaan..",
        "......OOaaaa....",
        ".......Oaa......",
        "......OOaaO.....",
        "..qq.OOOaaOO....",
        ".qOOqOOOaaO.....",
        ".aOOqOOOOOO.....",
        "..aa..OOOOq.....",
        "......kk.kk.....",
    ],
}

# ---------- Lumen Moth: fluffy, drifting, with glowing wing spots ----------
MOTH_LEGEND = {
    "f": P["white"][0], "F": P["white"][1], "B": P["white"][2], "l": P["lumen"][0], "L": P["lumen"][1], "i": P["lumen"][2],
    "e": P["outline"], "w": hexc("cfc6e8"), "W": hexc("9c90c4"), "v": hexc("6d6296"),
}
MOTH = {
    "down": [[
        "................",
        ".....e....e.....",
        "......e..e......",
        "......ffff......",
        ".....fefFef.....",
        "wwW...FFFF...Www",
        "wlLW.fFFFFf.WLlw",
        "wLiWWfFFFFfWWiLw",
        "wWWWWfFFFFfWWWWw",
        ".vWW.fFFFFf.WWv.",
        "..v...FBBF...v..",
        "......BBBB......",
        ".......BB.......",
    ], [
        "................",
        ".....e....e.....",
        "......e..e......",
        "......ffff......",
        "..wW.fefFef.Ww..",
        ".wlLW.FFFF.WLlw.",
        ".wLiWfFFFFfWiLw.",
        ".wWWWfFFFFfWWWw.",
        "..vWWfFFFFfWWv..",
        "...v.fFFFFf.v...",
        "......FBBF......",
        "......BBBB......",
        ".......BB.......",
    ]],
    "side": [[
        "................",
        "........e.e.....",
        ".........ee.....",
        "........ffff....",
        "...ww...fFef....",
        "..wlLW..FFFF....",
        "..wLiWWfFFFFf...",
        "..wWWWWfFFFFf...",
        "...vWWWfFFFFf...",
        "....vv.fFFFFf...",
        "........FBBF....",
        "........BBBB....",
        ".........BB.....",
    ], [
        "................",
        "........e.e.....",
        ".........ee.....",
        "........ffff....",
        "........fFef....",
        "........FFFF....",
        "...wwWWfFFFFf...",
        "..wlLWWfFFFFf...",
        "..wLiWWfFFFFf...",
        "...vWWvfFFFFf...",
        "....vv..FBBF....",
        "........BBBB....",
        ".........BB.....",
    ]],
}

# ---------- Hermit Sage: an old robed hermit with a long beard ----------
SAGE_LEGEND = {
    "r": hexc("8a96b8"), "R": hexc("626e94"), "q": hexc("444e6e"), "s": P["skin"][0], "S": P["skin"][1],
    "b": P["white"][0], "B": P["white"][1], "e": P["outline"], "w": P["wood"][1], "l": P["lumen"][0], "L": P["lumen"][1],
}
SAGE = {
    "down": [
        "...........lL...",
        "....rrrrr..Ll...",
        "...rRRRRRr..w...",
        "...RRsssRRq.w...",
        "...RsesesRq.w...",
        "...RsBBBsRq.w...",
        "....bbbbbb..w...",
        "...rbbbbbbRqw...",
        "...rRbbbbRRqw...",
        "...rRRbbRRRq....",
        "...rRRRRRRRq....",
        "...rRRRRRRRq....",
        "...RRRRRRRRRq...",
        "....qq...qq.....",
    ],
    "up": [
        "....rrrrr.......",
        "...rRRRRRr..lL..",
        "...RRRRRRRq.Ll..",
        "...RRRRRRRq..w..",
        "...RRRRRRRq..w..",
        "...qRRRRRqq..w..",
        "...rRRRRRRq..w..",
        "...rRRRRRRq..w..",
        "...rRRRRRRq.....",
        "...rRRRRRRq.....",
        "...rRRRRRRq.....",
        "...RRRRRRRRq....",
        "....qq...qq.....",
    ],
    "side": [
        "............lL..",
        ".....rrrrr..Ll..",
        "....rRRRRRr..w..",
        "....RRRsssr..w..",
        "....RRRsess..w..",
        "....RRRsBBB..w..",
        "....RRRbbbbb.w..",
        "....rRRRbbbb.w..",
        "....rRRRRbb..w..",
        "....rRRRRRRq....",
        "....rRRRRRRq....",
        "....rRRRRRRq....",
        "....RRRRRRRRq...",
        ".....qq..qq.....",
    ],
}


def recolor(legend, mapping):
    out = dict(legend)
    out.update(mapping)
    return out


ABERRATION = recolor(TROLL_LEGEND, {"t": hexc("c69be8"), "T": hexc("8f5ccc"), "d": hexc("5e3596"), "D": hexc("3a1f66"), "y": hexc("ff5a6a")})
SEALED = recolor(TROLL_LEGEND, {"t": hexc("5a5266"), "T": hexc("3c3548"), "d": hexc("29233a"), "D": hexc("181424"), "y": hexc("ffd84a"), "e": P["gold"][1], "E": P["gold"][2]})


def make(rows, legend, size, bob=0, flip=False):
    c = Canvas(size, size)
    oy = size - len(rows) - 1 + bob
    c.ascii(rows, legend, (size - max(len(r) for r in rows)) // 2, oy, flip)
    c.outline()
    return c


def frames_for(kind):
    """Rows down, up, right, left x columns idle0, idle1, walk0..3 (same layout as humans)."""
    if kind == "Sprite":
        views, legend, size = SPRITE, SPRITE_LEGEND, 16
    elif kind == "Moth":
        views, legend, size = {"down": MOTH["down"], "up": MOTH["down"], "side": MOTH["side"]}, MOTH_LEGEND, 16
    elif kind in ("Troll", "Aberration", "Sealed"):
        views, legend, size = TROLL, {"Troll": TROLL_LEGEND, "Aberration": ABERRATION, "Sealed": SEALED}[kind], 24
    elif kind == "Fox":
        views, legend, size = FOX, FOX_LEGEND, 16
    else:
        views, legend, size = SAGE, SAGE_LEGEND, 16
    out = []
    for d in ("down", "up", "right", "left"):
        v = views["side" if d in ("left", "right") else d]
        flip = d == "left"
        variants = v if isinstance(v[0], list) else [v]
        for i in range(6):
            rows = variants[i % len(variants)]
            hover = kind in ("Sprite", "Moth")
            bob = (-1 if (i // 2) % 2 else 0) if hover else (-1 if i in (3, 5) else 0)
            out.append(make(rows, legend, size, bob, flip))
    return out


if __name__ == "__main__":
    for kind in ("Sprite", "Troll", "Fox", "Moth", "Sage", "Aberration", "Sealed"):
        preview(sheet(frames_for(kind), 6), f"creature_{kind}", 6)
