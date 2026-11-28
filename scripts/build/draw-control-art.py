"""Compile the launcher's touch drawing instructions for the Android Canvas renderer.

All artwork is authored on a small pixel grid. The extraction catalog supplies
names and dimensions. This compiler emits geometry only.
"""

import argparse
import json
import math
import colorsys
from pathlib import Path


ASSETS = Path(__file__).resolve().parents[2] / "managed/MobileLauncher.Controls/Assets"
INK = "#202038"
LIGHT = "#dcf2ff"
BLUE = "#879cdb"
STEEL = "#a9a9af"
GOLD = "#ffd455"
BROWN = "#985736"
GRASS = "#56ac51"
RED = "#ea4940"
TEAL = "#468f9e"
PURPLE = "#43488c"
GREEN = "#356635"
SLATE = "#547280"
CLEAR = (0, 0, 0, 0)


def color(value):
    if value is None:
        return None
    if isinstance(value, str):
        return value if value.startswith("#") else {"white": "#ffffff"}[value]
    r, g, b, *alpha = value
    return "#" + (f"{alpha[0]:02x}" if alpha else "") + f"{r:02x}{g:02x}{b:02x}"


class Sprite:
    def __init__(self):
        self.ops = []

    def poly(self, points, fill, outline=INK):
        self.ops.append(["polygon", points, color(fill), color(outline)])

    def box(self, box, fill=None, outline=INK, radius=0, width=1):
        self.ops.append(["box", box, color(fill), color(outline), radius, width])

    def oval(self, box, fill=None, outline=INK, width=1):
        self.ops.append(["oval", box, color(fill), color(outline), width])

    def arc(self, box, start, end, fill, width):
        self.ops.append(["arc", box, color(fill), start, end - start, width])

    def line(self, points, fill=LIGHT, width=2):
        self.ops.append(["line", points, color(fill), width])

    def paste(self, sprite, box=(0, 0, 32, 32), rotation=0, mirror=False):
        self.ops.append(["layer", box, rotation, mirror, sprite.ops])


def transformed(sprite, rotation=0, mirror=False):
    result = Sprite()
    result.paste(sprite, rotation=rotation, mirror=mirror)
    return result


def button(fill, circle=False, radius=4):
    s = Sprite()
    r, g, b = bytes.fromhex(fill.lstrip("#"))
    shade = tuple(int(c * 0.65) for c in (r, g, b))
    glint = tuple(min(255, c + 22) for c in (r, g, b))
    shape = s.oval if circle else lambda box, color, edge: s.box(box, color, edge, radius)
    shape((1, 1, 30, 30), shade, INK)
    shape((2, 2, 28, 28), fill, glint)
    return s


def points(cx, cy, radii, count, rotation=-math.pi / 2):
    return [
        (
            round(cx + radii[i % len(radii)] * math.cos(rotation + 2 * math.pi * i / count)),
            round(cy + radii[i % len(radii)] * math.sin(rotation + 2 * math.pi * i / count)),
        )
        for i in range(count)
    ]


def glyph(kind):
    s = Sprite()
    if kind == "hand":
        s.poly(
            [
                (5, 3),
                (7, 2),
                (14, 10),
                (16, 11),
                (16, 8),
                (19, 8),
                (20, 10),
                (23, 8),
                (25, 9),
                (25, 15),
                (27, 15),
                (27, 20),
                (22, 28),
                (13, 25),
                (7, 21),
                (8, 17),
                (11, 15),
            ],
            BLUE,
        )
        s.poly(
            [(6, 4), (8, 4), (15, 13), (19, 14), (22, 11), (23, 12), (22, 19), (18, 23), (10, 20), (13, 16)],
            LIGHT,
            None,
        )
    elif kind == "saddle":
        s.poly(
            [
                (3, 5),
                (7, 7),
                (9, 12),
                (16, 16),
                (21, 14),
                (25, 8),
                (29, 6),
                (28, 15),
                (23, 20),
                (19, 22),
                (17, 29),
                (13, 27),
                (13, 22),
                (7, 19),
                (4, 13),
            ],
            "#968d85",
        )
        s.line([(5, 7), (8, 14), (15, 18), (22, 16), (27, 9)], "#eee6d0")
        s.line([(15, 20), (16, 25), (18, 21)], "#d3c4a9")
    elif kind == "hook":
        s.line([(6, 25), (5, 16), (10, 7), (19, 4), (26, 9), (26, 16), (21, 21)], INK, 5)
        s.line([(6, 23), (7, 15), (12, 8), (19, 6), (24, 10), (24, 15), (21, 19)], STEEL, 3)
        for x, y in [(7, 17), (10, 11), (15, 7), (21, 8), (24, 13)]:
            s.box((x, y, x + 2, y + 2), LIGHT, None)
        s.poly([(3, 25), (7, 19), (11, 26), (8, 25), (6, 29)], STEEL)
    elif kind == "sword":
        s.poly([(7, 21), (23, 4), (28, 2), (26, 10), (12, 23)], BLUE)
        s.line([(11, 20), (25, 5)], LIGHT)
        s.line([(4, 29), (11, 22)], BROWN, 4)
        s.line([(5, 18), (15, 27)], INK, 4)
        s.line([(5, 18), (15, 26)], GOLD)
    elif kind in ("tools", "pick"):
        if kind == "tools":
            s.line([(7, 6), (25, 26)], INK, 5)
            s.line([(7, 6), (25, 26)], BROWN, 3)
            s.poly([(2, 10), (5, 4), (12, 3), (14, 5), (10, 7), (7, 12)], STEEL)
        s.line([(5, 27), (22, 9)], INK, 5)
        s.line([(5, 27), (22, 9)], BROWN, 2)
        s.poly([(12, 6), (17, 4), (24, 5), (29, 13), (24, 10), (20, 9), (15, 11)], STEEL)
        s.line([(15, 6), (21, 6), (25, 9)], LIGHT, 1)
    elif kind == "gear":
        s.poly(points(16, 16, [10, 14, 14, 10], 32), STEEL)
        s.oval((9, 9, 23, 23), "#777784")
        s.oval((12, 12, 20, 20), INK, LIGHT)
    elif kind == "torch":
        s.poly([(6, 26), (9, 28), (23, 10), (19, 8)], BROWN)
        s.poly([(16, 11), (17, 6), (20, 8), (23, 1), (26, 6), (24, 12), (20, 15)], RED)
        s.poly([(19, 11), (22, 6), (23, 10), (21, 13)], GOLD, None)
    elif kind in ("smart", "smart-on"):
        s.box(
            (6, 6, 25, 25),
            "#ab9957" if kind == "smart-on" else "#666571",
            GOLD if kind == "smart-on" else "#403e50",
            3,
        )
    elif kind in ("target-on", "target-off"):
        boxes = (
            [(2, 12, 29, 21)] if kind == "target-on" else [(9, 3, 23, 11), (1, 18, 13, 28), (18, 18, 30, 28)]
        )
        for x, y, r, b in boxes:
            s.poly([(x, y), (r, y), ((x + r) // 2, b)], GOLD, "#9e5429")
            s.line([(x + 3, y + 2), (r - 3, y + 2)], "#fff6b9")
    elif kind in ("cross", "aim", "move", "ticks", "reticle", "ring", "radial", "unbound"):
        if kind in ("ring", "reticle", "aim", "unbound"):
            if kind == "unbound":
                for a in range(0, 360, 30):
                    s.arc((3, 3, 28, 28), a, a + 16, fill=INK, width=2)
            else:
                s.oval((3, 3, 28, 28), outline=INK, width=4)
                s.oval((4, 4, 27, 27), outline=LIGHT, width=1)
        if kind in ("aim", "cross"):
            s.poly(
                [
                    (13, 4),
                    (18, 4),
                    (18, 13),
                    (27, 13),
                    (27, 18),
                    (18, 18),
                    (18, 27),
                    (13, 27),
                    (13, 18),
                    (4, 18),
                    (4, 13),
                    (13, 13),
                ],
                LIGHT,
            )
            s.line([(15, 6), (15, 25)], "#bfc5eb", 1)
        if kind in ("move", "ticks", "radial"):
            for a in range(4):
                arm = Sprite()
                if kind == "move":
                    arm.poly([(11, 10), (12, 6), (15, 3), (18, 5), (20, 10), (16, 12)], LIGHT)
                elif kind == "ticks":
                    arm.box((14, 2, 17, 8), LIGHT)
                else:
                    arm.oval((12, 3, 20, 11), BLUE)
                    arm.oval((14, 4, 18, 8), LIGHT, None)
                s.paste(arm, rotation=-90 * a)
    elif kind == "jump":
        for y in (3, 16):
            s.poly([(6, y + 10), (15, y), (17, y), (25, y + 10), (24, y + 14), (16, y + 6), (7, y + 14)], BLUE)
            s.line([(7, y + 10), (16, y + 2), (24, y + 10)], LIGHT)
    elif kind in ("zoom-in", "zoom-out", "search"):
        s.line([(17, 18), (28, 28)], INK, 6)
        s.line([(18, 19), (27, 28)], BROWN, 3)
        s.oval((2, 2, 22, 22), BLUE)
        s.oval((5, 4, 19, 18), "#a4cde8", "#6473af")
        if kind != "search":
            s.line([(7, 12), (17, 12)], INK)
            if kind == "zoom-in":
                s.line([(12, 7), (12, 17)], INK)
    elif kind == "bag":
        s.box((4, 5, 27, 27), "#583a32", INK, 3)
        s.box((6, 6, 25, 17), "#8c563c", "#412a30", 3)
        s.line([(7, 7), (23, 7)], "#b47851")
        s.line([(7, 18), (13, 20), (24, 16)], "#321e26")
        s.box((13, 15, 18, 20), GOLD, "#734b22", 1)
    elif kind in ("map", "pin", "world"):
        if kind == "pin":
            s.poly([(7, 6), (12, 2), (21, 3), (26, 9), (25, 18), (16, 28), (7, 18)], "#ce812f", "#753c2a")
            s.oval((10, 5, 22, 17), GOLD, "#ffdc85")
            s.oval((13, 8, 19, 14), "#b16a34", None)
        else:
            s.box((2, 4, 29, 27), BLUE, INK, 5 if kind == "world" else 1)
            s.poly([(4, 17), (10, 11), (16, 13), (20, 9), (27, 15), (27, 24), (4, 24)], GRASS)
            s.poly([(4, 20), (11, 16), (18, 19), (23, 15), (27, 18), (27, 25), (4, 25)], BROWN)
            s.line([(16, 6), (16, 24)], "#645e77", 1)
    elif kind in ("bubble", "face"):
        if kind == "bubble":
            s.poly(
                [(3, 10), (9, 5), (21, 4), (29, 10), (29, 18), (22, 22), (12, 24), (7, 28), (8, 22), (3, 19)],
                BLUE,
            )
            s.oval((5, 6, 26, 21), LIGHT, None)
        else:
            s.oval((4, 3, 27, 28), LIGHT, BLUE)
        for x in (11, 19):
            s.box((x, 12, x + 1, 14), INK, None)
        s.line([(12, 17), (15, 19), (19, 17)], BLUE)
    elif kind in ("potion-red", "potion-blue", "potion-gold"):
        liquid = {"potion-red": RED, "potion-blue": "#58aaff", "potion-gold": GOLD}[kind]
        s.poly(
            [(11, 4), (20, 4), (20, 14), (25, 20), (25, 25), (20, 28), (10, 28), (6, 24), (7, 18), (11, 14)],
            LIGHT,
        )
        s.oval((9, 17, 23, 25), liquid, BLUE)
        s.box((12, 3, 19, 7), BROWN)
        s.line([(12, 18), (11, 21)], "white")
    else:
        return detail(kind)
    return s


def composite(*layers):
    s = Sprite()
    for kind, box in layers:
        s.paste(glyph(kind), box)
    return s


# Vertices describe the silhouette. A second face or seam keeps these small
# equipment icons legible over both bright scenery and dark inventory slots.
SHAPES = {
    "chestplate": (
        STEEL,
        [
            (2, 8),
            (9, 4),
            (13, 6),
            (18, 6),
            (22, 4),
            (29, 9),
            (26, 16),
            (22, 14),
            (22, 25),
            (16, 28),
            (9, 24),
            (9, 14),
            (5, 16),
        ],
        [(9, 8), (13, 13), (18, 13), (23, 8)],
    ),
    "shirt": (
        "#d4d1cc",
        [
            (2, 9),
            (10, 5),
            (14, 8),
            (18, 8),
            (22, 5),
            (29, 9),
            (26, 16),
            (22, 14),
            (22, 27),
            (9, 27),
            (9, 14),
            (5, 16),
        ],
        [(10, 7), (14, 11), (18, 11), (21, 7)],
    ),
    "vest": (
        STEEL,
        [(7, 4), (11, 4), (16, 13), (21, 4), (25, 5), (22, 15), (22, 27), (9, 27), (9, 14)],
        [(11, 7), (16, 17), (21, 7)],
    ),
    "helmet": (
        STEEL,
        [
            (5, 13),
            (8, 7),
            (15, 4),
            (23, 7),
            (26, 14),
            (24, 22),
            (20, 28),
            (19, 16),
            (16, 12),
            (13, 18),
            (11, 28),
            (6, 22),
        ],
        [(8, 14), (11, 9), (16, 7)],
    ),
    "pants": (STEEL, [(7, 4), (24, 4), (25, 27), (18, 27), (16, 16), (13, 27), (5, 27)], [(8, 9), (22, 9)]),
    "boot": (
        "#cbc6c0",
        [(15, 4), (25, 4), (24, 20), (26, 24), (24, 27), (4, 27), (4, 22), (13, 18)],
        [(16, 8), (22, 8)],
    ),
    "shield": (
        BLUE,
        [(4, 6), (15, 3), (27, 6), (26, 19), (21, 25), (16, 29), (9, 25), (5, 18)],
        [(8, 9), (15, 6), (22, 8), (18, 14), (14, 23)],
    ),
    "hat": (
        STEEL,
        [(2, 21), (10, 18), (14, 9), (22, 5), (22, 14), (29, 17), (24, 23), (11, 25)],
        [(10, 18), (19, 19), (24, 16)],
    ),
    "cart": (STEEL, [(2, 8), (29, 8), (24, 20), (9, 20)], [(5, 10), (26, 10)]),
    "arrow-ammo": (
        STEEL,
        [
            (10, 3),
            (15, 7),
            (20, 3),
            (19, 10),
            (17, 12),
            (17, 22),
            (20, 22),
            (16, 29),
            (12, 22),
            (15, 22),
            (15, 12),
            (11, 10),
        ],
        [(15, 9), (16, 11)],
    ),
    "trophy": (
        GOLD,
        [
            (3, 5),
            (9, 7),
            (9, 4),
            (22, 4),
            (22, 7),
            (29, 5),
            (27, 14),
            (20, 18),
            (17, 20),
            (17, 25),
            (22, 27),
            (22, 29),
            (9, 29),
            (9, 27),
            (14, 25),
            (14, 20),
            (10, 17),
            (5, 14),
        ],
        [(12, 7), (13, 13), (16, 16)],
    ),
    "crown": (GOLD, [(2, 10), (9, 15), (16, 4), (22, 15), (30, 9), (26, 24), (6, 24)], [(8, 20), (24, 20)]),
    "seed": (BROWN, [(6, 22), (10, 17), (16, 16), (22, 20), (21, 26), (15, 29), (9, 27)], [(10, 22), (15, 19)]),
    "speaker": (GRASS, [(3, 12), (9, 12), (18, 4), (19, 27), (9, 20), (3, 20)], [(6, 14), (10, 14), (16, 8)]),
    "sign": (
        BROWN,
        [
            (13, 28),
            (13, 20),
            (4, 20),
            (4, 7),
            (14, 7),
            (14, 3),
            (18, 3),
            (18, 7),
            (27, 7),
            (27, 20),
            (18, 20),
            (18, 28),
        ],
        [(7, 10), (23, 10)],
    ),
    "gamepad": (
        "#5d607c",
        [
            (2, 22),
            (5, 9),
            (9, 6),
            (14, 8),
            (18, 8),
            (23, 6),
            (27, 9),
            (30, 23),
            (27, 26),
            (20, 20),
            (11, 20),
            (6, 26),
        ],
        [(6, 12), (12, 12)],
    ),
    "heart": (
        RED,
        [(3, 12), (4, 8), (9, 5), (15, 9), (21, 5), (27, 7), (29, 12), (26, 19), (16, 28), (6, 19)],
        [(6, 11), (9, 8)],
    ),
    "hammer": (
        STEEL,
        [(3, 25), (15, 11), (12, 9), (17, 4), (28, 13), (24, 17), (20, 14), (7, 28)],
        [(16, 8), (23, 13)],
    ),
}


def detail(kind):
    s = Sprite()
    if kind in SHAPES:
        fill, contour, seam = SHAPES[kind]
        s.poly(contour, fill)
        s.line(seam, LIGHT, 1)
        if kind == "cart":
            for x in (9, 21):
                s.oval((x, 20, x + 4, 24), STEEL)
        if kind == "gamepad":
            s.line([(10, 10), (10, 16)], INK)
            s.line([(7, 13), (13, 13)], INK)
            for x, y, c in [(21, 13, RED), (24, 10, GOLD)]:
                s.oval((x, y, x + 2, y + 2), c, None)
        if kind == "seed":
            s.line([(16, 18), (15, 12), (10, 8)], "#559944", 2)
            s.poly([(15, 12), (18, 5), (26, 5), (23, 10)], GRASS)
        if kind == "speaker":
            for r in (5, 9):
                s.arc((16 - r, 15 - r, 16 + r, 15 + r), -60, 60, fill=BLUE, width=2)
    elif kind in ("swap", "interact-sword", "pvp"):
        if kind == "pvp":
            s.paste(glyph("sword"), mirror=True)
            s.paste(glyph("sword"))
        elif kind == "interact-sword":
            s.paste(glyph("sword"))
            s.paste(glyph("hand"), (8, 9, 22, 22))
        else:
            s.paste(glyph("sword"), (0, 0, 20, 20))
            s.paste(glyph("hand"), (12, 13, 20, 20))
            s.line([(20, 3), (24, 4), (27, 9)], "#58bfff", 2)
            s.poly([(23, 9), (30, 7), (28, 13)], "#58bfff")
            s.line([(12, 29), (7, 27), (4, 22)], "#58bfff", 2)
            s.poly([(2, 24), (4, 18), (9, 23)], "#58bfff")
    elif kind in ("plus", "close", "check", "play", "down", "up", "back", "undo", "redo"):
        if kind == "plus":
            s.poly(
                [
                    (12, 3),
                    (19, 3),
                    (19, 12),
                    (28, 12),
                    (28, 19),
                    (19, 19),
                    (19, 28),
                    (12, 28),
                    (12, 19),
                    (3, 19),
                    (3, 12),
                    (12, 12),
                ],
                GRASS,
            )
        elif kind == "close":
            for a, b in [((5, 5), (26, 26)), ((5, 26), (26, 5))]:
                s.line([a, b], INK, 6)
                s.line([a, b], RED, 3)
        elif kind == "check":
            s.line([(3, 15), (12, 24), (28, 6)], INK, 6)
            s.line([(3, 15), (12, 24), (28, 6)], "#79e75c", 3)
        elif kind in ("undo", "redo"):
            s.arc((3, 8, 24, 31), 180, 344, fill=BLUE, width=4)
            s.poly([(16, 19), (26, 10), (26, 20)], LIGHT)
            if kind == "undo":
                return transformed(s, mirror=True)
        else:
            s.poly(
                [(4, 6), (27, 16), (4, 26)],
                GRASS if kind == "play" else LIGHT if kind in ("up", "down") else RED,
            )
            return transformed(s, rotation={"play": 0, "up": -90, "down": 90, "back": 180}[kind])
    elif kind in ("star", "unfavorite"):
        s.poly(points(16, 16, [14, 6], 10), GOLD, "#8c5925")
        s.line([(9, 18), (15, 14), (17, 7)], "#fff598")
        if kind == "unfavorite":
            s.line([(5, 27), (27, 4)], RED, 3)
    elif kind in ("eye", "eye-off", "iris"):
        if kind != "iris":
            s.poly([(2, 16), (8, 9), (15, 6), (23, 9), (30, 16), (23, 22), (16, 25), (8, 22)], LIGHT)
        s.oval((10, 9, 22, 22), STEEL if kind == "eye-off" else BLUE)
        s.oval((14, 12, 20, 19), INK)
        s.box((14, 12, 16, 14), LIGHT, None)
    elif kind in (
        "chest",
        "trash",
        "lock",
        "copy",
        "book",
        "die",
        "block",
        "ruler",
        "chair",
        "list",
        "grid",
        "interface",
        "joystick",
    ):
        if kind == "chest":
            s.box((2, 7, 29, 26), BROWN, INK, 3)
            s.box((4, 9, 27, 15), "#d69762", INK, 2)
            for x in (6, 23):
                s.line([(x, 9), (x, 24)], STEEL, 2)
            s.box((13, 15, 18, 20), GOLD)
        elif kind == "trash":
            s.poly([(7, 9), (25, 9), (22, 27), (10, 27)], BLUE)
            s.box((5, 6, 27, 10), LIGHT)
            s.box((12, 3, 19, 6), STEEL)
            for x in (12, 16, 20):
                s.line([(x, 13), (x, 23)], LIGHT, 1)
        elif kind == "lock":
            s.oval((8, 3, 23, 20), None, STEEL)
            s.box((5, 13, 26, 27), GOLD, INK, 3)
            s.oval((13, 17, 18, 22), INK)
        elif kind in ("copy", "book"):
            if kind == "copy":
                s.box((5, 4, 20, 23), BLUE)
                s.box((11, 9, 26, 28), LIGHT)
                for y in (14, 18, 22):
                    s.line([(14, y), (23, y)], BLUE, 1)
            else:
                for x, dy in [(2, 0), (16, 1)]:
                    s.box((x, 6 + dy, x + 13, 25 + dy), "#eadbc6", BROWN, 2)
                    for y in (12, 16, 20):
                        s.line([(x + 3, y + dy), (x + 10, y + dy)], BLUE, 1)
        elif kind == "die":
            s.box((4, 4, 27, 27), LIGHT, INK, 4)
            for x, y in [(10, 10), (22, 10), (16, 16), (10, 22), (22, 22)]:
                s.oval((x - 1, y - 1, x + 1, y + 1), INK)
        elif kind == "block":
            s.box((4, 4, 27, 27), GOLD)
            s.poly([(5, 5), (26, 5), (5, 26)], "#ffed77", None)
        elif kind == "ruler":
            s.box((10, 3, 22, 29), "#e9a732")
            for y in range(6, 28, 4):
                s.line([(11, y), (16, y)], BROWN, 1)
        elif kind == "chair":
            s.box((6, 4, 10, 26), BROWN)
            s.box((9, 16, 25, 20), BROWN)
            s.box((21, 20, 24, 28), BROWN)
        elif kind == "list":
            for y in (5, 13, 21):
                s.box((3, y, 28, y + 4), LIGHT)
        elif kind == "grid":
            for x in (9, 15, 21):
                for y in (9, 15, 21):
                    s.box((x, y, x + 2, y + 2), LIGHT, None)
        elif kind == "interface":
            s.box((3, 4, 28, 26), SLATE)
            for x, y, c in [(6, 7, GRASS), (17, 7, GOLD), (6, 17, BLUE), (17, 17, TEAL)]:
                s.box((x, y, x + 7, y + 6), c)
        else:
            s.poly([(3, 23), (9, 17), (23, 17), (29, 22), (23, 28), (8, 28)], STEEL)
            s.line([(15, 19), (12, 9)], INK, 4)
            s.oval((7, 4, 18, 13), BLUE)
    elif kind in (
        "coin",
        "necklace",
        "pet",
        "light-pet",
        "bow",
        "dye",
        "chain",
        "music",
        "hue",
        "head",
        "bunny",
        "house",
    ):
        if kind == "coin":
            s.oval((10, 4, 22, 28), GOLD)
            s.oval((13, 8, 19, 24), None, "#9d682c")
        elif kind in ("necklace", "pet", "chain"):
            s.oval((6, 3, 26, 18), None, STEEL)
            s.oval((8, 5, 24, 16), None, "#7c7986")
            s.oval((13, 16, 19, 24), BLUE if kind == "chain" else STEEL)
            s.line([(16, 23), (16, 28)], STEEL)
        elif kind in ("light-pet", "bow"):
            s.poly(
                [(2, 5), (10, 9), (15, 15), (21, 8), (29, 5), (26, 17), (20, 21), (15, 19), (10, 21), (5, 16)],
                STEEL,
            )
            s.oval((12, 14, 19, 22), GOLD)
        elif kind == "dye":
            s.poly([(7, 9), (15, 6), (23, 13), (14, 23), (5, 17)], STEEL)
            s.oval((22, 18, 25, 22), TEAL)
            s.oval((24, 24, 26, 27), TEAL)
        elif kind == "music":
            s.line([(20, 22), (20, 4), (27, 10), (27, 15)], LIGHT, 3)
            s.oval((8, 20, 21, 28), STEEL)
        elif kind == "hue":
            for x in range(2, 30):
                s.line(
                    [(x, 6), (x, 25)],
                    tuple(round(v * 255) for v in colorsys.hls_to_rgb((x - 2) * 13 / 360, 0.55, 0.85)),
                    1,
                )
        elif kind == "head":
            s.oval((9, 3, 23, 21), "#c9beb3")
            s.poly([(3, 28), (7, 24), (13, 21), (19, 21), (25, 24), (29, 28)], STEEL)
        elif kind == "bunny":
            s.oval((7, 15, 23, 26), LIGHT, BLUE)
            s.oval((16, 10, 26, 21), LIGHT, BLUE)
            s.oval((16, 2, 20, 13), LIGHT, BLUE)
            s.oval((21, 1, 25, 13), LIGHT, BLUE)
            s.box((22, 14, 23, 15), INK, None)
            s.oval((4, 18, 9, 23), LIGHT)
        else:
            s.box((6, 13, 26, 28), "#e9a25e")
            s.poly([(2, 15), (14, 4), (19, 4), (30, 15)], RED)
            s.box((13, 18, 19, 28), BROWN)
    else:
        raise ValueError(f"Missing drawing for {kind}")
    return s


# Shared mobile pictograms. Group-specific variants are composed below.
GROUPS = {
    "move": "DirectionalArrowsOriginal JoystickKnob EditorMove",
    "ticks": "JoystickArrowsOriginal AttackAimUse",
    "cross": "AimOnlyMode AttackAimOnly PhoneAimCross ZoomInPlus EditorAimCross WorldAimCross",
    "aim": "AimUseMode PhoneAimModeGlyph",
    "reticle": "AimReticle",
    "bag": "Bag BackpackIcon PhoneInventoryGlyph",
    "bubble": "Chat ChatBubble ChatSelected",
    "face": "Emote",
    "hook": "GrappleHook GrappleHookIdle PhoneEmptyGrappleGlyph PhoneGrappleGlyph EmptyGrapple EditorGrappleGlyph",
    "jump": "Jump PhoneJumpGlyph MoveJumpHint MoveJumpHintOriginal EditorJumpGlyph",
    "map": "Map MapSelected",
    "pin": "MapStyle Minimap MinimapSelected",
    "saddle": "Mount PhoneMountGlyph EmptyMount EditorMountGlyph",
    "torch": "PhoneAutoSelectGlyph",
    "hand": "PhoneInteractGlyph WorldInteractCursor EditorMultitouch PauseControls",
    "chestplate": "PhoneLoadoutGlyph Loadout EmptyBody",
    "tools": "PhoneMenuGlyph EditorCustom PauseSettings",
    "radial": "PhoneRadialGlyph EditorRadial",
    "smart": "PhoneSmartCursorGlyph SmartCursorOff",
    "smart-on": "PhoneSmartCursorOnGlyph SmartCursor",
    "interact-sword": "PhoneSmartInteractGlyph",
    "swap": "PhoneSwapGlyph",
    "target-on": "PhoneTargetGlyph TargetLockOn",
    "target-off": "TargetLockOff",
    "unbound": "PhoneUnboundGlyph EditorUnbound",
    "sword": "PhoneUseGlyph",
    "gamepad": "PhoneUseProfileGlyph",
    "zoom-in": "PhoneZoomInGlyph ZoomIn",
    "zoom-out": "PhoneZoomOutGlyph ZoomOut",
    "potion-red": "Heal",
    "potion-blue": "Mana",
    "potion-gold": "Buff RadialBuff",
    "gear": "EditorGear",
    "close": "Close EditorDiscard",
    "pick": "Craft CraftEmpty",
    "shield": "Defense",
    "necklace": "EmptyAccessory",
    "bow": "EmptyVanityAccessory",
    "arrow-ammo": "EmptyAmmo",
    "vest": "CharacterUndershirt",
    "shirt": "CharacterShirt EmptyVanityBody",
    "coin": "EmptyCoin",
    "dye": "EmptyDye",
    "helmet": "EmptyHead Equipment",
    "hat": "EmptyVanityHead",
    "pants": "EmptyLegs CharacterPants EmptyVanityLegs",
    "light-pet": "EmptyLightPet",
    "cart": "EmptyMinecart",
    "pet": "EmptyPet",
    "star": "Favorite MobileFavorite",
    "unfavorite": "Unfavorite",
    "grid": "Grid",
    "house": "House",
    "chain": "Link",
    "pvp": "Pvp",
    "ruler": "Ruler",
    "search": "Search",
    "trash": "Trash MobileDelete",
    "eye": "VisibilityOn CharacterEye",
    "eye-off": "VisibilityOff",
    "lock": "CraftLock",
    "back": "Back",
    "hue": "CharacterHue",
    "list": "CharacterInfo WorldGeneral",
    "iris": "CharacterIris",
    "boot": "CharacterShoes",
    "head": "CharacterSkin",
    "music": "CharacterVoice",
    "crown": "EditorAdvanced",
    "check": "EditorApply",
    "copy": "EditorCopy",
    "redo": "EditorRedo",
    "seed": "EditorSimple WorldSeed",
    "undo": "EditorUndo",
    "down": "ExpandDown WorldDropdown",
    "up": "ExpandUp",
    "plus": "MobileAdd",
    "play": "MobilePlay PauseResume",
    "sign": "MobileRename WorldName",
    "trophy": "PauseAchievements",
    "book": "PauseBestiary PauseSaveExit",
    "die": "WorldDice",
}
SYMBOLS = {name: kind for kind, names in GROUPS.items() for name in names.split()}

# Flat, dark-edged materials used as nine-slice surfaces in the actual UI.
MATERIALS = {
    "#414889": "Slot EmotePanel Panel ChatPanel",
    "#57717d": "SlotTeal Tool EmoteSlot",
    GREEN: "SlotGreen EditorActionGreen",
    SLATE: "EditorActionTeal",
    "#914f90": "SlotPink",
    "#493773": "SlotPurple",
    "#555191": "SlotOccupied",
    "#538fb9": "SlotNew",
    "#768337": "SlotShop",
    "#ffe144": "SlotSelected EditorPressed",
    "#44598d": "ToolSelected TabSelected EmoteHeader Button MultiplayerTab MultiplayerTitle Row Title EditorSelected",
    "#4b4eae": "PhoneEditorSlot",
    "#3a3e73": "EditorButton MultiplayerButton PauseButton",
    "#283455": "CreationPopup MultiplayerPanel",
    "#344170": "CreationRow MultiplayerRow SelectionNormal",
    "#52639f": "SelectionSelected PreviewSelected",
    "#1c2339": "PreviewFrame",
    "#394c86": "CreationOption",
    "#6980b5": "CreationOptionSelected",
    "#893e40": "ToggleOff EditorActionUnbound",
    "#3b8c37": "ToggleOn SwitchOn",
    TEAL: "Toolbar EditorToolButton PhoneTealCircle",
    PURPLE: "PhonePurpleCircle ItemButtonFrame",
    "#ffe464": "PhonePressedCircle PhoneYellowCircle EditorYellowCircle",
    "#355f37": "PhoneGreenCircle",
    "#525fb1": "JoystickKnobBaseOriginal",
    "#ffffff": "EditorPlainButton",
}
FILLS = {name: color for color, names in MATERIALS.items() for name in names.split()}
CIRCLES = set(
    "PhoneTealCircle PhonePurpleCircle ItemButtonFrame PhonePressedCircle PhoneYellowCircle EditorYellowCircle PhoneGreenCircle JoystickKnobBaseOriginal EditorPlainButton".split()
)
ACTION_BASE = {
    "Jump": TEAL,
    "SmartCursor": TEAL,
    "SmartCursorOff": TEAL,
    "TargetLockOn": TEAL,
    "TargetLockOff": TEAL,
    "AimOnlyMode": TEAL,
    "AimUseMode": TEAL,
    "ZoomInPlus": TEAL,
    "AttackAimOnly": PURPLE,
    "AttackAimUse": PURPLE,
    "JoystickKnob": PURPLE,
    "GrappleHook": GREEN,
    "GrappleHookIdle": GREEN,
    "Mount": GREEN,
    "ZoomIn": SLATE,
    "ZoomOut": SLATE,
    "Chat": SLATE,
    "Map": SLATE,
}
RINGS = set(
    "JoystickPad JoystickPadOriginal JoystickBase PhoneGrapplePad JoystickFrame PhoneRadialRing EditorRadialRing".split()
)
INVENTORY_CATEGORIES = [
    "shirt",
    "chair",
    "interact-sword",
    "potion-red",
    "star",
    "tools",
    "block",
    "heart",
    "boot",
    "chain",
    "sword",
    "helmet",
]
CHAT_CATEGORIES = ["face", "hand", "sword", "world", "head", "bunny", "heart"]
TABS = ["list", "interface", "map", "speaker", "joystick", "hand", "gamepad"]


def highlight(sprite):
    result = Sprite()
    for x, y in ((-1, 0), (1, 0), (0, -1), (0, 1)):
        result.ops.append(["layer", [x, y, 32, 32], 0, False, sprite.ops, GOLD])
    result.paste(sprite)
    return result


def inventory_action(name):
    s = Sprite()
    if name in ("BlockSwapOn", "BlockSwapOff"):
        s.box((3, 4, 21, 22), STEEL)
        s.box((12, 12, 29, 27), BROWN)
        if name.endswith("Off"):
            s.oval((9, 8, 25, 24), outline=RED, width=3)
            s.line([(11, 22), (23, 10)], RED)
        else:
            s.poly([(10, 21), (21, 8), (22, 20)], GRASS)
    elif name == "InventorySort":
        s.paste(glyph("tools"), (2, 1, 16, 21))
        s.paste(glyph("sword"), (11, 0, 20, 21))
        s.box((2, 16, 29, 28), "#ab382d")
        s.line([(4, 18), (27, 18)], RED)
    elif name in ("QuickStack", "QuickStackCurrent", "QuickStackNearby"):
        s.paste(glyph("chest"), (9, 11, 23, 21))
        if name == "QuickStackCurrent":
            for r in (5, 9, 13):
                s.arc((2, 2, r * 2 + 2, r * 2 + 2), 180, 265, fill=BLUE, width=2)
        else:
            for x, y in [(3, 5), (11, 7), (19, 6), (25, 6), (5, 14), (4, 22)]:
                s.box((x, y, x + 3, y + 3), BLUE)
    elif name in ("Split", "Restock"):
        for x, count in [(4, 4), (22, 1)]:
            for i in range(count):
                s.oval((x, 26 - i * 3, x + 7, 29 - i * 3), GOLD if name == "Restock" else STEEL)
        s.line([(7, 13), (7, 7), (11, 5), (22, 5), (25, 8), (25, 17)], RED, 3)
        s.poly([(21, 15), (29, 15), (25, 21)], RED)
    else:
        target = "bag" if name in ("Sort", "Deposit", "DepositAll") else "chest"
        s.paste(glyph(target), (9, 10, 23, 22))
        for y in (4, 7, 10):
            s.line([(2, y), (13, y)], "#6cc2f8")
        s.line([(15, 7), (23, 7)], BLUE)
        s.poly([(22, 3), (29, 7), (22, 11)], BLUE)
        s.line([(5, 14), (5, 22)], BLUE)
        s.poly([(1, 21), (9, 21), (5, 26)], BLUE)
    return s


def landscape(name):
    s = Sprite()
    if name.endswith("Sky"):
        sky, back, front, sun = {
            "WorldPreviewSky": ("#91cde0", "#78afc7", "#609ab8", "#fff7b4"),
            "WorldPreviewExpertSky": ("#4a465f", "#39394f", "#29283d", "#b47781"),
            "WorldPreviewMasterSky": ("#392e28", "#39281d", "#271c19", "#ffac49"),
        }[name]
        s.box((0, 0, 31, 31), sky, None)
        s.oval((7, 2, 15, 9), sun, None)
        if name == "WorldPreviewMasterSky":
            s.oval((8, 2, 14, 8), INK, None)
        for offset, color in [(0, back), (6, front)]:
            s.poly(
                [
                    (0, 12 + offset),
                    (5, 15 + offset),
                    (10, 11 + offset),
                    (16, 16 + offset),
                    (23, 10 + offset),
                    (28, 13 + offset),
                    (31, 9 + offset),
                    (31, 32),
                    (0, 32),
                ],
                color,
                None,
            )
    elif name.endswith("Bunny"):
        s.paste(glyph("bunny"), (18, 18, 8, 8))
        if "Classic" not in name:
            s.ops.append(["tint", GOLD if "Journey" in name else "#c961b2", 128])

    else:
        evil = "Evil" in name
        height = {"Small": 6, "Medium": 10, "Large": 14}.get(name.removeprefix("WorldPreview"), 10)
        x = 2 if evil else 28
        color = "#8555a3" if name.endswith("Corruption") else "#b94744" if name.endswith("Crimson") else GRASS
        edge = (
            [(0, 22), (5, 22), (8, 25), (15, 27)]
            if evil
            else [(14, 31), (18, 27), (22, 28), (26, 24), (31, 23)]
        )
        s.poly(edge + [(edge[-1][0], 31), (edge[0][0], 31)], BROWN, None)
        s.line(edge, color, 2)
        s.line([(x, 24), (x, 24 - height)], BROWN, 2)
        s.oval((x - 4, 18 - height, x + 4, 25 - height), color, "#3b6042")
    return s


def render(group, name):
    s = Sprite()
    if name in RINGS:
        s.oval((0, 0, 31, 31), (110, 114, 126, 176), INK)
        s.oval((8, 8, 23, 23), CLEAR, None)
    elif name in FILLS:
        s = button(FILLS[name], name in CIRCLES)
    elif name in ("MinimapFrame", "WorldPreviewBorder", "ToolOutline", "SlotFavorite", "SlotFavoriteSelected"):
        if "Favorite" in name:
            s = button(GOLD if name.endswith("Selected") else PURPLE)
        edge = BLUE if name == "SlotFavorite" else GOLD
        s.box((1, 1, 30, 30), radius=3, outline=edge, width=1)
        for x, y in [(2, 2), (25, 2), (2, 25), (25, 25)]:
            s.box((x, y, x + 3, y + 3), edge, BROWN)
    elif name.startswith("WorldPreview"):
        return landscape(name)
    elif name.startswith("Category"):
        i = int(name[8:])
        if group == "MobileHud":
            return composite(("bubble", (0, 0, 32, 32)), (CHAT_CATEGORIES[i], (9, 8, 15, 14)))
        return glyph(INVENTORY_CATEGORIES[i])
    elif name.startswith("Tab") and name[3:].isdigit():
        s = button("#44598d")
        s.paste(glyph(TABS[int(name[3:])]), (6, 6, 20, 20))
    elif name in ("QuickPotions", "ZoomPair"):
        s = button(TEAL, True)
        if name == "ZoomPair":
            s.paste(glyph("zoom-in"), (16, 0, 16, 16))
            s.paste(glyph("zoom-out"), (0, 15, 16, 16))
        else:
            for kind, box in [
                ("potion-red", (8, 0, 13, 13)),
                ("potion-blue", (17, 10, 13, 13)),
                ("potion-gold", (8, 19, 13, 13)),
            ]:
                s.paste(glyph(kind), box)
    elif name in ("EditorSliderTrack", "SliderTrack"):
        s.box((0, 6, 31, 25), "#5c5d65", INK, 2)
    elif name in ("SliderKnob", "PlayerSliderKnob", "EditorJoystick"):
        s.oval((2, 1, 29, 30), LIGHT if name != "EditorJoystick" else INK)
        s.oval((12, 8, 19, 20), BLUE if name == "EditorJoystick" else "#d5d5da", None)
    elif name == "EquipmentGlyphs":
        # Slots use 34-pixel cells with three columns. Keep the individual
        # placeholders aligned with their crop coordinates in the catalog.
        atlas = Sprite()
        cells = [
            ("helmet", "dye", "bow"),
            ("hat", "hook", "pet"),
            ("chestplate", "cart", "necklace"),
            ("shirt", "light-pet", "coin"),
            ("pants", "saddle", "shield"),
            ("pants", "boot", "chain"),
            ("helmet", "arrow-ammo", "sword"),
        ]
        for row, kinds in enumerate(cells):
            for col, kind in enumerate(kinds):
                atlas.paste(glyph(kind), (col * 34 + 1, row * 34 + 1, 32, 32))
        return atlas
    elif name in (
        "Deposit",
        "DepositAll",
        "Loot",
        "LootAll",
        "InventorySort",
        "Sort",
        "Split",
        "Restock",
        "QuickStack",
        "QuickStackCurrent",
        "QuickStackNearby",
        "BlockSwapOn",
        "BlockSwapOff",
    ):
        return inventory_action(name)
    elif name in ("AddServerIcon", "HostIcon", "LocalIcon", "JoinIcon", "RemoteIcon", "PauseInvite"):
        base = {
            "AddServerIcon": "world",
            "HostIcon": "house",
            "LocalIcon": "house",
            "JoinIcon": "head",
            "RemoteIcon": "world",
            "PauseInvite": "head",
        }[name]
        overlay = "plus" if name in ("AddServerIcon", "PauseInvite") else "redo"
        return composite((base, (3, 6, 24, 24)), (overlay, (16, 0, 15, 15)))
    elif name.startswith("WorldDifficulty") or name == "PauseJourney":
        s.paste(glyph("bunny"))
        tint = {
            "WorldDifficultyJourney": GOLD,
            "WorldDifficultyExpert": "#bf51b6",
            "WorldDifficultyMaster": RED,
        }.get(name)
        if tint:
            s.ops.append(["tint", tint, 170])
        if name == "PauseJourney":
            s.paste(glyph("crown"), (4, 0, 25, 16))
    elif name.startswith("WorldSize"):
        size = {"WorldSizeSmall": 14, "WorldSizeMedium": 21, "WorldSizeLarge": 28}[name]
        s.paste(glyph("world"), ((32 - size) // 2, (32 - size) // 2, size, size))
    elif name in ("Heal", "Mana", "Buff"):
        s = button(PURPLE, True)
        s.paste(glyph(SYMBOLS[name]), (8, 6, 16, 20))
    else:
        kind = ("gear" if group == "MobileInventory" else "tools") if name == "Settings" else SYMBOLS[name]
        mark = glyph(kind)
        if group == "MobileHud" and name.endswith("Selected"):
            mark = highlight(mark)
        if group == "MobileControls" and name in ACTION_BASE:
            s = button(ACTION_BASE[name], name not in ("Map", "Chat", "ZoomIn", "ZoomOut"))
            inset = 0 if name == "AttackAimUse" else 5
            s.paste(mark, (inset, inset, 32 - 2 * inset, 32 - 2 * inset))
        else:
            return mark
    return s


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    target = parser.parse_args().output
    sizes = json.loads((ASSETS / "sprite-sizes.json").read_text())
    catalog = json.loads((ASSETS / "APK-SPRITES.json").read_text())["assets"]
    if sizes.keys() != catalog.keys():
        raise ValueError("Every mobile sprite requires a drawing and its original dimensions")

    # Extra artwork used only by the editor, including potion previews for
    # desktop XNB formats that the Android preview decoder cannot decode.
    for name in ("Category1", "Category3", "DepositAll", "LootAll", "CraftLock"):
        sizes["MobileInventory/" + name] = [32, 32]
    potions = {"Items/Item_28": "potion-red", "Items/Item_110": "potion-blue", "Items/Item_2349": "potion-gold"}
    sizes.update({name: [24, 28] for name in potions})

    drawings = {}
    for path, size in sizes.items():
        sprite = glyph(potions[path]) if path in potions else render(*path.split("/"))
        viewport = size if path == "MobileInventory/EquipmentGlyphs" else [32, 32]
        drawings[path] = [size, viewport, sprite.ops]
    target.write_text(json.dumps(drawings, separators=(",", ":")), encoding="utf-8")


if __name__ == "__main__":
    main()
