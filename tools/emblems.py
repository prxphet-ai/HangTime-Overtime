"""Team banner emblems in the style of Hangtime!'s own: one flat light color, chunky hand-drawn
silhouettes, a mascot or symbol, and the team name in bold hand lettering. Drawn from code (no image
generation), from each teams.json row's `emblem` cell: {"mascot": ..., "layout": ..., "font": ...}.

usage: python tools/emblems.py            # writes assets/emblems/<team id>.png and assets/emblems/preview.png
Output is our own art (shipped with the mod). Lettering fonts (tools/fonts): Permanent Marker (Apache 2.0),
Bangers (SIL OFL 1.1).
"""
import json
import math
import os
import random

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "assets", "emblems")
FONTS = {"marker": os.path.join(ROOT, "tools", "fonts", "PermanentMarker-Regular.ttf"),
         "bangers": os.path.join(ROOT, "tools", "fonts", "Bangers-Regular.ttf")}
W, H, SS = 600, 430, 3          # output size and supersampling
INK = (255, 255, 255, 255)
CLEAR = (0, 0, 0, 0)


class Pen:
    """Hand-drawn shapes on a supersampled RGBA canvas (coordinates in output pixels)."""

    def __init__(self, seed):
        self.img = Image.new("RGBA", (W * SS, H * SS), CLEAR)
        self.d = ImageDraw.Draw(self.img)
        self.rng = random.Random(seed)
        self.noise_phase = self.rng.uniform(0, 100)

    def _s(self, pts):
        return [(x * SS, y * SS) for x, y in pts]

    def wobble(self, pts, amp=2.5, step=10.0, closed=True):
        """Subdivide edges and push points sideways by smooth noise: the wavering line of a marker."""
        out = []
        n = len(pts)
        rng = range(n) if closed else range(n - 1)
        for i in rng:
            (x0, y0), (x1, y1) = pts[i], pts[(i + 1) % n]
            L = math.hypot(x1 - x0, y1 - y0)
            k = max(1, int(L / step))
            nx, ny = (-(y1 - y0) / L, (x1 - x0) / L) if L else (0, 0)
            for j in range(k):
                t = j / k
                x, y = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
                w = amp * math.sin(self.noise_phase + (x * 0.05 + y * 0.07)) * math.cos(self.noise_phase * 0.7 + (x * 0.031 - y * 0.043))
                out.append((x + nx * w, y + ny * w))
        if not closed:
            out.append(pts[-1])
        return out

    def poly(self, pts, fill=INK, amp=2.5):
        self.d.polygon(self._s(self.wobble(pts, amp)), fill=fill)

    def ellipse(self, cx, cy, rx, ry, fill=INK, amp=2.0, a0=0.0, a1=360.0):
        n = 48
        pts = [(cx + rx * math.cos(math.radians(a0 + (a1 - a0) * i / n)), cy + ry * math.sin(math.radians(a0 + (a1 - a0) * i / n))) for i in range(n + (0 if a1 - a0 >= 360 else 1))]
        if a1 - a0 < 360:
            pts.append((cx, cy))
        self.poly(pts, fill, amp)

    def stroke(self, pts, width=14.0, fill=INK, amp=1.5):
        pts = self.wobble(pts, amp, closed=False) if len(pts) > 1 else pts
        sp = self._s(pts)
        w = width * SS
        self.d.line(sp, fill=fill, width=int(w), joint="curve")
        r = w / 2
        for x, y in (sp[0], sp[-1]):
            self.d.ellipse([x - r, y - r, x + r, y + r], fill=fill)

    def curve(self, p0, p1, p2, width=14.0, fill=INK, n=24):
        pts = [((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0], (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1])
               for t in (i / n for i in range(n + 1))]
        self.stroke(pts, width, fill)

    def sparkle(self, cx, cy, r, fill=INK):
        """Four-point star like the ones on Kozuki's emblem."""
        self.poly([(cx, cy - r), (cx + r * 0.22, cy - r * 0.22), (cx + r, cy), (cx + r * 0.22, cy + r * 0.22),
                   (cx, cy + r), (cx - r * 0.22, cy + r * 0.22), (cx - r, cy), (cx - r * 0.22, cy - r * 0.22)], fill, 0.8)

    def text(self, s, cx, cy, size, font="marker", fill=INK, spacing=0.92, max_w=None):
        """Hand lettering: each letter slightly turned and lifted, the way the game's own signs are drawn."""
        f = ImageFont.truetype(FONTS[font], int(size * SS))
        widths = [f.getbbox(ch)[2] - f.getbbox(ch)[0] if ch != " " else size * SS * 0.35 for ch in s]
        total = sum(widths) * spacing
        if max_w and total > max_w * SS:
            return self.text(s, cx, cy, size * max_w * SS / total, font, fill, spacing)
        x = cx * SS - total / 2
        for ch, w in zip(s, widths):
            if ch != " ":
                layer = Image.new("RGBA", (int(size * SS * 2), int(size * SS * 2)), CLEAR)
                ImageDraw.Draw(layer).text((size * SS * 0.5, size * SS * 0.3), ch, font=f, fill=fill)
                layer = layer.rotate(self.rng.uniform(-6, 6), resample=Image.BICUBIC)
                bb = f.getbbox(ch)
                self.img.alpha_composite(layer, (int(x - size * SS * 0.5 - bb[0]), int(cy * SS - size * SS * 0.95 + self.rng.uniform(-1.5, 1.5) * SS)))
            x += w * spacing

    def result(self):
        return self.img.resize((W, H), Image.LANCZOS)


# ---------------------------------------------------------------- mascots (drawn in a 300x220 box centred on cx, cy)

def brazier(p, cx, cy, s, accent):
    """Kagaribi: a bonfire basket with a tall three-tongued flame and sparks."""
    p.poly([(cx - 70 * s, cy + 20 * s), (cx + 70 * s, cy + 20 * s), (cx + 52 * s, cy + 58 * s), (cx - 52 * s, cy + 58 * s)])
    for dx in (-40, 0, 40):
        p.stroke([(cx + dx * s, cy + 58 * s), (cx + dx * 1.35 * s, cy + 98 * s)], 13 * s)
    flame = [(cx - 62 * s, cy + 16 * s), (cx - 70 * s, cy - 30 * s), (cx - 38 * s, cy - 8 * s), (cx - 30 * s, cy - 78 * s),
             (cx - 4 * s, cy - 28 * s), (cx + 8 * s, cy - 112 * s), (cx + 30 * s, cy - 30 * s), (cx + 52 * s, cy - 70 * s),
             (cx + 56 * s, cy - 14 * s), (cx + 72 * s, cy - 36 * s), (cx + 64 * s, cy + 16 * s)]
    p.poly(flame, amp=3.5)
    core = [(cx - 30 * s, cy + 16 * s), (cx - 22 * s, cy - 24 * s), (cx - 4 * s, cy - 6 * s), (cx + 6 * s, cy - 58 * s),
            (cx + 22 * s, cy - 8 * s), (cx + 34 * s, cy - 26 * s), (cx + 32 * s, cy + 16 * s)]
    p.poly(core, fill=accent, amp=2.5)
    for x, y, r in ((-92, -60, 11), (94, -84, 9), (-64, -104, 7), (78, -126, 6)):
        p.sparkle(cx + x * s, cy + y * s, r * 1.5 * s)


def penguin(p, cx, cy, s, accent):
    """Hyoga: a round penguin with a scarf, snowflakes around it."""
    p.ellipse(cx, cy + 22 * s, 58 * s, 72 * s)
    p.ellipse(cx, cy - 58 * s, 40 * s, 36 * s)
    p.poly([(cx - 52 * s, cy - 6 * s), (cx - 92 * s, cy + 46 * s), (cx - 50 * s, cy + 32 * s)])
    p.poly([(cx + 52 * s, cy - 6 * s), (cx + 92 * s, cy + 46 * s), (cx + 50 * s, cy + 32 * s)])
    p.ellipse(cx, cy + 32 * s, 36 * s, 50 * s, fill=CLEAR)                 # white belly reads as a cut-out
    p.ellipse(cx - 14 * s, cy - 64 * s, 7 * s, 8 * s, fill=CLEAR)
    p.ellipse(cx + 14 * s, cy - 64 * s, 7 * s, 8 * s, fill=CLEAR)
    p.poly([(cx - 10 * s, cy - 50 * s), (cx + 10 * s, cy - 50 * s), (cx, cy - 36 * s)], fill=accent, amp=1)
    p.poly([(cx - 44 * s, cy - 30 * s), (cx + 44 * s, cy - 30 * s), (cx + 42 * s, cy - 16 * s), (cx - 42 * s, cy - 16 * s)], fill=accent, amp=1.5)
    p.poly([(cx + 22 * s, cy - 20 * s), (cx + 44 * s, cy - 20 * s), (cx + 50 * s, cy + 18 * s), (cx + 30 * s, cy + 14 * s)], fill=accent, amp=1.5)
    for x, y, r in ((-110, -70, 20), (112, -40, 16), (-96, 70, 13)):
        flake(p, cx + x * s, cy + y * s, r * s)
    for dx in (-24, 24):
        p.ellipse(cx + dx * s, cy + 94 * s, 18 * s, 8 * s)


def flake(p, cx, cy, r):
    for a in range(0, 180, 60):
        dx, dy = r * math.cos(math.radians(a)), r * math.sin(math.radians(a))
        p.stroke([(cx - dx, cy - dy), (cx + dx, cy + dy)], max(6, r * 0.36))


def thunder(p, cx, cy, s, accent):
    """Raijin: a big lightning bolt inside a ring of thunder drums."""
    for i in range(8):
        a = math.radians(-200 + i * (220 / 7))
        x, y = cx + 108 * s * math.cos(a), cy + 92 * s * math.sin(a) + 6 * s
        p.ellipse(x, y, 21 * s, 21 * s)
        p.ellipse(x, y, 7 * s, 7 * s, fill=CLEAR)
    bolt = [(cx + 20 * s, cy - 108 * s), (cx - 58 * s, cy + 6 * s), (cx - 6 * s, cy + 6 * s), (cx - 34 * s, cy + 104 * s),
            (cx + 62 * s, cy - 22 * s), (cx + 8 * s, cy - 22 * s), (cx + 46 * s, cy - 108 * s)]
    p.poly(bolt, amp=2)
    inner = [(cx + 18 * s, cy - 84 * s), (cx - 34 * s, cy - 6 * s), (cx + 6 * s, cy - 6 * s), (cx - 14 * s, cy + 62 * s),
             (cx + 38 * s, cy - 10 * s), (cx - 4 * s, cy - 10 * s), (cx + 30 * s, cy - 84 * s)]
    p.poly(inner, fill=accent, amp=1.5)


def swallow(p, cx, cy, s, accent):
    """Fujin: a swallow riding swirling gusts."""
    body = [(cx - 92 * s, cy - 24 * s), (cx - 52 * s, cy - 38 * s), (cx - 10 * s, cy - 30 * s), (cx + 30 * s, cy - 96 * s),
            (cx + 54 * s, cy - 98 * s), (cx + 30 * s, cy - 24 * s), (cx + 70 * s, cy - 6 * s), (cx + 128 * s, cy + 30 * s),
            (cx + 74 * s, cy + 14 * s), (cx + 104 * s, cy + 52 * s), (cx + 44 * s, cy + 14 * s), (cx + 12 * s, cy + 18 * s),
            (cx - 20 * s, cy + 56 * s), (cx - 42 * s, cy + 56 * s), (cx - 30 * s, cy + 6 * s), (cx - 70 * s, cy - 2 * s)]
    p.poly(body, amp=2.5)
    p.ellipse(cx - 66 * s, cy - 24 * s, 6 * s, 6 * s, fill=CLEAR)
    p.poly([(cx - 92 * s, cy - 24 * s), (cx - 110 * s, cy - 18 * s), (cx - 90 * s, cy - 14 * s)], fill=accent, amp=0.5)
    for y, a, b in ((74, -120, 30), (102, -60, 90)):
        p.curve((cx + a * s, cy + y * s), (cx + (a + b) / 2 * s, cy + (y - 24) * s), (cx + b * s, cy + y * s), 14 * s)
    p.curve((cx - 128 * s, cy + 34 * s), (cx - 152 * s, cy - 8 * s), (cx - 118 * s, cy - 42 * s), 14 * s, fill=accent)
    p.curve((cx + 92 * s, cy - 72 * s), (cx + 130 * s, cy - 94 * s), (cx + 142 * s, cy - 52 * s), 14 * s, fill=accent)


def whale(p, cx, cy, s, accent):
    """Kaien: a whale's tail rising out of the sea, with spray."""
    tail = [(cx - 14 * s, cy + 52 * s), (cx - 8 * s, cy - 6 * s), (cx - 34 * s, cy - 40 * s), (cx - 96 * s, cy - 58 * s),
            (cx - 60 * s, cy - 80 * s), (cx - 6 * s, cy - 54 * s), (cx + 6 * s, cy - 66 * s), (cx + 60 * s, cy - 80 * s),
            (cx + 96 * s, cy - 58 * s), (cx + 34 * s, cy - 40 * s), (cx + 8 * s, cy - 6 * s), (cx + 14 * s, cy + 52 * s)]
    p.poly(tail, amp=2.5)
    wave = [(cx - 140 * s + i * 7 * s, cy + 52 * s + 9 * s * math.sin(i * 0.7)) for i in range(41)]
    p.stroke(wave, 12 * s, fill=accent)
    wave2 = [(cx - 110 * s + i * 7 * s, cy + 84 * s + 8 * s * math.sin(i * 0.7 + 1.5)) for i in range(32)]
    p.stroke(wave2, 10 * s)
    for x, y, r in ((-70, -112, 9), (-40, -126, 7), (52, -118, 8), (82, -102, 6)):
        p.ellipse(cx + x * s, cy + y * s, r * s, r * 1.3 * s)


def mountain(p, cx, cy, s, accent):
    """Iwao: rocky peaks with a crossed hammer and pick."""
    p.poly([(cx - 130 * s, cy + 70 * s), (cx - 60 * s, cy - 50 * s), (cx - 20 * s, cy + 10 * s), (cx + 20 * s, cy - 96 * s),
            (cx + 74 * s, cy - 10 * s), (cx + 96 * s, cy - 40 * s), (cx + 136 * s, cy + 70 * s)], amp=3)
    p.poly([(cx + 2 * s, cy - 64 * s), (cx + 20 * s, cy - 96 * s), (cx + 40 * s, cy - 64 * s), (cx + 28 * s, cy - 56 * s),
            (cx + 18 * s, cy - 66 * s), (cx + 10 * s, cy - 56 * s)], fill=CLEAR, amp=1)
    p.stroke([(cx - 96 * s, cy + 104 * s), (cx + 36 * s, cy - 2 * s)], 18 * s, fill=accent)
    p.poly([(cx - 14 * s, cy - 44 * s), (cx + 30 * s, cy - 32 * s), (cx + 84 * s, cy + 30 * s), (cx + 70 * s, cy + 38 * s),
            (cx + 26 * s, cy - 10 * s), (cx - 22 * s, cy - 30 * s)], fill=accent, amp=1.5)


def bat_moon(p, cx, cy, s, accent):
    """Yomi: a bat across a crescent moon."""
    p.ellipse(cx + 20 * s, cy - 10 * s, 92 * s, 92 * s, fill=accent, amp=2)
    p.ellipse(cx + 58 * s, cy - 32 * s, 82 * s, 82 * s, fill=CLEAR, amp=2)
    bat = [(cx - 130 * s, cy - 10 * s), (cx - 92 * s, cy - 40 * s), (cx - 70 * s, cy - 20 * s), (cx - 56 * s, cy - 44 * s),
           (cx - 30 * s, cy - 26 * s), (cx - 20 * s, cy - 48 * s), (cx - 12 * s, cy - 34 * s), (cx - 4 * s, cy - 34 * s),
           (cx + 4 * s, cy - 48 * s), (cx + 14 * s, cy - 26 * s), (cx + 40 * s, cy - 44 * s), (cx + 54 * s, cy - 20 * s),
           (cx + 76 * s, cy - 40 * s), (cx + 114 * s, cy - 10 * s), (cx + 80 * s, cy - 2 * s), (cx + 60 * s, cy + 18 * s),
           (cx + 36 * s, cy + 4 * s), (cx + 14 * s, cy + 22 * s), (cx, cy + 40 * s), (cx - 14 * s, cy + 22 * s),
           (cx - 36 * s, cy + 4 * s), (cx - 60 * s, cy + 18 * s), (cx - 80 * s, cy - 2 * s)]
    p.poly(bat, amp=2)
    p.ellipse(cx - 7 * s, cy - 18 * s, 3.5 * s, 4 * s, fill=CLEAR, amp=0.3)
    p.ellipse(cx + 7 * s, cy - 18 * s, 3.5 * s, 4 * s, fill=CLEAR, amp=0.3)
    for x, y, r in ((-110, -88, 10), (110, 70, 8), (-88, 80, 6)):
        p.sparkle(cx + x * s, cy + y * s, r * 1.5 * s)


def sun_crown(p, cx, cy, s, accent):
    """Amaterasu: a rayed sun wearing a crown."""
    for i in range(12):
        a = math.radians(i * 30 + 15)
        r0, r1 = 66 * s, (108 if i % 2 else 94) * s
        w = math.radians(8)
        p.poly([(cx + r0 * math.cos(a - w), cy + 12 * s + r0 * math.sin(a - w)), (cx + r1 * math.cos(a), cy + 12 * s + r1 * math.sin(a)),
                (cx + r0 * math.cos(a + w), cy + 12 * s + r0 * math.sin(a + w))], amp=1)
    p.ellipse(cx, cy + 12 * s, 58 * s, 58 * s)
    p.ellipse(cx, cy + 12 * s, 40 * s, 40 * s, fill=accent)
    crown = [(cx - 50 * s, cy - 30 * s), (cx - 58 * s, cy - 92 * s), (cx - 26 * s, cy - 62 * s), (cx, cy - 104 * s),
             (cx + 26 * s, cy - 62 * s), (cx + 58 * s, cy - 92 * s), (cx + 50 * s, cy - 30 * s)]
    p.poly(crown, amp=1.5)
    for x in (-58, 0, 58):
        p.ellipse(cx + x * s, cy + (-96 if x else -110) * s, 8 * s, 8 * s)


def blossom(p, cx, cy, s, accent):
    """Sakuradai: a five-petal cherry blossom with falling petals."""
    for i in range(5):
        a = math.radians(-90 + i * 72)
        px, py = cx + 46 * s * math.cos(a), cy + 46 * s * math.sin(a)
        tip = (cx + 92 * s * math.cos(a), cy + 92 * s * math.sin(a))
        side = math.radians(90)
        p.poly([(cx + 10 * s * math.cos(a), cy + 10 * s * math.sin(a)),
                (px + 34 * s * math.cos(a - side), py + 34 * s * math.sin(a - side)),
                (tip[0] + 14 * s * math.cos(a - side), tip[1] + 14 * s * math.sin(a - side)),
                (tip[0] - 6 * s * math.cos(a), tip[1] - 6 * s * math.sin(a)),                 # notch at the petal tip
                (tip[0] + 14 * s * math.cos(a + side), tip[1] + 14 * s * math.sin(a + side)),
                (px + 34 * s * math.cos(a + side), py + 34 * s * math.sin(a + side))], amp=2)
    p.ellipse(cx, cy, 24 * s, 24 * s, fill=accent)
    for i in range(5):
        a = math.radians(-90 + i * 72 + 36)
        p.ellipse(cx + 18 * s * math.cos(a), cy + 18 * s * math.sin(a), 5 * s, 5 * s, fill=CLEAR, amp=0.3)
    for x, y, r, rot in ((-124, -50, 14, 20), (118, 40, 12, -30), (-96, 76, 10, 50), (128, -86, 9, 10)):
        p.ellipse(cx + x * s, cy + y * s, r * s, r * 0.6 * s, fill=accent)


def bullet_train(p, cx, cy, s, accent):
    """Kaminari: a bullet train racing out of a lightning streak."""
    nose = [(cx - 140 * s, cy + 40 * s), (cx + 40 * s, cy + 40 * s), (cx + 120 * s, cy + 22 * s), (cx + 150 * s, cy + 6 * s),
            (cx + 120 * s, cy - 18 * s), (cx + 50 * s, cy - 38 * s), (cx - 140 * s, cy - 38 * s)]
    p.poly(nose, amp=2)
    p.poly([(cx + 52 * s, cy - 28 * s), (cx + 108 * s, cy - 12 * s), (cx + 96 * s, cy - 2 * s), (cx + 46 * s, cy - 6 * s)], fill=CLEAR, amp=1)
    for x in (-110, -70, -30, 10):
        p.poly([(cx + x * s, cy - 24 * s), (cx + (x + 28) * s, cy - 24 * s), (cx + (x + 28) * s, cy - 6 * s), (cx + x * s, cy - 6 * s)], fill=CLEAR, amp=0.6)
    p.stroke([(cx - 140 * s, cy + 14 * s), (cx + 128 * s, cy + 14 * s)], 9 * s, fill=accent)
    p.stroke([(cx - 150 * s, cy + 62 * s), (cx + 150 * s, cy + 62 * s)], 10 * s)
    for y, x0 in ((-62, -60), (-88, -20), (88, -100)):
        p.stroke([(cx + x0 * s, cy + y * s), (cx + (x0 - 70) * s, cy + y * s)], 9 * s, fill=accent)
    p.poly([(cx + 4 * s, cy - 120 * s), (cx - 30 * s, cy - 68 * s), (cx - 6 * s, cy - 68 * s), (cx - 24 * s, cy - 40 * s),
            (cx + 26 * s, cy - 84 * s), (cx + 2 * s, cy - 84 * s), (cx + 20 * s, cy - 120 * s)], fill=accent, amp=1)


def rooster(p, cx, cy, s, accent):
    """Hinode: a crowing rooster in front of a rising sun."""
    for i in range(7):
        a = math.radians(190 + i * 26.6)
        p.stroke([(cx + 96 * s * math.cos(a), cy + 70 * s + 96 * s * math.sin(a)), (cx + 136 * s * math.cos(a), cy + 70 * s + 136 * s * math.sin(a))], 11 * s, fill=accent)
    body = [(cx - 70 * s, cy + 60 * s), (cx - 96 * s, cy - 10 * s), (cx - 70 * s, cy - 70 * s), (cx - 40 * s, cy - 40 * s),
            (cx - 50 * s, cy + 4 * s), (cx - 10 * s, cy - 10 * s), (cx + 10 * s, cy - 60 * s), (cx + 26 * s, cy - 92 * s),
            (cx + 52 * s, cy - 84 * s), (cx + 78 * s, cy - 92 * s), (cx + 58 * s, cy - 70 * s), (cx + 50 * s, cy - 40 * s),
            (cx + 62 * s, cy + 6 * s), (cx + 40 * s, cy + 50 * s)]
    p.poly(body, amp=2.5)
    p.poly([(cx + 26 * s, cy - 94 * s), (cx + 34 * s, cy - 120 * s), (cx + 44 * s, cy - 100 * s), (cx + 56 * s, cy - 124 * s),
            (cx + 60 * s, cy - 92 * s)], fill=accent, amp=1)
    p.poly([(cx + 76 * s, cy - 88 * s), (cx + 100 * s, cy - 82 * s), (cx + 76 * s, cy - 76 * s)], fill=accent, amp=0.5)
    p.ellipse(cx + 48 * s, cy - 76 * s, 5 * s, 5 * s, fill=CLEAR, amp=0.3)
    for dx in (-30, 10):
        p.stroke([(cx + dx * s, cy + 50 * s), (cx + dx * s, cy + 90 * s), (cx + (dx + 14) * s, cy + 96 * s)], 10 * s)
    p.stroke([(cx - 150 * s, cy + 70 * s), (cx + 150 * s, cy + 70 * s)], 10 * s, fill=accent)


def snow_cat(p, cx, cy, s, accent):
    """Yukimura: a snow-leopard cat face with a snowflake on its brow."""
    head = [(cx - 92 * s, cy + 30 * s), (cx - 100 * s, cy - 40 * s), (cx - 84 * s, cy - 112 * s), (cx - 40 * s, cy - 70 * s),
            (cx + 40 * s, cy - 70 * s), (cx + 84 * s, cy - 112 * s), (cx + 100 * s, cy - 40 * s), (cx + 92 * s, cy + 30 * s),
            (cx + 50 * s, cy + 76 * s), (cx - 50 * s, cy + 76 * s)]
    p.poly(head, amp=2.5)
    p.poly([(cx - 78 * s, cy - 90 * s), (cx - 50 * s, cy - 68 * s), (cx - 82 * s, cy - 60 * s)], fill=accent, amp=0.6)
    p.poly([(cx + 78 * s, cy - 90 * s), (cx + 50 * s, cy - 68 * s), (cx + 82 * s, cy - 60 * s)], fill=accent, amp=0.6)
    for dx in (-38, 38):
        p.poly([(cx + (dx - 22) * s, cy - 6 * s), (cx + dx * s, cy - 22 * s), (cx + (dx + 22) * s, cy - 6 * s), (cx + dx * s, cy + 8 * s)], fill=CLEAR, amp=0.6)
        p.ellipse(cx + dx * s, cy - 7 * s, 4 * s, 9 * s, fill=accent, amp=0.3)
    p.poly([(cx - 12 * s, cy + 20 * s), (cx + 12 * s, cy + 20 * s), (cx, cy + 32 * s)], fill=CLEAR, amp=0.5)
    for side in (-1, 1):
        for dy in (-4, 12):
            p.stroke([(cx + side * 30 * s, cy + (30 + dy * 0.3) * s), (cx + side * 120 * s, cy + (20 + dy) * s)], 6 * s)
    flake(p, cx, cy - 46 * s, 14 * s)


def sea_dragon(p, cx, cy, s, accent):
    """Ryujin: a coiling sea dragon rising from waves."""
    coil = [(cx - 120 * s + i * 12 * s, cy + 20 * s + 34 * s * math.sin(i * 0.55)) for i in range(18)]
    p.stroke(coil, 30 * s)
    for i in range(1, 17, 2):
        x, y = coil[i]
        p.poly([(x - 8 * s, y - 12 * s), (x, y - 30 * s), (x + 8 * s, y - 12 * s)], fill=accent, amp=0.6)
    hx, hy = coil[-1][0] + 18 * s, coil[-1][1] - 30 * s
    head = [(hx - 30 * s, hy + 30 * s), (hx - 34 * s, hy - 10 * s), (hx - 10 * s, hy - 34 * s), (hx + 30 * s, hy - 30 * s),
            (hx + 62 * s, hy - 12 * s), (hx + 36 * s, hy), (hx + 60 * s, hy + 12 * s), (hx + 20 * s, hy + 22 * s)]
    p.poly(head, amp=2)
    p.ellipse(hx + 6 * s, hy - 14 * s, 5 * s, 5 * s, fill=CLEAR, amp=0.3)
    p.curve((hx - 10 * s, hy - 34 * s), (hx - 30 * s, hy - 70 * s), (hx - 60 * s, hy - 72 * s), 8 * s, fill=accent)
    p.curve((hx + 6 * s, hy - 32 * s), (hx + 10 * s, hy - 74 * s), (hx - 16 * s, hy - 92 * s), 8 * s, fill=accent)
    wave = [(cx - 150 * s + i * 7 * s, cy + 84 * s + 9 * s * math.sin(i * 0.7)) for i in range(44)]
    p.stroke(wave, 11 * s, fill=accent)


def ghost(p, cx, cy, s, accent):
    """Mugen: a yurei ghost with wisps of spirit fire."""
    body = [(cx - 62 * s, cy + 70 * s), (cx - 70 * s, cy - 20 * s), (cx - 50 * s, cy - 84 * s), (cx, cy - 104 * s),
            (cx + 50 * s, cy - 84 * s), (cx + 70 * s, cy - 20 * s), (cx + 76 * s, cy + 60 * s), (cx + 50 * s, cy + 40 * s),
            (cx + 30 * s, cy + 80 * s), (cx + 6 * s, cy + 44 * s), (cx - 18 * s, cy + 86 * s), (cx - 38 * s, cy + 44 * s)]
    p.poly(body, amp=3)
    p.ellipse(cx - 24 * s, cy - 36 * s, 12 * s, 16 * s, fill=CLEAR, amp=0.6)
    p.ellipse(cx + 24 * s, cy - 36 * s, 12 * s, 16 * s, fill=CLEAR, amp=0.6)
    p.ellipse(cx, cy + 6 * s, 14 * s, 9 * s, fill=CLEAR, amp=0.6)
    p.poly([(cx - 64 * s, cy - 6 * s), (cx - 120 * s, cy + 20 * s), (cx - 66 * s, cy + 20 * s)], amp=1.5)
    p.poly([(cx + 66 * s, cy - 6 * s), (cx + 122 * s, cy + 6 * s), (cx + 70 * s, cy + 22 * s)], amp=1.5)
    for x, y, r in ((-112, -70, 20), (114, -84, 16), (118, 64, 13)):
        p.poly([(cx + (x - r * 0.7) * s, cy + y * s), (cx + x * s, cy + (y - r * 1.9) * s), (cx + (x + r * 0.7) * s, cy + y * s),
                (cx + x * s, cy + (y + r * 0.7) * s)], fill=accent, amp=1)


def star(p, cx, cy, s, accent):
    """Hoshikuzu: a shooting star with a sparkling tail."""
    pts = []
    for i in range(10):
        a = math.radians(-90 + i * 36)
        r = (70 if i % 2 == 0 else 30) * s
        pts.append((cx + 30 * s + r * math.cos(a), cy - 10 * s + r * math.sin(a)))
    for k, (w, dy) in enumerate(((16, -14), (12, 8), (9, 26))):
        p.stroke([(cx - 40 * s, cy + (dy - 4) * s), (cx - 140 * s, cy + (dy + 30) * s)], w * s, fill=accent if k == 1 else INK)
    p.poly(pts, amp=1.5)
    p.ellipse(cx + 30 * s, cy - 8 * s, 14 * s, 14 * s, fill=accent, amp=0.6)
    for x, y, r in ((100, -90, 10), (120, 50, 8), (-60, -80, 9), (-130, 80, 7)):
        p.sparkle(cx + x * s, cy + y * s, r * s)


def oni(p, cx, cy, s, accent):
    """Oni Gakuen: an oni mask with horns and fangs."""
    face = [(cx - 76 * s, cy - 50 * s), (cx - 60 * s, cy - 84 * s), (cx, cy - 98 * s), (cx + 60 * s, cy - 84 * s), (cx + 76 * s, cy - 50 * s),
            (cx + 80 * s, cy + 20 * s), (cx + 50 * s, cy + 80 * s), (cx, cy + 96 * s), (cx - 50 * s, cy + 80 * s), (cx - 80 * s, cy + 20 * s)]
    p.poly(face, amp=2.5)
    for side in (-1, 1):
        p.poly([(cx + side * 40 * s, cy - 88 * s), (cx + side * 92 * s, cy - 140 * s), (cx + side * 70 * s, cy - 76 * s)], fill=accent, amp=1)
        p.poly([(cx + side * 16 * s, cy - 30 * s), (cx + side * 56 * s, cy - 44 * s), (cx + side * 50 * s, cy - 16 * s),
                (cx + side * 20 * s, cy - 14 * s)], fill=CLEAR, amp=0.8)
        p.poly([(cx + side * 10 * s, cy - 56 * s), (cx + side * 60 * s, cy - 66 * s), (cx + side * 54 * s, cy - 56 * s)], fill=CLEAR, amp=0.6)
    mouth = [(cx - 50 * s, cy + 30 * s), (cx + 50 * s, cy + 30 * s), (cx + 40 * s, cy + 62 * s), (cx - 40 * s, cy + 62 * s)]
    p.poly(mouth, fill=CLEAR, amp=1)
    for side in (-1, 1):
        p.poly([(cx + side * 34 * s, cy + 30 * s), (cx + side * 22 * s, cy + 30 * s), (cx + side * 28 * s, cy + 54 * s)], amp=0.4)
        p.poly([(cx + side * 16 * s, cy + 62 * s), (cx + side * 4 * s, cy + 62 * s), (cx + side * 10 * s, cy + 44 * s)], amp=0.4)
    for x, y, r in ((-120, -40, 16), (124, 10, 13)):
        p.poly([(cx + (x - r * 0.7) * s, cy + y * s), (cx + x * s, cy + (y - r * 1.9) * s), (cx + (x + r * 0.7) * s, cy + y * s),
                (cx + x * s, cy + (y + r * 0.7) * s)], fill=accent, amp=1)


def spider(p, cx, cy, s, accent):
    """Kumo: a spider hanging in its web."""
    for i in range(8):
        a = math.radians(-90 + i * 45)
        p.stroke([(cx, cy), (cx + 130 * s * math.cos(a), cy + 110 * s * math.sin(a))], 5 * s, fill=accent)
    for r in (40, 80, 118):
        ring = [(cx + r * s * math.cos(math.radians(-90 + i * 45)), cy + r * 0.85 * s * math.sin(math.radians(-90 + i * 45))) for i in range(9)]
        p.stroke(ring, 5 * s, fill=accent)
    for side in (-1, 1):
        for k, (dx1, dy1, dx2, dy2) in enumerate(((30, -20, 64, -56), (34, -6, 76, -20), (34, 8, 76, 28), (30, 20, 62, 62))):
            p.stroke([(cx + side * 14 * s, cy + (dy1 / 3) * s), (cx + side * dx1 * s, cy + dy1 * s), (cx + side * dx2 * s, cy + dy2 * s)], 9 * s)
    p.ellipse(cx, cy + 14 * s, 26 * s, 32 * s)
    p.ellipse(cx, cy - 24 * s, 18 * s, 16 * s)
    p.ellipse(cx - 7 * s, cy - 26 * s, 4 * s, 4 * s, fill=CLEAR, amp=0.3)
    p.ellipse(cx + 7 * s, cy - 26 * s, 4 * s, 4 * s, fill=CLEAR, amp=0.3)
    p.poly([(cx - 8 * s, cy + 6 * s), (cx + 8 * s, cy + 6 * s), (cx + 12 * s, cy + 18 * s), (cx, cy + 30 * s), (cx - 12 * s, cy + 18 * s)], fill=accent, amp=0.5)
    p.stroke([(cx, cy - 40 * s), (cx, cy - 110 * s)], 5 * s)


def turtle(p, cx, cy, s, accent):
    """Tsunagi: a sturdy turtle with a patterned shell."""
    p.ellipse(cx, cy, 92 * s, 62 * s, a0=180, a1=360)
    p.poly([(cx - 104 * s, cy + 2 * s), (cx + 104 * s, cy + 2 * s), (cx + 96 * s, cy + 16 * s), (cx - 96 * s, cy + 16 * s)], amp=1)
    for (x, y) in ((-46, -24), (0, -40), (46, -24)):
        hexp = [(cx + (x + 18 * math.cos(math.radians(a))) * s, cy + (y + 15 * math.sin(math.radians(a))) * s) for a in range(0, 360, 60)]
        p.poly(hexp, fill=accent, amp=0.8)
    p.ellipse(cx + 122 * s, cy - 18 * s, 26 * s, 22 * s)
    p.ellipse(cx + 130 * s, cy - 24 * s, 4.5 * s, 4.5 * s, fill=CLEAR, amp=0.3)
    for x in (-66, -20, 30, 74):
        p.poly([(cx + (x - 14) * s, cy + 14 * s), (cx + (x + 14) * s, cy + 14 * s), (cx + (x + 12) * s, cy + 46 * s), (cx + (x - 12) * s, cy + 46 * s)], amp=1)
    p.poly([(cx - 96 * s, cy + 6 * s), (cx - 128 * s, cy + 20 * s), (cx - 96 * s, cy + 18 * s)], amp=0.6)
    p.stroke([(cx - 150 * s, cy + 62 * s), (cx + 150 * s, cy + 62 * s)], 10 * s, fill=accent)


MASCOTS = {"star": star, "oni": oni, "spider": spider, "turtle": turtle, "blossom": blossom, "bullet_train": bullet_train, "rooster": rooster, "snow_cat": snow_cat,
           "sea_dragon": sea_dragon, "ghost": ghost, "brazier": brazier, "penguin": penguin, "thunder": thunder, "swallow": swallow, "whale": whale,
           "mountain": mountain, "bat_moon": bat_moon, "sun_crown": sun_crown}


# ---------------------------------------------------------------- layouts

def light(hex_color, amount):
    c = tuple(int(hex_color[i:i + 2], 16) for i in (0, 2, 4))
    return tuple(int(v + (255 - v) * amount) for v in c) + (255,)


def draw(team):
    e = team["emblem"]
    p = Pen(hash(team["id"]) & 0xFFFF)
    accent = light(e.get("accent", team["jersey"]), 0.35)
    name = team["name"].upper()
    words = name.split()
    font = e.get("font", "marker")
    mascot = MASCOTS[e["mascot"]]
    if e["layout"] == "top":                       # name over the mascot (Daigan, Shirogane, Hoshiyumi)
        p.text(name, W / 2, 80, 76, font, max_w=W - 30)
        mascot(p, W / 2, 284, 1.22, accent)
    elif e["layout"] == "stacked":                 # first word over, second word under (Daigan "DAIGAN / TECH")
        p.text(words[0], W / 2, 76, 78, font, max_w=W - 50)
        mascot(p, W / 2, 236, 0.92, accent)
        p.text(" ".join(words[1:]), W / 2, 380 if font == "marker" else 396, 62, font, max_w=W - 80)
    else:                                          # "split": mascot in the middle, words left and right (Hinami Kai)
        mascot(p, W / 2, 196, 1.3, accent)
        base = 378 if font == "marker" else 395
        p.text(words[0], W / 2, base + 5, 70, font, max_w=W - 40) if len(words) == 1 else None
        if len(words) > 1:
            p.text(words[0], W * 0.27, base, 62, font, max_w=W * 0.48)
            p.text(" ".join(words[1:]), W * 0.73, base, 62, font, max_w=W * 0.48)
    return p.result()


def main():
    teams = json.load(open(os.path.join(ROOT, "sheets", "teams.json"), encoding="utf-8"))["rows"]
    os.makedirs(OUT, exist_ok=True)
    tiles = []
    for t in teams:
        img = draw(t)
        box = img.split()[3].getbbox()
        if box and (box[0] <= 1 or box[1] <= 1 or box[2] >= W - 1 or box[3] >= H - 1):
            print(f"WARNING {t['id']}: emblem touches the edge {box}")
        img.save(os.path.join(OUT, t["id"] + ".png"))
        tiles.append((t, img))
        print("wrote assets/emblems/" + t["id"] + ".png")
    # preview: each emblem on its tinted banner cloth color, large and small
    cols, cell = 4, 320
    rows = (len(tiles) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell, rows * (cell + 80)), (36, 56, 88))
    for i, (t, img) in enumerate(tiles):
        x, y = (i % cols) * cell, (i // cols) * (cell + 80)
        bg = Image.new("RGB", (cell - 10, cell - 10), tuple(int(t["banner"][k:k + 2], 16) for k in (0, 2, 4)))
        sheet.paste(bg, (x + 5, y + 5))
        big = img.copy()
        big.thumbnail((cell - 30, cell - 30))
        sheet.paste(big, (x + (cell - big.width) // 2, y + (cell - big.height) // 2), big)
        small = img.copy()
        small.thumbnail((64, 64))
        sheet.paste(small, (x + 10, y + cell + 6), small)
    sheet.save(os.path.join(OUT, "preview.png"))
    print("wrote assets/emblems/preview.png")


if __name__ == "__main__":
    main()
