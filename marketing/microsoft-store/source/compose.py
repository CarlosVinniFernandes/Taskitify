"""Composes the Microsoft Store campaign (3840 x 2160 PNG) from real Windows captures
(raw-captures/desktop), the app's own rendered views (raw-captures/components, made by
`dotnet run --project marketing/microsoft-store/source -c Release -- . --campaign`) and
CC0 photographs (raw-captures/photos).

    python marketing/microsoft-store/source/compose.py
"""
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
STORE = os.path.join(ROOT, "marketing", "microsoft-store")
RAW = os.path.join(STORE, "raw-captures")
FONTS = os.path.join(ROOT, "src", "Taskitify", "Resources", "Fonts")
OUT = os.path.join(STORE, "campaign")
W, H = 3840, 2160
CREAM = (241, 242, 237)
TURQUOISE = (24, 155, 179)

# The Taskitify player docks just left of the taskbar's overflow chevron (x 1626 at 1x).
DOCK_RIGHT, TASKBAR_TOP = 1614, 1152


def raw(*parts):
    return Image.open(os.path.join(RAW, *parts)).convert("RGBA")


def comp(name):
    return raw("components", name + ".png")


def font(weight, size):
    return ImageFont.truetype(os.path.join(FONTS, f"InterDisplay-{weight}.ttf"), size)


def gradient(stops, size=(W, H), angle=35):
    """Linear gradient through (position, rgb) stops, drawn small and smoothly enlarged."""
    sw, sh = 384, 216
    img = Image.new("RGB", (sw, sh))
    px = img.load()
    a = math.radians(angle)
    dx, dy = math.cos(a), math.sin(a)
    span = abs(dx) * sw + abs(dy) * sh
    for y in range(sh):
        for x in range(sw):
            t = max(0.0, min(1.0, (x * dx + y * dy) / span))
            for i in range(len(stops) - 1):
                p0, c0 = stops[i]
                p1, c1 = stops[i + 1]
                if p0 <= t <= p1:
                    f = (t - p0) / (p1 - p0) if p1 > p0 else 0
                    px[x, y] = tuple(int(c0[k] + (c1[k] - c0[k]) * f) for k in range(3))
                    break
    return img.resize(size, Image.BICUBIC).convert("RGBA")


def glow(base, centre, radius, color, alpha):
    layer = Image.new("RGBA", base.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    cx, cy = centre
    d.ellipse((cx - radius, cy - radius * .72, cx + radius, cy + radius * .72), fill=color + (alpha,))
    layer = layer.filter(ImageFilter.GaussianBlur(radius * .45))
    base.alpha_composite(layer)


def grain(img, amount=3, seed=7):
    """A faint grain against banding in large gradients."""
    rnd = random.Random(seed)
    noise = Image.frombytes("L", (512, 512), bytes(rnd.randint(128 - amount, 128 + amount) for _ in range(512 * 512)))
    tile = Image.new("L", img.size)
    for y in range(0, img.size[1], 512):
        for x in range(0, img.size[0], 512):
            tile.paste(noise, (x, y))
    from PIL import ImageChops
    r, g, b = img.convert("RGB").split()
    out = Image.merge("RGB", [ImageChops.add(c, tile, 1, -128) for c in (r, g, b)])
    return out.convert("RGBA")


def atmosphere(warm=0.0):
    """The approved petrol-to-gold atmosphere."""
    gold = (int(140 + 30 * warm), int(138 + 4 * warm), int(102 - 10 * warm))
    img = gradient([(0, (13, 34, 46)), (.35, (20, 45, 60)), (.62, (29, 67, 82)), (.84, (53, 107, 121)), (1, gold)])
    glow(img, (2700, 700), 1500, TURQUOISE, 60)
    glow(img, (3700, 1800), 1400, (175, 155, 103), 120)
    glow(img, (250, 250), 1400, (8, 22, 30), 150)
    return img


def rounded(img, radius):
    mask = Image.new("L", img.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, img.size[0] - 1, img.size[1] - 1), radius, fill=255)
    out = img.copy()
    out.putalpha(Image.composite(img.getchannel("A"), Image.new("L", img.size, 0), mask))
    return out


def shadow(base, img, pos, blur=60, offset=(0, 30), opacity=150):
    a = img.getchannel("A").point(lambda v: v * opacity // 255)
    pad = blur * 3
    layer = Image.new("L", (img.size[0] + pad * 2, img.size[1] + pad * 2), 0)
    layer.paste(a, (pad, pad))
    layer = layer.filter(ImageFilter.GaussianBlur(blur))
    black = Image.new("RGBA", layer.size, (4, 12, 18, 255))
    black.putalpha(layer)
    base.alpha_composite(black, (int(pos[0] - pad + offset[0]), int(pos[1] - pad + offset[1])))


def place(base, img, pos, blur=60, offset=(0, 30), opacity=150, angle=0):
    if angle:
        img = img.rotate(angle, resample=Image.BICUBIC, expand=True)
    if blur:
        shadow(base, img, pos, blur, offset, opacity)
    base.alpha_composite(img, (int(pos[0]), int(pos[1])))


def scale(img, factor):
    return img.resize((round(img.size[0] * factor), round(img.size[1] * factor)), Image.LANCZOS)


def centred_text(base, text, weight, size, baseline, color=CREAM, opacity=255, x_centre=W / 2, shade=True):
    f = font(weight, size)
    ascent, _ = f.getmetrics()
    width = f.getlength(text)
    x, y = x_centre - width / 2, baseline - ascent
    if shade:
        layer = Image.new("RGBA", base.size, (0, 0, 0, 0))
        ImageDraw.Draw(layer).text((x, y + size * .04), text, font=f, fill=(5, 14, 20, 90))
        base.alpha_composite(layer.filter(ImageFilter.GaussianBlur(size * .22)))
    layer = Image.new("RGBA", base.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).text((x, y), text, font=f, fill=color + (opacity,))
    base.alpha_composite(layer)


def outline(base, box, radius, width=3, alpha=150, glow_alpha=110):
    layer = Image.new("RGBA", base.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).rounded_rectangle(box, radius, outline=TURQUOISE + (glow_alpha,), width=width * 6)
    base.alpha_composite(layer.filter(ImageFilter.GaussianBlur(width * 6)))
    layer = Image.new("RGBA", base.size, (0, 0, 0, 0))
    ImageDraw.Draw(layer).rounded_rectangle(box, radius, outline=TURQUOISE + (alpha,), width=width)
    base.alpha_composite(layer)


# ---- Real captures ----------------------------------------------------------------------------

def desktop(name):
    """A real 1920 x 1200 capture, with personal details covered (account pictures, names)."""
    img = raw("desktop", name + ".png")
    d = ImageDraw.Draw(img)
    if name in ("browse-1", "study-1"):
        # The temporary Edge profile picked up the Windows account picture.
        d.ellipse((1592, 115, 1621, 144), fill=img.getpixel((1588, 129)))
    if name == "work-1":
        # Excel's title bar: activation notice and the account name and picture.
        d.rectangle((455, 56, 632, 80), fill=img.getpixel((640, 60)))
        d.rectangle((1395, 54, 1532, 82), fill=img.getpixel((1380, 60)))
    return img


def dock(img, widget, k=1.0, glow_ring=False):
    """The Taskitify player in its real place on a capture scaled by k: right edge at the chevron."""
    x = round(DOCK_RIGHT * k) - widget.size[0]
    y = round(TASKBAR_TOP * k)
    img.alpha_composite(widget, (x, y))
    if glow_ring:
        outline(img, (x + 2 * k - 6, y + 4 * k - 6, x + widget.size[0] - 2 * k + 6, y + widget.size[1] - 4 * k + 6), 6 * k + 6, width=max(2, round(k)), alpha=170)
    return img


def mini(name):
    import json
    import re
    text = open(os.path.join(RAW, "components", name + ".json")).read()
    meta = json.loads(text.split(',"scale"')[0] + '}')  # the scale is written with the current culture's decimal comma
    return comp(name), meta


# ---- The six artworks -------------------------------------------------------------------------

def art01():
    img = atmosphere()
    # Browser: the real Edge window (Bing search), its visible frame without Windows' invisible border.
    browser = desktop("browse-1").crop((157, 70, 1763, 980))
    browser = rounded(scale(browser, 2466 / browser.size[0]), 14)
    place(img, browser, (102, 102), blur=70, offset=(0, 26), opacity=120)
    card, meta = mini("mini-rain-cinema")
    x = W - 102 - meta["w"] - meta["x"]
    shadow(img, rounded(Image.new("RGBA", (meta["w"], meta["h"]), (0, 0, 0, 255)), 30), (x + meta["x"], 112 + meta["y"]), blur=90, offset=(0, 46), opacity=170)
    img.alpha_composite(card, (x, 112 - meta["y"]))
    centred_text(img, "Less switching. More listening.", "Bold", 168, 1650)
    centred_text(img, "Your music, right on your taskbar.", "Medium", 84, 1776, opacity=205)
    # Taskbar: the real right half of the Windows taskbar, 4x, with the player in its place.
    k = 4.0
    bar = raw("desktop", "desk-clean.png").crop((960, TASKBAR_TOP, 1920, 1200))
    bar = scale(bar, k)
    band_top = H - bar.size[1]
    img.alpha_composite(bar, (0, band_top))
    widget = comp("bar-rain-4x")
    wx = round((DOCK_RIGHT - 960) * k) - widget.size[0]
    img.alpha_composite(widget, (wx, band_top))
    outline(img, (wx + 2 * k - 8, band_top + 4 * k - 8, wx + widget.size[0] - 2 * k + 8, band_top + widget.size[1] - 4 * k + 8), 6 * k + 8)
    return img


def art02():
    img = atmosphere(warm=.3)
    centred_text(img, "Your music. Never in the way.", "Bold", 132, 205)
    centred_text(img, "Keep listening, whatever you're doing.", "Medium", 62, 300, opacity=205)
    scenes = [("browse-1", "rain", "Browsing"), ("study-1", "planes", "Studying"), ("work-1", "golden", "Working"), ("relax-1", "north", "Relaxing")]
    sw, sh = 1216, 760
    gap_x, gap_y = 72, 40
    left = (W - (sw * 2 + gap_x)) // 2
    top = 380
    label_font = font("Medium", 50)
    for i, (cap, song, label) in enumerate(scenes):
        shot = dock(desktop(cap), comp(f"bar-{song}-1x"), glow_ring=True)
        shot = rounded(scale(shot, sw / 1920), 18)
        x = left + (i % 2) * (sw + gap_x)
        y = top + (i // 2) * (sh + 96 + gap_y)
        place(img, shot, (x, y), blur=40, offset=(0, 18), opacity=130)
        f = label_font
        ImageDraw.Draw(img).text((x + sw / 2 - f.getlength(label) / 2, y + sh + 18), label, font=f, fill=CREAM + (225,))
    return img


def art03():
    img = atmosphere(warm=.15)
    centred_text(img, "As little or as much as you want.", "Bold", 132, 215)
    centred_text(img, "A listening experience that adapts to you.", "Medium", 62, 310, opacity=205)
    k = 1.875
    desk = raw("desktop", "desk-clean.png")
    rows = [("bar-full", "Lyrics & controls"), ("bar-lyrics", "Just the lyrics"), ("bar-compact", "Compact player"), ("bar-minimal", "Minimal")]
    slice_h = 70  # a strip of the real desktop above the taskbar
    strip = desk.crop((0, TASKBAR_TOP - slice_h, 1920, 1200))
    strip = scale(strip, k)
    left = (W - strip.size[0]) // 2
    y = 400
    f = font("Medium", 48)
    for name, label in rows:
        ImageDraw.Draw(img).text((left + 8, y), label, font=f, fill=CREAM + (230,))
        y += 74
        row = strip.copy()
        widget = comp(name)
        row.alpha_composite(widget, (round(DOCK_RIGHT * k) - widget.size[0], round(slice_h * k)))
        row = rounded(row, 22)
        place(img, row, (left, y), blur=36, offset=(0, 14), opacity=140)
        x = left + round(DOCK_RIGHT * k) - widget.size[0]
        wy = y + round(slice_h * k)
        outline(img, (x + 2 * k - 6, wy + 4 * k - 6, x + widget.size[0] - 2 * k + 6, wy + widget.size[1] - 4 * k + 6), 6 * k + 6, alpha=150, glow_alpha=90)
        y += row.size[1] + 52
    return img


def art04():
    base = Image.open(os.path.join(RAW, "photos", "matterhorn.jpg")).convert("RGB")
    base = base.resize((W, round(base.size[1] * W / base.size[0])), Image.LANCZOS).crop((0, 0, W, H)).filter(ImageFilter.GaussianBlur(28))
    img = base.convert("RGBA")
    # The Matterhorn at sunset (CC0), softened into an atmosphere.
    img = Image.blend(img, Image.new("RGBA", (W, H), (10, 16, 28, 255)), .42)
    centred_text(img, "Fall in love with your music again.", "Bold", 132, 215)
    centred_text(img, "A beautiful new way to experience your favorite songs.", "Medium", 62, 310, opacity=210)
    echo, em = mini("mini-echo-vertical")
    north, nm = mini("mini-north-vertical")
    rain, rm = mini("mini-rain-cinema")
    echo = scale(echo, .95)
    north = scale(north, .95)
    place(img, echo.rotate(5, resample=Image.BICUBIC, expand=True), (150, 560), blur=60, offset=(0, 30), opacity=150)
    place(img, north.rotate(-5, resample=Image.BICUBIC, expand=True), (W - 150 - north.size[0] - 60, 560), blur=60, offset=(0, 30), opacity=150)
    rain = scale(rain, .94)
    place(img, rain, ((W - rain.size[0]) // 2, 520), blur=90, offset=(0, 40), opacity=180)
    return img


def art05():
    img = atmosphere(warm=.8)
    centred_text(img, "Some lyrics deserve to stay.", "Bold", 132, 215)
    centred_text(img, "Create and share something personal with Lyrics Studio.", "Medium", 62, 310, opacity=210)
    cards = ["card-paint", "card-receipt", "card-cassette", "card-gameboy", "card-magic"]
    angles = [9, 4, 0, -4, -9]
    h = 1320
    centres = [640, 1280, 1920, 2560, 3200]
    tops = [600, 540, 500, 540, 600]
    for name, angle, cx, top in zip(cards, angles, centres, tops):
        card = comp(name)
        card = rounded(scale(card, h / card.size[1]), 36)
        card = card.rotate(angle, resample=Image.BICUBIC, expand=True)
        place(img, card, (cx - card.size[0] // 2, top), blur=50, offset=(0, 26), opacity=170)
    return img


def hero():
    img = atmosphere()
    glow(img, (2900, 900), 1300, (40, 170, 190), 50)
    rain, rm = mini("mini-rain-cinema")
    rain = scale(rain, .9)
    place(img, rain, (W - rain.size[0] - 150, 250), blur=90, offset=(0, 40), opacity=170)
    widget = comp("bar-rain-3x")
    wx, wy = W - widget.size[0] - 520, 1720
    panel = Image.new("RGBA", (widget.size[0] + 60, widget.size[1] + 40), (20, 32, 42, 235))
    panel = rounded(panel, 28)
    place(img, panel, (wx - 30, wy - 20), blur=50, offset=(0, 22), opacity=150)
    img.alpha_composite(widget, (wx, wy))
    outline(img, (wx + 6 - 8, wy + 12 - 8, wx + widget.size[0] - 6 + 8, wy + widget.size[1] - 12 + 8), 26, alpha=150)
    return img


def save(img, name):
    os.makedirs(OUT, exist_ok=True)
    os.makedirs(os.path.join(OUT, "reduced"), exist_ok=True)
    img = grain(img)
    path = os.path.join(OUT, name + ".png")
    img.save(path, optimize=True)
    img.resize((1280, 720), Image.LANCZOS).save(os.path.join(OUT, "reduced", name + "-1280.png"))
    img.resize((308, 173), Image.LANCZOS).save(os.path.join(OUT, "reduced", name + "-308.png"))
    print(name, os.path.getsize(path) // 1024, "KB")
    return img


if __name__ == "__main__":
    import sys
    only = set(sys.argv[1:])
    done = []
    for name, fn in [("01-less-switching", art01), ("02-never-in-the-way", art02), ("03-as-little-or-as-much", art03),
                     ("04-fall-in-love", art04), ("05-lyrics-deserve-to-stay", art05), ("super-hero-art", hero)]:
        if not only or name[:2] in only or name in only:
            done.append(save(fn(), name))
    sheet = Image.new("RGB", (1920, 1620), (18, 18, 18))
    names = sorted(f for f in os.listdir(OUT) if f.endswith(".png"))
    for i, f in enumerate(names[:6]):
        t = Image.open(os.path.join(OUT, f)).convert("RGB").resize((960, 540), Image.LANCZOS)
        sheet.paste(t, ((i % 2) * 960, (i // 2) * 540))
    sheet.save(os.path.join(OUT, "reduced", "contact-sheet.png"))
