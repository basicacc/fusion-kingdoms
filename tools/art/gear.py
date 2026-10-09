"""Headgear and tools for each role. Each piece is ASCII per view ('down', 'up', 'side'), drawn relative to the
head's top-left so it bobs with the walk. Tools are drawn relative to the hand."""
from lib import P

C = {
    ".": None,
    # straw
    "y": P["straw"][0], "Y": P["straw"][1], "z": P["straw"][2],
    # gold
    "g": P["gold"][0], "G": P["gold"][1], "q": P["gold"][2],
    # steel
    "m": P["steel"][0], "M": P["steel"][1], "n": P["steel"][2], "N": P["steel"][3],
    # green cloth
    "v": P["green"][0], "V": P["green"][1], "x": P["green"][2],
    # dark cloth
    "k": P["dark"][0], "K": P["dark"][1], "j": P["dark"][2],
    # fur
    "u": P["fur"][0], "U": P["fur"][1], "o": P["fur"][2],
    # white
    "a": P["white"][0], "A": P["white"][1], "B": P["white"][2],
    # wood
    "w": P["wood"][0], "W": P["wood"][1], "X": P["wood"][2],
    # lumen
    "l": P["lumen"][0], "L": P["lumen"][1], "i": P["lumen"][2],
    # red
    "r": P["red"][1], "R": P["red"][2],
    # orange
    "c": P["orange"][0], "O": P["orange"][1], "Q": P["orange"][2],
    # leather
    "e": P["leather"][0], "E": P["leather"][1], "F": P["leather"][2],
}

# Headgear: (rows, dx, dy) per view, drawn with its top-left at (dx, head_y + dy).
HATS = {
    "Farmer": {  # wide straw hat
        "down": ([
            ".....yyyy.......",
            "....yYYYYz......",
            "..yyyyyyyyyyz...",
            "..zzzzzzzzzzz...",
        ], 0, -2),
        "up": ([
            ".....yyyy.......",
            "....yYYYYz......",
            "..yyyyyyyyyyz...",
            "..zzzzzzzzzzz...",
        ], 0, -2),
        "side": ([
            ".....yyyy.......",
            "....yYYYYz......",
            "..yyyyyyyyyyyz..",
            "...zzzzzzzzzz...",
        ], 0, -2),
    },
    "Builder": {  # yellow hard hat
        "down": ([
            ".....gggg.......",
            "....gGGGGq......",
            "...gGGGGGGq.....",
            "..qqqqqqqqqq....",
        ], 0, -1),
        "up": ([
            ".....gggg.......",
            "....gGGGGq......",
            "...gGGGGGGq.....",
            "..qqqqqqqqqq....",
        ], 0, -1),
        "side": ([
            ".....gggg.......",
            "....gGGGGGq.....",
            "...gGGGGGGGq....",
            "...qqqqqqqqqqq..",
        ], 0, -1),
    },
    "Guard": {  # steel helmet with crest
        "down": ([
            "......rr........",
            ".....mmmm.......",
            "....mMMMMn......",
            "...mMMMMMMn.....",
            "...MMnnnnMM.....",
            "...M......M.....",
        ], 0, -2),
        "up": ([
            "......rr........",
            ".....mmmm.......",
            "....mMMMMn......",
            "...mMMMMMMn.....",
            "...MMMMMMMMn....",
            "...MMMMMMMMn....",
            "...nnnnnnnnN....",
        ], 0, -2),
        "side": ([
            ".....rrr........",
            ".....mmmm.......",
            "....mMMMMn......",
            "...mMMMMMMMn....",
            "...MMMMnnnnn....",
            "...MMM..........",
            "...nnn..........",
        ], 0, -2),
    },
    "Scout": {  # green hood with a feather
        "down": ([
            ".........y......",
            ".....vvvvY......",
            "....vVVVVVv.....",
            "...vVVVVVVVx....",
            "...VV......Vx...",
            "...V........x...",
        ], 0, -1),
        "up": ([
            ".........y......",
            ".....vvvvY......",
            "....vVVVVVv.....",
            "...vVVVVVVVx....",
            "...VVVVVVVVVx...",
            "...VVVVVVVVVx...",
            "...xVVVVVVVxx...",
            "....xxxxxxx.....",
        ], 0, -1),
        "side": ([
            "......y.........",
            ".....vYvv.......",
            "....vVVVVv......",
            "...vVVVVVVV.....",
            "..xVVVV.........",
            "..xVVV..........",
            "..xVV...........",
        ], 0, -1),
    },
    "Thief": {  # dark hood and a mask
        "down": ([
            ".....kkkk.......",
            "....kKKKKj......",
            "...kKKKKKKj.....",
            "...KK......j....",
            "...K........j...",
            "................",
            "................",
            "...KKKKKKKKKj...",
            "....jjjjjjj.....",
        ], 0, -1),
        "up": ([
            ".....kkkk.......",
            "....kKKKKj......",
            "...kKKKKKKj.....",
            "...KKKKKKKKj....",
            "...KKKKKKKKKj...",
            "...KKKKKKKKKj...",
            "...jKKKKKKKjj...",
            "....jjjjjjj.....",
        ], 0, -1),
        "side": ([
            ".....kkkk.......",
            "....kKKKKj......",
            "...kKKKKKKK.....",
            "..jKKKK.........",
            "..jKKK..........",
            "..jKK...........",
            "..jK............",
            "..jKKKKKKKKKj...",
            "...jjjjjjjjj....",
        ], 0, -1),
    },
    "Hunter": {  # fur cap with a tail
        "down": ([
            ".....uuuu.......",
            "....uUUUUo......",
            "...uUUUUUUo.....",
            "...ooooooooo....",
        ], 0, -1),
        "up": ([
            ".....uuuu.......",
            "....uUUUUo......",
            "...uUUUUUUo.....",
            "...ooooooooo....",
            ".........Uo.....",
            ".........Uo.....",
            "........uo......",
        ], 0, -1),
        "side": ([
            ".....uuuu.......",
            "....uUUUUo......",
            "...uUUUUUUo.....",
            "..Uoooooooooo...",
            "..Uo............",
            ".uo.............",
        ], 0, -1),
    },
    "Healer": {  # white hood with a red mark
        "down": ([
            ".....aaaa.......",
            "....aAArAB......",
            "...aAArrrAB.....",
            "...AA..r...B....",
            "...A........B...",
            "...A........B...",
        ], 0, -1),
        "up": ([
            ".....aaaa.......",
            "....aAAAAB......",
            "...aAAAAAAB.....",
            "...AAAAAAAAB....",
            "...AAAAAAAAAB...",
            "...AAAAAAAAAB...",
            "...BAAAAAAABB...",
            "....BBBBBBB.....",
        ], 0, -1),
        "side": ([
            ".....aaaa.......",
            "....aAAAAA......",
            "...aAAAAAAA.....",
            "..BAAAA.........",
            "..BAAA..........",
            "..BAA...........",
            "..BAA...........",
        ], 0, -1),
    },
    "Hoarder": {  # headband; the big backpack is drawn as a tool
        "down": ([
            "...cOOOOOOOQ....",
        ], 0, 2),
        "up": ([
            "...cOOOOOOOQ....",
            "..........OQ....",
            "..........Q.....",
        ], 0, 2),
        "side": ([
            "...cOOOOOOOOQ...",
            "..OQ............",
            "..Q.............",
        ], 0, 2),
    },
    "Crown": {
        "down": ([
            "....g.g.g.......",
            "....gGgGg.......",
            "....qGrGq.......",
        ], 1, -2),
        "up": ([
            "....g.g.g.......",
            "....gGgGg.......",
            "....qGGGq.......",
        ], 1, -2),
        "side": ([
            "....g.g.g.......",
            "....gGgGg.......",
            "....qGrGq.......",
        ], 1, -2),
    },
}

# Tools: (rows, dx, dy) per view, relative to head top-left. Drawn after the body.
TOOLS = {
    "Farmer": {  # pitchfork
        "down": ([
            "m.m.m",
            "mMmMm",
            ".MMM.",
            "..W..",
            "..W..",
            "..W..",
            "..X..",
            "..X..",
            "..X..",
        ], 10, 4),
        "up": ([
            "m.m.m",
            "mMmMm",
            ".MMM.",
            "..W..",
            "..W..",
        ], 1, 3),
        "side": ([
            "m.m.m",
            "mMmMm",
            ".MMM.",
            "..W..",
            "..W..",
            "..W..",
            "..X..",
            "..X..",
            "..X..",
        ], 10, 4),
    },
    "Builder": {  # hammer
        "down": ([
            "nMMn",
            "nMMn",
            ".W..",
            ".W..",
            ".X..",
        ], 11, 7),
        "up": ([
            "nMMn",
            ".W..",
        ], 2, 6),
        "side": ([
            "nMMn",
            "nMMn",
            ".W..",
            ".W..",
            ".X..",
        ], 11, 7),
    },
    "Guard": {  # round shield on the left arm, team colours with a steel boss
        "down": ([
            ".MMM.",
            "MttTM",
            "MtmTM",
            "MTTTM",
            ".MMM.",
        ], 1, 8),
        "up": ([
            "m",
            "M",
            "M",
            "M",
            "n",
            "n",
            "n",
        ], 12, 3),
        "side": ([
            "m",
            "M",
            "M",
            "M",
            "M",
            "n",
            "n",
            "n",
            "n",
        ], 12, 3),
    },
    "Scout": {  # spyglass
        "down": ([
            "G",
            "q",
            "G",
            "q",
        ], 12, 9),
        "up": ([], 0, 0),
        "side": ([
            "GqGqG",
        ], 10, 9),
    },
    "Thief": {  # dagger
        "down": ([
            "m",
            "M",
            "n",
            "E",
        ], 12, 8),
        "up": ([], 0, 0),
        "side": ([
            "Emmm",
        ], 10, 10),
    },
    "Hunter": {  # bow
        "down": ([
            ".W",
            "W.",
            "W.",
            "W.",
            "W.",
            ".W",
        ], 12, 6),
        "up": ([
            "W....W",
            ".W..W.",
            "..WW..",
        ], 5, 7),
        "side": ([
            ".Wa",
            "W.a",
            "W.a",
            "W.a",
            ".Wa",
        ], 11, 6),
    },
    "Healer": {  # staff with a Lumen orb
        "down": ([
            ".l.",
            "lLi",
            ".i.",
            ".W.",
            ".W.",
            ".W.",
            ".X.",
            ".X.",
            ".X.",
        ], 11, 3),
        "up": ([
            ".l.",
            "lLi",
            ".i.",
            ".W.",
            ".W.",
        ], 1, 2),
        "side": ([
            ".l.",
            "lLi",
            ".i.",
            ".W.",
            ".W.",
            ".W.",
            ".X.",
            ".X.",
            ".X.",
        ], 10, 3),
    },
    "Hoarder": {  # bulging backpack
        "down": ([
            "eE......Ee",
        ], 3, 9),
        "up": ([
            ".eeeeee.",
            "eEEEEEEF",
            "eEEgEEEF",
            "eEEEEEEF",
            ".FFFFFF.",
        ], 4, 8),
        "side": ([
            ".ee.",
            "eEEF",
            "eEEF",
            "eEEF",
            ".FF.",
        ], 2, 8),
    },
}

TEAM_TOKENS = {"t": 1, "T": 2}


def draw(c, rows, dx, dy, head_y, team_ramp):
    legend = dict(C)
    legend["t"] = team_ramp[1]
    legend["T"] = team_ramp[2]
    for y, row in enumerate(rows):
        for x, ch in enumerate(row):
            col = legend.get(ch)
            if col is not None:
                c.px(dx + x, head_y + dy + y, col)
