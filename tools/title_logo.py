"""Title-screen additions under the game's own "HangTime!" logo: an "OVERTIME" word in the logo's style (white letters,
thick yellow outline, each letter slightly turned) and the credit line. Our own art, drawn from code.

usage: python tools/title_logo.py      # writes assets/title/overtime.png and assets/title/credit.png
"""
import os
import random

from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "assets", "title")
FONTS = os.path.join(ROOT, "tools", "fonts")
WHITE = (254, 254, 246, 255)
YELLOW = (254, 208, 61, 255)        # sampled from the game's logo outline
BLUE = (0, 92, 150, 255)            # the menu's dark blue, for the credit outline


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
    over = lettering("OVERTIME", "Bangers-Regular.ttf", 220, WHITE, YELLOW, 22, tilt=4, jitter=5, seed=7)
    over.save(os.path.join(OUT, "overtime.png"))
    credit = lettering("by averageprxphet", "PermanentMarker-Regular.ttf", 90, WHITE, BLUE, 7, tilt=2, jitter=3, seed=3)
    credit.save(os.path.join(OUT, "credit.png"))
    print(f"wrote assets/title/overtime.png {over.size}, credit.png {credit.size}")


if __name__ == "__main__":
    main()
