"""Human subjects: a layered 16x16 paper doll. Body + headgear + tool are drawn per direction and frame,
then outlined together, so a fused mind can wear one parent's hat and the other's tool."""
from lib import P, Canvas, preview, sheet

# ---------- body ----------
# s skin  S skin shade  e eye  w eye shine  h hair  H hair shade  l tunic light  t tunic  d tunic dark
# b belt  p pants  P pants shade  f boots  F boot shade
HEAD = {
    "down": [
        "....hhhhhh......",
        "...hhhhhhhh.....",
        "...hhhhhhhhh....",
        "...hhssssssh....",
        "...hsssssssh....",
        "...sswesswes....",
        "...ssssssssS....",
        "....SssssSS.....",
    ],
    "up": [
        "....hhhhhh......",
        "...hhhhhhhh.....",
        "...hhhhhhhhh....",
        "...hhhhhhhhh....",
        "...hhhhhhhhh....",
        "...Hhhhhhhhh....",
        "...HHhhhhhHH....",
        "....SssssSS.....",
    ],
    "side": [
        "....hhhhhh......",
        "...hhhhhhhh.....",
        "...hhhhhhhhh....",
        "...hhhhhssss....",
        "...hhhhsssss....",
        "...hhhssssews...",
        "...Hhhsssssss...",
        "....HSssssS.....",
    ],
}

TORSO = {
    "down": [
        "....lttttttd....",
        "...sltttttdds...",
        "...Sltttttdds...",
        "....bbbbbbbb....",
    ],
    "up": [
        "....dtttttdd....",
        "...sdttttttds...",
        "...Sdttttttds...",
        "....bbbbbbbb....",
    ],
    "side": [
        ".....lttttd.....",
        ".....lttsstd....",
        ".....lttSstd....",
        ".....bbbbbbb....",
    ],
}

# Legs per walk phase: 0 = standing, 1 = left foot forward, 2 = right foot forward.
LEGS = {
    "down": [
        ["....pp..PP.....", "....pp..PP.....", "....ff..FF....."],
        ["....pp..PP.....", "....pp..FF.....", "....ff........."],
        ["....pp..PP.....", "....ff..PP.....", "........FF....."],
    ],
    "up": [
        ["....pp..PP.....", "....pp..PP.....", "....ff..FF....."],
        ["....pp..PP.....", "....pp..FF.....", "....ff........."],
        ["....pp..PP.....", "....ff..PP.....", "........FF....."],
    ],
    "side": [
        [".....ppPP......", ".....ppPP......", ".....ffFF......"],
        [".....pp.PP.....", "....pp...PP....", "....ff....FF..."],
        [".....pPpP......", "......pP.......", "......fF......."],
    ],
}

# Walk cycle: (leg phase, body bob). Idle: two breathing frames.
WALK = [(1, 0), (0, -1), (2, 0), (0, -1)]
IDLE = [(0, 0), (0, 1)]


def legend(team, hair="hair", skin="skin"):
    s, h, tm = P[skin], P[hair], P[team]
    return {
        "s": s[0], "S": s[1], "e": P["outline"], "w": P["white"][0],
        "h": h[0], "H": h[1],
        "l": tm[0], "t": tm[1], "d": tm[2],
        "b": P["leather"][2], "p": P["dark"][0], "P": P["dark"][1], "f": P["leather"][1], "F": P["leather"][2],
    }


def body(direction, phase, bob, team, hair="hair"):
    """Draws an un-outlined body. Returns the canvas and the head's top y (for headgear)."""
    key = "side" if direction in ("left", "right") else direction
    c = Canvas(16, 16)
    lg = legend(team, hair)
    head_y = 1 + bob
    c.ascii(HEAD[key], lg, 0, head_y)
    c.ascii(TORSO[key], lg, 0, head_y + 8)
    c.ascii(LEGS[key][phase], lg, 0, 13)
    return c, head_y


def frame(direction, phase, bob, team, hair="hair", extras=()):
    c, head_y = body(direction, phase, bob, team, hair)
    for draw in extras:
        draw(c, direction, head_y, phase)
    c.outline()
    if direction == "left":
        flipped = Canvas(16, 16)
        flipped.a = c.a[:, ::-1].copy()
        return flipped
    return c


def all_frames(team, hair="hair", extras=()):
    """Rows: down, up, right, left. Columns: idle0, idle1, walk0..3."""
    frames = []
    for d in ("down", "up", "right", "left"):
        for ph, bob in IDLE + WALK:
            frames.append(frame(d, ph, bob, team, hair, extras))
    return frames


if __name__ == "__main__":
    s = sheet(all_frames("blue"), 6)
    preview(s, "body_v1", 8)


import gear

ROLES = ["Farmer", "Builder", "Guard", "Scout", "Thief", "Hunter", "Healer", "Hoarder"]


def view_of(direction):
    return "side" if direction in ("left", "right") else direction


def outfit(hat=None, tool=None, team="blue", ruler=False):
    """Extras that dress the body: a hat from one role, a tool from another (fusions mix them)."""
    ramp = P[team]

    def draw_hat(c, d, head_y, phase):
        if hat is None:
            return
        rows, dx, dy = gear.HATS[hat][view_of(d)]
        gear.draw(c, rows, dx, dy, head_y, ramp)

    def draw_tool(c, d, head_y, phase):
        if tool is None:
            return
        rows, dx, dy = gear.TOOLS[tool][view_of(d)]
        swing = 1 if phase == 1 else -1 if phase == 2 else 0
        gear.draw(c, rows, dx, dy + (swing if tool != "Hoarder" else 0), head_y, ramp)

    def draw_cape(c, d, head_y, phase):
        if not ruler:
            return
        v = view_of(d)
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
        rows, dx, dy = gear.HATS["Crown"][v]
        gear.draw(c, rows, dx, dy, head_y, ramp)

    return [draw_cape, draw_hat, draw_tool]


if __name__ == "__main__":
    frames = []
    for role in ROLES + ["Ruler"]:
        ex = outfit(None, None, "red", True) if role == "Ruler" else outfit(role, role, "blue")
        for d in ("down", "up", "right", "left"):
            frames.append(frame(d, 0, 0, "blue", "blond" if role in ("Scout", "Healer") else "hair", ex))
        for ph, bob in WALK:
            frames.append(frame("right", ph, bob, "blue", "hair", ex))
    preview(sheet(frames, 8), "roles_v1", 6)
