"""Buildings in a 3/4 top-down view. Each image's bottom edge sits on the building's footprint bottom."""
import random

from lib import P, Canvas, hexc, preview, sheet, ASSETS

STONE = [hexc("d4d1dc"), hexc("aeabbd"), hexc("8a8799"), hexc("66637a"), hexc("4a4760")]
DARKSTONE = [hexc("8a8698"), hexc("6a6680"), hexc("4e4a62"), hexc("38354a"), hexc("26233a")]
WOOD = P["wood"]
ROOF = {
    "blue": [hexc("7fa6e8"), hexc("5079cc"), hexc("3a589e"), hexc("283d70")],
    "red": [hexc("ef8a76"), hexc("cf5848"), hexc("a03a38"), hexc("6e262c")],
    "wild": [hexc("b49a72"), hexc("8e7550"), hexc("6a5638"), hexc("4a3c28")],
}
TEAM = {"blue": P["blue"], "red": P["red"], "wild": P["wild"]}
SHADOW = (15, 25, 20, 110)


def shadow(c, cx, cy, rx, ry):
    c.ellipse(cx, cy, rx, ry, SHADOW)


def brick_wall(c, x, y, w, h, ramp=STONE):
    c.rect(x, y, w, h, ramp[2])
    for yy in range(y, y + h):
        row = (yy - y) // 3
        for xx in range(x, x + w):
            if (yy - y) % 3 == 2 or (xx - x + (row % 2) * 3) % 6 == 5:
                c.px(xx, yy, ramp[3])
            elif (yy - y) % 3 == 0:
                c.px(xx, yy, ramp[1])
    c.rect(x, y, w, 1, ramp[0])
    c.rect(x, y + h - 1, w, 1, ramp[4])


def plank_wall(c, x, y, w, h):
    c.rect(x, y, w, h, WOOD[1])
    for xx in range(x, x + w):
        if (xx - x) % 4 == 3:
            for yy in range(y, y + h):
                c.px(xx, yy, WOOD[2])
        elif (xx - x) % 4 == 0:
            for yy in range(y, y + h):
                c.px(xx, yy, WOOD[0])
    c.rect(x, y + h - 1, w, 1, WOOD[3])


def gable_roof(c, x, y, w, h, ramp, overhang=2):
    """A front-facing roof: shingle rows, darker toward the eaves, a light ridge on top."""
    for yy in range(h):
        inset = max(0, (h - yy - 1) * (w // 2) // max(1, h) - 0) if False else 0
        for xx in range(-overhang, w + overhang):
            shade = 1 if yy < h * 0.5 else 2
            if (xx + (yy // 2) * 2) % 4 == 0 and yy % 2 == 1:
                shade += 1
            c.px(x + xx, y + yy, ramp[min(3, shade)])
    c.rect(x - overhang, y, w + overhang * 2, 1, ramp[0])
    c.rect(x - overhang, y + h, w + overhang * 2, 1, ramp[3])


def peaked_roof(c, x, y, w, h, ramp):
    """A triangular roof seen from the front-top."""
    for yy in range(h):
        half = (yy + 1) * (w / 2) / h
        for xx in range(int(w / 2 - half), int(w / 2 + half) + 1):
            left = xx < w / 2
            shade = 1 if left else 2
            if yy % 3 == 2:
                shade += 1
            c.px(x + xx, y + yy, ramp[min(3, shade)])
    c.line(x + w // 2, y, x, y + h - 1, ramp[0])


def door(c, x, y, w=4, h=6, col=None):
    col = col or WOOD[3]
    c.rect(x, y + 1, w, h - 1, col)
    c.rect(x + 1, y, w - 2, 1, col)
    c.px(x + w - 2, y + h // 2, P["gold"][1])


def window(c, x, y, lit=True):
    c.rect(x, y, 3, 3, hexc("ffd977") if lit else hexc("2a3550"))
    c.px(x + 1, y + 1, hexc("fff3c4") if lit else hexc("3c4a70"))
    c.rect(x, y + 3, 3, 1, STONE[3])


def banner(c, x, y, team, h=10):
    c.rect(x, y, 1, h, WOOD[2])
    c.px(x, y - 1, P["gold"][1])
    t = TEAM[team]
    for yy in range(4):
        for xx in range(1, 6 - (1 if yy in (0, 3) else 0)):
            c.px(x + xx, y + 1 + yy, t[1] if yy < 2 else t[2])
    c.px(x + 2, y + 2, P["gold"][0])


def crenellations(c, x, y, w, ramp=STONE):
    for xx in range(x, x + w, 4):
        c.rect(xx, y - 3, 2, 3, ramp[1])
        c.px(xx, y - 3, ramp[0])


def keep(team):
    c = Canvas(72, 80)
    shadow(c, 36, 74, 34, 6)
    # side towers
    for tx in (2, 52):
        brick_wall(c, tx, 26, 18, 48)
        crenellations(c, tx, 26, 18)
        c.rect(tx + 7, 36, 4, 6, STONE[4])
        window(c, tx + 7, 52)
        peaked_roof(c, tx - 1, 6, 20, 18, ROOF[team])
    # main hall
    brick_wall(c, 16, 38, 40, 36)
    crenellations(c, 16, 38, 40)
    door(c, 30, 60, 12, 14, WOOD[3])
    c.rect(30, 60, 12, 1, STONE[1])
    for wx in (21, 46):
        window(c, wx, 46)
    banner(c, 35, 14, team, 22)
    c.outline()
    return c


def house(team):
    c = Canvas(32, 36)
    shadow(c, 16, 33, 14, 3)
    plank_wall(c, 5, 18, 22, 14)
    door(c, 14, 25, 5, 7)
    window(c, 8, 22)
    window(c, 21, 22)
    peaked_roof(c, 2, 4, 28, 15, ROOF[team])
    c.rect(20, 4, 3, 6, STONE[2])  # chimney
    c.px(21, 3, hexc("cfcfcf"))
    c.outline()
    return c


def cradle(team):
    c = Canvas(44, 48)
    shadow(c, 22, 44, 19, 4)
    brick_wall(c, 6, 26, 32, 18)
    for ax in (8, 32):  # arch pillars
        brick_wall(c, ax, 8, 4, 20)
    c.rect(8, 6, 28, 3, STONE[1])
    c.rect(8, 6, 28, 1, STONE[0])
    # the glowing egg in a nest
    c.ellipse(22, 25, 9, 3, WOOD[2])
    c.ellipse(22, 24, 8, 2.5, WOOD[1])
    c.ellipse(22, 18, 5, 7, hexc("f4efe0"))
    c.ellipse(21, 16, 3, 4, hexc("ffffff"))
    for y in (15, 19):
        c.px(24, y, P["lumen"][1]); c.px(20, y + 2, P["lumen"][1])
    banner(c, 3, 14, team, 14)
    c.outline()
    return c


def camp(team):
    c = Canvas(32, 32)
    shadow(c, 16, 29, 14, 3)
    # tent
    for yy in range(14):
        half = (yy + 1) * 8 / 14
        for xx in range(int(12 - half), int(12 + half) + 1):
            c.px(xx, 12 + yy, TEAM[team][1] if xx < 12 else TEAM[team][2])
    c.rect(11, 20, 2, 6, P["dark"][2])
    # logs and stones
    for y in (22, 25):
        c.rect(20, y, 9, 2, WOOD[1]); c.px(20, y, WOOD[0])
    c.ellipse(25, 18, 3, 2, STONE[1])
    c.outline()
    return c


def altar(team):
    c = Canvas(48, 44)
    shadow(c, 24, 38, 22, 5)
    c.ellipse(24, 32, 20, 8, DARKSTONE[2])
    c.ellipse(24, 31, 17, 6, DARKSTONE[1])
    # rune ring
    for i in range(16):
        import math
        a = i / 16 * math.tau
        x, y = 24 + math.cos(a) * 14, 31 + math.sin(a) * 4.5
        c.px(int(x), int(y), P["purple"][0] if i % 2 else P["lumen"][1])
    # standing stones
    for sx in (6, 38):
        brick_wall(c, sx, 12, 5, 20, DARKSTONE)
        c.px(sx + 2, 16, P["purple"][0])
    # the altar slab with a swirl
    brick_wall(c, 17, 22, 14, 8, DARKSTONE)
    c.rect(16, 21, 16, 2, DARKSTONE[0])
    c.ellipse(24, 14, 5, 5, P["purple"][2])
    c.ellipse(24, 14, 3.5, 3.5, P["purple"][1])
    c.ellipse(23, 13, 1.5, 1.5, P["purple"][0])
    banner(c, 42, 4, team, 12)
    c.outline()
    return c


def grounds(team):
    c = Canvas(48, 36)
    shadow(c, 24, 33, 22, 3)
    c.rect(4, 14, 40, 18, hexc("c9a46b"))
    for x in range(4, 44, 2):
        c.px(x, 20 + (x % 5), hexc("b08a55"))
    # fence
    for x in range(3, 46, 6):
        c.rect(x, 10, 2, 22, WOOD[1])
    c.rect(3, 13, 42, 1, WOOD[0])
    c.rect(3, 29, 42, 1, WOOD[2])
    # training dummies
    for dx in (14, 30):
        c.rect(dx, 16, 2, 12, WOOD[2])
        c.ellipse(dx + 1, 15, 3, 3, P["straw"][1])
        c.rect(dx - 3, 19, 8, 2, P["straw"][2])
        c.px(dx, 14, P["red"][1])
    banner(c, 41, 0, team, 12)
    c.outline()
    return c


def watchtower(team):
    c = Canvas(28, 64)
    shadow(c, 14, 60, 11, 3)
    for lx in (6, 19):
        c.rect(lx, 24, 3, 36, WOOD[2]); c.rect(lx, 24, 1, 36, WOOD[1])
    for y in range(30, 58, 8):
        c.line(7, y, 20, y + 6, WOOD[3]); c.line(20, y, 7, y + 6, WOOD[3])
    plank_wall(c, 3, 14, 22, 11)
    c.rect(2, 12, 24, 2, WOOD[0])
    peaked_roof(c, 1, 0, 26, 12, ROOF[team])
    banner(c, 13, -2, team, 6) if False else None
    window(c, 12, 17)
    c.outline()
    return c


def dungeon(team):
    c = Canvas(48, 44)
    shadow(c, 24, 40, 22, 4)
    # a dark stone mound with an iron gate
    c.ellipse(24, 30, 22, 14, DARKSTONE[2])
    c.ellipse(23, 27, 19, 11, DARKSTONE[1])
    c.ellipse(21, 23, 12, 6, DARKSTONE[0])
    brick_wall(c, 14, 26, 20, 14, DARKSTONE)
    c.rect(18, 28, 12, 12, hexc("120f18"))
    for x in range(19, 30, 3):
        c.rect(x, 28, 1, 12, P["steel"][2])
    c.rect(18, 32, 12, 1, P["steel"][2])
    c.px(24, 24, hexc("ff4a4a"))
    banner(c, 40, 10, team, 14)
    c.outline()
    return c


def pit(team):
    c = Canvas(48, 40)
    shadow(c, 24, 34, 22, 4)
    c.ellipse(24, 26, 20, 10, DARKSTONE[3])
    c.ellipse(24, 25, 17, 8, hexc("3a1e4a"))
    c.ellipse(24, 25, 14, 6, hexc("6b2f8f"))
    for bx, by, r in ((18, 24, 2), (27, 26, 1.5), (23, 22, 1.2), (30, 23, 1)):
        c.ellipse(bx, by, r, r, hexc("b46be0"))
        c.px(int(bx - r / 2), int(by - r / 2), hexc("f0c8ff"))
    # bones and stakes around the rim
    for x, y in ((6, 22), (40, 22), (12, 32), (36, 32)):
        c.rect(x, y - 8, 2, 9, WOOD[2])
        c.px(x, y - 9, P["white"][1])
    c.rect(14, 32, 5, 1, P["white"][0]); c.px(13, 31, P["white"][0]); c.px(19, 33, P["white"][0])
    banner(c, 43, 4, team, 12)
    c.outline()
    return c


def spire(team):
    c = Canvas(40, 80)
    shadow(c, 20, 76, 16, 4)
    brick_wall(c, 8, 60, 24, 16, DARKSTONE)
    for yy in range(56):
        half = 2 + (yy / 56) * 8
        for xx in range(int(20 - half), int(20 + half) + 1):
            col = P["lumen"][1] if xx < 20 else P["lumen"][2]
            if (yy + xx) % 7 == 0:
                col = P["lumen"][0]
            c.px(xx, 6 + yy, col)
    c.ellipse(20, 6, 4, 4, P["lumen"][0])
    c.ellipse(20, 6, 2, 2, hexc("ffffff"))
    banner(c, 2, 50, team, 12)
    c.outline(hexc("123448"))
    return c


def market(team):
    c = Canvas(48, 36)
    shadow(c, 24, 33, 22, 3)
    for sx, col in ((3, TEAM[team]), (25, P["gold"])):
        c.rect(sx, 22, 20, 10, WOOD[1])
        c.rect(sx, 22, 20, 2, WOOD[0])
        for xx in range(sx - 1, sx + 21):
            stripe = ((xx - sx) // 3) % 2
            for yy in range(12, 18):
                c.px(xx, yy, col[1] if stripe else P["white"][1])
            c.px(xx, 18, col[2])
        for px_ in (sx, sx + 19):
            c.rect(px_, 12, 1, 12, WOOD[2])
    for x, col in ((6, hexc("e9c85a")), (10, hexc("e2504a")), (28, P["lumen"][1]), (33, P["gold"][1]), (38, hexc("9a7449"))):
        c.ellipse(x, 21, 2, 1.5, col)
    c.outline()
    return c


def infirmary(team):
    c = Canvas(44, 36)
    shadow(c, 22, 33, 20, 3)
    for yy in range(18):
        half = 4 + yy * 14 / 18
        for xx in range(int(22 - half), int(22 + half) + 1):
            c.px(xx, 12 + yy, P["white"][0] if xx < 22 else P["white"][1])
    c.rect(19, 22, 6, 8, P["dark"][2])
    c.rect(20, 14, 4, 2, P["red"][1]); c.rect(21, 13, 2, 4, P["red"][1])
    banner(c, 38, 6, team, 12)
    c.outline()
    return c


def ancient_altar(team):
    c = Canvas(40, 40)
    shadow(c, 20, 36, 18, 3)
    c.ellipse(20, 30, 16, 6, STONE[3])
    c.ellipse(20, 29, 13, 4.5, STONE[2])
    for px_, h, broken in ((5, 22, False), (31, 14, True), (13, 18, True), (25, 22, False)):
        brick_wall(c, px_, 32 - h, 5, h, STONE)
        if broken:
            c.px(px_ + 4, 32 - h, (0, 0, 0, 0))
    c.rect(4, 9, 27, 3, STONE[1]) if False else None
    for i, (x, y) in enumerate(((16, 27), (24, 27), (20, 25))):
        c.px(x, y, P["lumen"][1])
    for x, y in ((8, 26), (32, 22), (14, 20)):  # moss
        c.px(x, y, hexc("6fb04a")); c.px(x + 1, y, hexc("4a8a3c"))
    c.outline()
    return c


def merchant(team):
    c = Canvas(40, 32)
    shadow(c, 20, 29, 18, 3)
    plank_wall(c, 6, 14, 26, 11)
    for xx in range(4, 34):  # canvas cover
        for yy in range(6, 14):
            if (xx - 4) ** 2 / 225 + (yy - 14) ** 2 / 64 <= 1:
                c.px(xx, yy, P["white"][0] if yy < 10 else P["white"][1])
    for wx in (10, 27):
        c.ellipse(wx, 26, 3.5, 3.5, WOOD[3]); c.ellipse(wx, 26, 2, 2, WOOD[1]); c.px(wx, 26, WOOD[3])
    c.ellipse(36, 20, 3, 2, P["gold"][1])
    c.outline()
    return c


def den(team):
    c = Canvas(32, 28)
    shadow(c, 16, 25, 14, 3)
    c.ellipse(16, 19, 13, 8, hexc("6e5a3a"))
    c.ellipse(15, 17, 11, 6, hexc("8a7350"))
    c.ellipse(16, 21, 4, 3, hexc("1a1410"))
    for x, y in ((6, 15), (24, 14), (12, 12)):
        c.px(x, y, hexc("6fb04a")); c.px(x + 1, y - 1, hexc("9ad66a"))
    c.outline()
    return c


def site(frame):
    """Scaffolding for build sites; the planks fill in as frames advance (0..3)."""
    c = Canvas(40, 36)
    for px_ in (4, 18, 34):
        c.rect(px_, 6, 2, 26, WOOD[2])
    for yy in (10, 20, 30):
        c.rect(3, yy, 34, 2, WOOD[1])
    for k in range(frame * 3):
        c.rect(5 + (k % 3) * 10, 22 - (k // 3) * 10, 9, 8, WOOD[0] if k % 2 else WOOD[1])
    c.ellipse(30, 32, 4, 2, STONE[1])
    c.outline()
    return c


def farm(team):
    """A ploughed 3x3 field seen from above, fenced at the corners, with a scarecrow. Drawn flat on the ground."""
    c = Canvas(48, 48)
    soil = [hexc("8a5f3c"), hexc("75502f"), hexc("5e3f25")]
    rnd = random.Random(7)
    for y in range(2, 46):
        for x in range(2, 46):
            row = (y - 2) % 4
            col = soil[0] if row == 0 else soil[2] if row == 3 else soil[1]
            if rnd.random() < 0.06:
                col = soil[2]
            c.px(x, y, col)
    # rail fence on the border
    for x in range(1, 47):
        if x % 6 != 0:
            c.px(x, 1, WOOD[1]); c.px(x, 46, WOOD[2])
    for y in range(1, 47):
        if y % 6 != 0:
            c.px(1, y, WOOD[1]); c.px(46, y, WOOD[2])
    for (x, y) in ((0, 0), (45, 0), (0, 45), (45, 45), (23, 0), (23, 45)):
        c.rect(x, y, 3, 3, WOOD[0]); c.px(x + 1, y + 2, WOOD[3])
    # scarecrow in the top-left corner
    sx, sy = 9, 5
    c.rect(sx + 3, sy + 3, 1, 10, WOOD[2])
    c.rect(sx, sy + 6, 7, 1, WOOD[2])
    c.rect(sx + 1, sy + 5, 5, 4, TEAM[team][1])
    c.ellipse(sx + 3.5, sy + 3, 2, 2, P["straw"][1])
    c.rect(sx + 1, sy + 1, 5, 1, P["straw"][2])
    c.rect(sx + 2, sy, 3, 1, P["straw"][2])
    return c


def manor(team):
    c = Canvas(52, 52)
    shadow(c, 26, 48, 24, 4)
    brick_wall(c, 6, 24, 40, 24)
    plank_wall(c, 9, 16, 34, 9)
    door(c, 22, 38, 8, 10)
    for wx in (10, 16, 34, 40):
        window(c, wx, 30)
    for wx in (13, 24, 36):
        window(c, wx, 18)
    peaked_roof(c, 3, 0, 46, 17, ROOF[team])
    c.rect(36, 2, 4, 8, STONE[2]); c.px(37, 1, hexc("cfcfcf"))
    banner(c, 46, 24, team, 10)
    c.outline()
    return c


def granary(team):
    c = Canvas(44, 46)
    shadow(c, 22, 42, 20, 4)
    # round silo
    for y in range(12, 40):
        for x in range(4, 20):
            t = (x - 4) / 15
            col = P["straw"][0] if t < 0.3 else P["straw"][1] if t < 0.75 else P["straw"][2]
            if (y - 12) % 5 == 4:
                col = P["leather"][1]
            c.px(x, y, col)
    for y in range(4, 13):
        half = (y - 3) * 9 / 9
        for x in range(int(12 - half), int(12 + half)):
            c.px(x, y, ROOF[team][1] if x < 12 else ROOF[team][2])
    # barn
    plank_wall(c, 20, 22, 21, 18)
    door(c, 26, 30, 9, 10, WOOD[2])
    c.line(26, 30, 34, 39, WOOD[0]); c.line(34, 30, 26, 39, WOOD[0])
    peaked_roof(c, 18, 10, 25, 13, ROOF[team])
    # sacks
    for x in (6, 11):
        c.ellipse(x, 39, 2.5, 2, P["white"][1])
    c.outline()
    return c


def lumber_mill(team):
    c = Canvas(48, 42)
    shadow(c, 24, 38, 22, 4)
    plank_wall(c, 4, 20, 26, 16)
    peaked_roof(c, 1, 8, 32, 13, ROOF[team])
    c.rect(12, 26, 10, 10, WOOD[3])
    # saw blade
    c.ellipse(17, 30, 4, 4, STONE[1])
    c.ellipse(17, 30, 1.5, 1.5, STONE[3])
    for a in range(8):
        import math
        c.px(int(17 + 4.6 * math.cos(a * 0.785)), int(30 + 4.6 * math.sin(a * 0.785)), STONE[0])
    # log pile
    for row, y in enumerate((34, 30, 26)):
        for i in range(3 - row):
            x = 32 + row * 3 + i * 5
            c.ellipse(x + 2, y, 2.5, 2.5, WOOD[1])
            c.ellipse(x + 2, y, 1.2, 1.2, P["straw"][1])
    banner(c, 3, 8, team, 12)
    c.outline()
    return c


def guard_post(team):
    c = Canvas(34, 50)
    shadow(c, 17, 46, 15, 3)
    # palisade base
    for x in range(3, 31, 3):
        c.rect(x, 32, 3, 13, WOOD[1])
        c.px(x + 1, 31, WOOD[0])
        c.rect(x + 2, 32, 1, 13, WOOD[2])
    # watch platform on stilts
    for x in (8, 24):
        c.rect(x, 14, 2, 20, WOOD[2])
    c.rect(5, 12, 24, 4, WOOD[1]); c.rect(5, 12, 24, 1, WOOD[0])
    for x in range(5, 29, 4):
        c.rect(x, 8, 2, 4, WOOD[1])
    peaked_roof(c, 4, 0, 26, 9, ROOF[team])
    # crossed spears sign
    c.line(12, 36, 22, 44, STONE[1]); c.line(22, 36, 12, 44, STONE[1])
    c.ellipse(17, 40, 3, 3, TEAM[team][1])
    c.outline()
    return c


def shrine(team):
    c = Canvas(34, 46)
    shadow(c, 17, 42, 15, 3)
    c.rect(4, 36, 26, 6, STONE[2]); c.rect(4, 36, 26, 1, STONE[0])
    c.rect(7, 33, 20, 4, STONE[1]); c.rect(7, 33, 20, 1, STONE[0])
    # obelisk
    for y in range(8, 34):
        w = 4 + (y - 8) * 3 // 26
        for x in range(17 - w, 17 + w):
            c.px(x, y, STONE[1] if x < 17 else STONE[2])
    c.rect(16, 4, 2, 4, STONE[1])
    # glowing rune and crystal
    for (x, y) in ((16, 16), (17, 17), (16, 18), (17, 19), (16, 22), (17, 22)):
        c.px(x, y, P["lumen"][1])
    c.ellipse(17, 3, 2.5, 3, P["lumen"][1]); c.px(16, 2, P["lumen"][0])
    for x in (6, 28):
        c.rect(x, 30, 2, 4, TEAM[team][1])
        c.px(x, 29, P["gold"][1])
    c.outline()
    return c


def extractor(team):
    c = Canvas(36, 46)
    shadow(c, 18, 42, 16, 3)
    c.rect(4, 32, 28, 10, STONE[2]); c.rect(4, 32, 28, 1, STONE[0]); c.rect(4, 41, 28, 1, STONE[4])
    # frame
    for x in (7, 27):
        c.rect(x, 6, 2, 27, P["steel"][2]); c.rect(x, 6, 1, 27, P["steel"][1])
    c.rect(7, 6, 22, 2, P["steel"][2]); c.rect(7, 6, 22, 1, P["steel"][0])
    # crystal tank
    c.rect(12, 14, 12, 18, P["lumen"][3])
    for y in range(15, 31):
        for x in range(13, 23):
            c.px(x, y, P["lumen"][2] if (x + y) % 5 else P["lumen"][1])
    c.rect(13, 15, 2, 15, P["lumen"][0])
    c.rect(11, 13, 14, 2, P["gold"][2]); c.rect(11, 31, 14, 2, P["gold"][2])
    # pipe and wheel
    c.rect(24, 22, 6, 2, P["steel"][2])
    c.ellipse(31, 18, 3, 3, P["steel"][1]); c.ellipse(31, 18, 1, 1, P["steel"][3])
    banner(c, 2, 20, team, 11)
    c.outline()
    return c


def wall(team):
    """One 1x1 stone wall segment: drawn tall so rows of them read as a rampart."""
    c = Canvas(16, 26)
    shadow(c, 8, 24, 8, 2)
    brick_wall(c, 0, 8, 16, 17)
    for x in (0, 6, 12):
        c.rect(x, 5, 4, 3, STONE[1]); c.rect(x, 5, 4, 1, STONE[0])
    c.rect(0, 8, 16, 1, STONE[0])
    c.rect(6, 14, 4, 2, TEAM[team][1])
    c.outline()
    return c


def gate(team):
    c = Canvas(16, 26)
    shadow(c, 8, 24, 8, 2)
    brick_wall(c, 0, 6, 3, 19)
    brick_wall(c, 13, 6, 3, 19)
    c.rect(0, 3, 16, 4, STONE[1]); c.rect(0, 3, 16, 1, STONE[0])
    c.rect(3, 10, 10, 15, WOOD[2])
    for x in (5, 8, 11):
        c.rect(x, 10, 1, 15, WOOD[3])
    c.rect(3, 13, 10, 1, P["steel"][2]); c.rect(3, 20, 10, 1, P["steel"][2])
    c.rect(6, 4, 4, 2, TEAM[team][1])
    c.outline()
    return c


BUILDINGS = {
    "Keep": keep, "House": house, "Cradle": cradle, "Camp": camp, "FusionAltar": altar, "TrainingGrounds": grounds,
    "Watchtower": watchtower, "Dungeon": dungeon, "MutationPit": pit, "LumenSpire": spire, "Market": market,
    "Infirmary": infirmary, "AncientAltar": ancient_altar, "Merchant": merchant, "Den": den,
    "Farm": farm, "Manor": manor, "Granary": granary, "LumberMill": lumber_mill, "GuardPost": guard_post,
    "Shrine": shrine, "LumenExtractor": extractor, "Wall": wall, "Gate": gate,
}


def build():
    out = ASSETS / "buildings"
    made = []
    for name, fn in BUILDINGS.items():
        for team in ("blue", "red", "wild"):
            img = fn(team)
            img.save(out / f"{name}_{team}.png")
            if team == "blue":
                made.append(img)
    sites = [site(i) for i in range(4)]
    sheet(sites, 4).save(out / "site.png")
    return made


if __name__ == "__main__":
    made = build()
    W = max(m.w for m in made)
    H = max(m.h for m in made)
    tiles = []
    for m in made:
        t = Canvas(W, H)
        t.blit(m, (W - m.w) // 2, H - m.h)
        tiles.append(t)
    preview(sheet(tiles, 5), "buildings", 3)
