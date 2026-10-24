"""Title-screen additions under the game's own "HangTime!" logo (the game's logo itself stays as it is): "OVERTIME" on a
folded orange ribbon with a stopwatch, in the hand-drawn style of the mod's emblems and menus, and the credit on a small
dark tag. Our own art, drawn from code.

usage: python tools/title_logo.py      # writes assets/title/overtime.png and assets/title/credit.png
"""
import os
import random

import math
import sys

from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ui_art import wobble, rrect  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "assets", "title")
FONTS = os.path.join(ROOT, "tools", "fonts")
WHITE = (254, 254, 246, 255)
YELLOW = (254, 208, 61, 255)        # sampled from the game's logo outline
BLUE = (0, 92, 150, 255)            # the menu's dark blue, for the credit outline
ORANGE = (255, 138, 61, 255)        # the menus' accent (tools/ui_art.py, src/Patches/UiArt.cs)
FOLD = (200, 90, 30, 255)
INK = (255, 246, 224, 255)
DARK = (8, 24, 44, 255)
GOLD = (255, 210, 74, 255)
SS = 3


def shape(img, pts, fill, outline=None, width=0, shadow=0, seed=1, amp=2.4, k=1.0, dx=0.0, dy=0.0):
    """a hand-drawn filled shape on img: pts in drawing units, wobbled there, then scaled by k (and moved by dx, dy)"""
    d = ImageDraw.Draw(img)
    P = [((x + dx) * k, (y + dy) * k) for x, y in wobble(pts, amp, seed)]
    if shadow:
        d.polygon([(x + shadow, y + shadow) for x, y in P], fill=(0, 10, 25, 110))
    d.polygon(P, fill=fill)
    if outline and width:
        d.line(P + [P[0], P[1]], fill=outline, width=width, joint="curve")


def ribbon():
    """OVERTIME on a folded ribbon: the band, its two folded ends behind it, a stopwatch, the word"""
    k = 2.5 * SS                                            # the mockup was drawn at 1/2.5 of this size
    W, H = int(420 * k), int(110 * k)
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    o = dict(k=k, dx=-440, dy=-282)
    shape(img, [(468, 318), (488, 310), (492, 360), (460, 374)], FOLD, seed=32, amp=1.4, **o)
    shape(img, [(816, 304), (836, 296), (842, 346), (818, 350)], FOLD, seed=33, amp=1.4, **o)
    shape(img, [(486, 300), (806, 290), (816, 352), (490, 364)], ORANGE, INK, int(5 * k), int(8 * k), seed=31, amp=2.6, **o)
    # the stopwatch
    cx, cy, r = 522, 328, 21
    shape(img, [(cx - r * 0.25, cy - r * 1.32), (cx + r * 0.25, cy - r * 1.32), (cx + r * 0.25, cy - r * 1.0), (cx - r * 0.25, cy - r * 1.0)], INK, seed=4, amp=0.8, **o)
    shape(img, [(cx + r * math.cos(math.radians(a)), cy + r * math.sin(math.radians(a))) for a in range(0, 360, 8)], DARK, INK, int(r * 0.16 * k), int(4 * k), seed=5, amp=1.2, **o)
    shape(img, [(cx - 3, cy + 2), (cx + 3, cy + 2), (cx + r * 0.55, cy - r * 0.55), (cx + r * 0.48, cy - r * 0.62)], GOLD, seed=6, amp=0.4, **o)
    # the word, slightly turned with the ribbon
    word = lettering("OVERTIME", "Bangers-Regular.ttf", int(54 * k), (254, 254, 246, 255), DARK, int(4 * k), tilt=2.0, jitter=4, seed=7)
    word.thumbnail((int(250 * k), int(90 * k)), Image.LANCZOS)
    img.alpha_composite(word, (int((670 - 440) * k - word.width / 2), int((324 - 282) * k - word.height / 2)))
    img = img.resize((W // SS, H // SS), Image.LANCZOS)
    return img.crop(img.getbbox())


def credit_tag():
    k = 2.5 * SS
    W, H = int(220 * k), int(50 * k)
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    shape(img, rrect(10, 10, 210, 36, 8), (8, 24, 44, 235), shadow=int(3 * k), seed=34, amp=1.0, k=k)
    word = lettering("BY AVERAGEPRXPHET", "Bangers-Regular.ttf", int(17 * k), GOLD, (8, 24, 44, 0), 0, tilt=0, jitter=2, seed=3)
    word.thumbnail((int(186 * k), int(24 * k)), Image.LANCZOS)
    img.alpha_composite(word, (int(110 * k - word.width / 2), int(23 * k - word.height / 2)))
    img = img.rotate(1.5, resample=Image.BICUBIC, expand=True)
    img = img.resize((img.width // SS, img.height // SS), Image.LANCZOS)
    return img.crop(img.getbbox())


def lettering(text, font_file, size, fill, stroke, stroke_w, tilt, jitter, seed, rise=0.0):
    """Hand lettering: each letter drawn on its own layer, turned a little and lifted, then the word tilted."""
    rng = random.Random(seed)
    f = ImageFont.truetype(os.path.join(FONTS, font_file), size)
    pad = stroke_w * 2 + size // 4
    layers, x = [], 0
    for ch in text:
        if ch == " ":
            x += int(size * 0.32)
            continue
        bb = f.getbbox(ch, stroke_width=stroke_w)
        w, h = bb[2] - bb[0] + pad * 2, size + pad * 2
        layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        ImageDraw.Draw(layer).text((pad - bb[0], pad), ch, font=f, fill=fill, stroke_width=stroke_w, stroke_fill=stroke)
        layer = layer.rotate(rng.uniform(-jitter, jitter), resample=Image.BICUBIC, expand=True)
        dy = int(rng.uniform(-1, 1) * size * 0.04 - rise * x)
        layers.append((layer, x, dy))
        x += f.getlength(ch) + stroke_w * 0.5
    W = int(x + pad * 4)
    H = size * 2 + int(abs(rise) * x) + pad * 2
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    base = H // 2 - size // 2 + int(abs(rise) * x) // 2
    for layer, lx, dy in layers:
        img.alpha_composite(layer, (int(lx + pad), max(0, base + dy - pad)))
    img = img.rotate(tilt, resample=Image.BICUBIC, expand=True)
    return img.crop(img.getbbox())


def main():
    os.makedirs(OUT, exist_ok=True)
    over = ribbon()
    over.save(os.path.join(OUT, "overtime.png"))
    credit = credit_tag()
    credit.save(os.path.join(OUT, "credit.png"))
    print(f"wrote assets/title/overtime.png {over.size}, credit.png {credit.size}")


if __name__ == "__main__":
    main()
