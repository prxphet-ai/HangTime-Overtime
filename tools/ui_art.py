"""The mod's hand-drawn UI pieces, in the style of its team emblems: wobbly marker-drawn panel shapes (a fill and an
outline, both white so the game tints them, sliced so they stretch to any size) and white icons. Drawn from code.

usage: python tools/ui_art.py        # writes assets/ui/*.png and assets/ui/preview.png
The game loads them in src/Patches/UiArt.cs (panel_*: 9-slice border = BORDER px; icons: square, centred).
"""
import math
import os
import random

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "assets", "ui")
SS = 3
WHITE = (255, 255, 255, 255)
CLEAR = (0, 0, 0, 0)

PANEL, RADIUS, INSET, BORDER, LINE = 384, 64, 14, 96, 11      # panel sprite size, corner radius, edge room, slice border, outline px
VARIANTS = 3
ICON = 128


def wobble(pts, amp, seed, step=9.0):
    """subdivide a closed outline and push it sideways by smooth noise: a marker's wavering line"""
    ph = random.Random(seed).uniform(0, 100)
    out = []
    n = len(pts)
    for i in range(n):
        (x0, y0), (x1, y1) = pts[i], pts[(i + 1) % n]
        L = math.hypot(x1 - x0, y1 - y0) or 1.0
        k = max(1, int(L / step))
        nx, ny = -(y1 - y0) / L, (x1 - x0) / L
        for j in range(k):
            t = j / k
            x, y = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
            w = amp * math.sin(ph + x * 0.045 + y * 0.065) * math.cos(ph * 0.7 + x * 0.029 - y * 0.041)
            out.append((x + nx * w, y + ny * w))
    return out


def rrect(x0, y0, x1, y1, r):
    pts = []
    for cx, cy, a0 in ((x1 - r, y0 + r, -90), (x1 - r, y1 - r, 0), (x0 + r, y1 - r, 90), (x0 + r, y0 + r, 180)):
        for i in range(10):
            a = math.radians(a0 + i * 10)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def panel(seed, outline):
    """one panel shape: the fill, or only its outline (a marker line drawn half inside the edge)"""
    img = Image.new("RGBA", (PANEL * SS, PANEL * SS), CLEAR)
    d = ImageDraw.Draw(img)
    pts = [(x * SS, y * SS) for x, y in wobble(rrect(INSET, INSET, PANEL - INSET, PANEL - INSET, RADIUS), 2.6, seed)]
    if outline:
        d.line(pts + [pts[0], pts[1]], fill=WHITE, width=LINE * SS, joint="curve")
    else:
        d.polygon(pts, fill=WHITE)
    return img.resize((PANEL, PANEL), Image.LANCZOS)


# ---------------------------------------------------------------- icons (drawn in a 100 x 100 box centred on 0, 0)

class Pen:
    def __init__(self, seed):
        self.img = Image.new("RGBA", (ICON * SS, ICON * SS), CLEAR)
        self.d = ImageDraw.Draw(self.img)
        self.seed = seed
        self.k = ICON * SS / 100.0
        self.o = ICON * SS / 2

    def P(self, pts):
        return [(self.o + x * self.k, self.o + y * self.k) for x, y in wobble(pts, 1.3, self.seed, 6)]

    def poly(self, pts, fill=WHITE):
        self.d.polygon(self.P(pts), fill=fill)

    def line(self, pts, w=7, fill=WHITE):
        self.d.line([(self.o + x * self.k, self.o + y * self.k) for x, y in pts], fill=fill, width=int(w * self.k), joint="curve")
        for x, y in (pts[0], pts[-1]):
            r = w * self.k / 2
            self.d.ellipse([self.o + x * self.k - r, self.o + y * self.k - r, self.o + x * self.k + r, self.o + y * self.k + r], fill=fill)

    def circ(self, x, y, r, fill=WHITE):
        self.poly([(x + r * math.cos(math.radians(a)), y + r * math.sin(math.radians(a))) for a in range(0, 360, 12)], fill)

    def ring(self, x, y, r, w=6, fill=WHITE):
        self.line([(x + r * math.cos(math.radians(a)), y + r * math.sin(math.radians(a))) for a in range(0, 368, 8)], w, fill)

    def result(self):
        return self.img.resize((ICON, ICON), Image.LANCZOS)


def ic_gear(p):
    pts = []
    for i in range(32):                                    # eight square teeth: two points out, two points in
        a = math.radians(i * 11.25 - 5.6)
        r = 40 if i % 4 in (0, 1) else 29
        pts.append((r * math.cos(a), r * math.sin(a)))
    p.poly(pts)
    p.circ(0, 0, 12, CLEAR)


def ic_sun(p):
    p.circ(0, 0, 19)
    for i in range(8):
        a = math.radians(i * 45)
        p.line([(28 * math.cos(a), 28 * math.sin(a)), (40 * math.cos(a), 40 * math.sin(a))], 7)


def ic_cards(p):
    p.poly([(-32, -22), (4, -36), (18, 22), (-16, 36)])
    p.poly([(-4, -32), (32, -26), (26, 34), (-10, 28)], CLEAR)
    p.poly([(0, -28), (28, -22), (22, 28), (-6, 22)])


def ic_trophy(p):
    p.poly([(-24, -34), (24, -34), (18, 4), (6, 12), (6, 22), (16, 32), (-16, 32), (-6, 22), (-6, 12), (-18, 4)])
    p.line([(-24, -26), (-38, -22), (-32, -2), (-18, 0)], 6)
    p.line([(24, -26), (38, -22), (32, -2), (18, 0)], 6)


def ic_book(p):
    p.poly([(-36, -28), (-3, -32), (-3, 34), (-36, 30)])
    p.poly([(3, -32), (36, -28), (36, 30), (3, 34)])


def ic_film(p):
    p.poly([(-36, -26), (36, -26), (36, 26), (-36, 26)])
    p.poly([(-9, -13), (15, 0), (-9, 13)], CLEAR)


def ic_podium(p):
    p.poly([(-13, -16), (13, -16), (13, 34), (-13, 34)])
    p.poly([(-39, 4), (-16, 4), (-16, 34), (-39, 34)])
    p.poly([(16, 14), (39, 14), (39, 34), (16, 34)])
    p.poly([(0, -40), (4, -32), (13, -31), (7, -25), (9, -17), (0, -21), (-9, -17), (-7, -25), (-13, -31), (-4, -32)])


def ic_pad(p):
    p.poly(rrect(-38, -20, 38, 24, 17))
    p.line([(-24, 2), (-10, 2)], 6, CLEAR)
    p.line([(-17, -5), (-17, 9)], 6, CLEAR)
    p.circ(14, -3, 5, CLEAR)
    p.circ(24, 6, 5, CLEAR)


def ic_bug(p):
    p.poly([(18 * math.cos(math.radians(a)), 6 + 26 * math.sin(math.radians(a))) for a in range(0, 360, 12)])
    p.circ(0, -26, 11)
    for y in (-6, 8, 22):
        p.line([(-18, y), (-34, y - 7)], 5)
        p.line([(18, y), (34, y - 7)], 5)


def ic_play(p):
    p.poly([(-18, -30), (28, 0), (-18, 30)])


def ic_pencil(p):
    p.poly([(-28, 24), (14, -18), (26, -6), (-16, 36), (-32, 38)])
    p.poly([(18, -22), (26, -30), (38, -18), (30, -10)])


def ic_share(p):
    p.line([(-22, 0), (20, -20)], 6)
    p.line([(-22, 0), (20, 20)], 6)
    p.circ(-22, 0, 11)
    p.circ(20, -20, 11)
    p.circ(20, 20, 11)


def ic_bin(p):
    p.poly([(-24, -16), (24, -16), (18, 34), (-18, 34)])
    p.line([(-30, -25), (30, -25)], 7)
    p.line([(-9, -34), (9, -34)], 7)


def ic_globe(p):
    p.ring(0, 0, 34, 7)
    p.line([(15 * math.cos(math.radians(a)), 34 * math.sin(math.radians(a))) for a in range(-90, 92, 6)], 5)
    p.line([(-15 * math.cos(math.radians(a)), 34 * math.sin(math.radians(a))) for a in range(-90, 92, 6)], 5)
    p.line([(-34, 0), (34, 0)], 5)
    p.line([(-28, -17), (28, -17)], 4)
    p.line([(-28, 17), (28, 17)], 4)


def ic_flame(p):
    p.poly([(0, -40), (18, -10), (24, 14), (10, 36), (-12, 36), (-24, 14), (-16, -8), (-6, 4)])


def ic_star(p):
    p.poly([(40 * math.cos(math.radians(-90 + i * 36)) * (1 if i % 2 == 0 else 0.45), 40 * math.sin(math.radians(-90 + i * 36)) * (1 if i % 2 == 0 else 0.45)) for i in range(10)])


def ic_clock(p):
    p.ring(0, 0, 34, 8)
    p.line([(0, 0), (0, -20)], 7)
    p.line([(0, 0), (16, 8)], 7)


def ic_people(p):
    p.circ(-16, -18, 12)
    p.poly([(-36, 30), (-30, 2), (-2, 2), (4, 30)])
    p.circ(18, -14, 11)
    p.poly([(4, 30), (8, 6), (32, 6), (38, 30)])


def ic_back(p):
    p.poly([(-34, 0), (2, -30), (2, -12), (32, -12), (32, 12), (2, 12), (2, 30)])


def ic_plus(p):
    p.line([(-28, 0), (28, 0)], 12)
    p.line([(0, -28), (0, 28)], 12)


def ic_check(p):
    p.line([(-28, 2), (-8, 22), (30, -22)], 12)


def ic_refresh(p):
    p.line([(28 * math.cos(math.radians(a)), 28 * math.sin(math.radians(a))) for a in range(-60, 230, 8)], 8)
    p.poly([(30, -44), (34, -12), (4, -20)])


def ic_ball(p):
    p.circ(0, 0, 36)
    for a0 in (0, 120, 240):
        pts = []
        for t in range(21):
            u = t / 20
            a = math.radians(a0 + 70 * u)
            rr = (0.25 + 0.75 * u) * 36
            pts.append((rr * math.cos(a + 0.6 * math.sin(u * 3)), rr * math.sin(a + 0.6 * math.sin(u * 3))))
        p.line(pts, 5, CLEAR)


def ic_palette(p):
    p.poly([(36 * math.cos(math.radians(a)), 30 * math.sin(math.radians(a))) for a in range(0, 360, 12)])
    for x, y in ((-16, -12), (2, -18), (18, -8), (-20, 8)):
        p.circ(x, y, 6, CLEAR)
    p.circ(14, 14, 9, CLEAR)


ICONS = {"gear": ic_gear, "sun": ic_sun, "cards": ic_cards, "trophy": ic_trophy, "book": ic_book, "film": ic_film,
         "podium": ic_podium, "pad": ic_pad, "bug": ic_bug, "play": ic_play, "pencil": ic_pencil, "share": ic_share,
         "bin": ic_bin, "globe": ic_globe, "flame": ic_flame, "star": ic_star, "clock": ic_clock, "people": ic_people,
         "back": ic_back, "plus": ic_plus, "check": ic_check, "refresh": ic_refresh, "ball": ic_ball, "palette": ic_palette}


def main():
    os.makedirs(OUT, exist_ok=True)
    tiles = []
    for v in range(VARIANTS):
        for kind in ("fill", "line"):
            img = panel(101 + v * 17, kind == "line")
            img.save(os.path.join(OUT, f"panel_{kind}_{v}.png"))
            tiles.append(img)
    for i, (name, fn) in enumerate(ICONS.items()):
        p = Pen(200 + i)
        fn(p)
        img = p.result()
        img.save(os.path.join(OUT, f"icon_{name}.png"))
        tiles.append(img)
    # preview: everything on the menus' navy
    cell = 140
    cols = 8
    rows = (len(tiles) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * cell, rows * cell), (14, 43, 77, 255))
    for i, t in enumerate(tiles):
        im = t.copy()
        im.thumbnail((cell - 16, cell - 16))
        sheet.alpha_composite(im, ((i % cols) * cell + (cell - im.width) // 2, (i // cols) * cell + (cell - im.height) // 2))
    sheet.convert("RGB").save(os.path.join(OUT, "preview.png"))
    print(f"wrote {VARIANTS * 2} panel pieces and {len(ICONS)} icons to assets/ui")


if __name__ == "__main__":
    main()
