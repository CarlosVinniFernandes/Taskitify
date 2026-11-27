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
    if name in ("browse-2", "study-2"):
        d.rectangle((1690, 566, 1800, 596), fill=img.getpixel((1684, 581)))
    if name == "work-2":
        d.rectangle((1120, 526, 1296, 548), fill=img.getpixel((1110, 530)))
        d.rectangle((1588, 524, 1724, 550), fill=img.getpixel((1580, 530)))
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


def label_pill(img, text, x, y, size=46):
    f = font("Medium", size)
    w = f.getlength(text)
    pill = rounded(Image.new("RGBA", (round(w + size * 1.2), round(size * 1.75)), (12, 22, 30, 200)), round(size * .875))
    img.alpha_composite(pill, (x, y))
    ImageDraw.Draw(img).text((x + size * .6, y + size * .32), text, font=f, fill=CREAM + (240,))


def art02():
    img = atmosphere(warm=.3)
    centred_text(img, "Your music. Never in the way.", "Bold", 116, 150)
    centred_text(img, "Keep listening, whatever you're doing.", "Medium", 56, 226, opacity=205)
    # The right part of each real screen: the app at work above, the taskbar and the player below.
    k = 1.6
    cw, ch = 1125, 563
    scenes = [("browse-2", "rain", "Browsing", 795), ("study-2", "planes", "Studying", 795), ("work-2", "golden", "Working", 795), ("relax-1", "north", "Relaxing", 795)]
    tw, th = round(cw * k), round(ch * k)
    gap_x, gap_y = 64, 48
    left = (W - (tw * 2 + gap_x)) // 2
    top = 268
    for i, (cap, song, label, cx) in enumerate(scenes):
        shot = scale(desktop(cap).crop((cx, 1200 - ch, cx + cw, 1200)), k)
        widget = comp(f"bar-{song}-tile")
        wx = round((DOCK_RIGHT - cx) * k) - widget.size[0]
        wy = round((TASKBAR_TOP - (1200 - ch)) * k)
        shot.alpha_composite(widget, (wx, wy))
        outline(shot, (wx + 2 * k - 7, wy + 4 * k - 7, wx + widget.size[0] - 2 * k + 7, wy + widget.size[1] - 4 * k + 7), 6 * k + 7, width=3, alpha=190, glow_alpha=130)
        shot = rounded(shot, 22)
        x = left + (i % 2) * (tw + gap_x)
        y = top + (i // 2) * (th + gap_y)
        place(img, shot, (x, y), blur=40, offset=(0, 18), opacity=140)
        label_pill(img, label, x + 28, y + 28)
    return img


def art03():
    img = atmosphere(warm=.15)
    centred_text(img, "As little or as much as you want.", "Bold", 124, 190)
    centred_text(img, "A listening experience that adapts to you.", "Medium", 58, 272, opacity=205)
    # The real taskbar without its empty middle: Start, search and pinned apps, then the player,
    # the notification area and the clock, as on a narrower screen.
    k = 3.08
    desk = raw("desktop", "desk-clean.png")
    cut_left, cut_right = 480, DOCK_RIGHT - 360 - 24
    bar = desk.crop((0, TASKBAR_TOP, cut_left, 1200))
    rest = desk.crop((cut_right, TASKBAR_TOP, 1920, 1200))
    joined = Image.new("RGBA", (bar.size[0] + rest.size[0], 48))
    joined.paste(bar, (0, 0))
    joined.paste(rest, (bar.size[0], 0))
    slice_h = 36
    wall = desk.crop((0, TASKBAR_TOP - slice_h, joined.size[0], TASKBAR_TOP))
    strip = Image.new("RGBA", (joined.size[0], slice_h + 48))
    strip.paste(wall, (0, 0))
    strip.paste(joined, (0, slice_h))
    strip = scale(strip, k)
    dock_x = round((bar.size[0] + DOCK_RIGHT - cut_right) * k)
    rows = [("bar-full", "Lyrics & controls"), ("bar-lyrics", "Just the lyrics"), ("bar-compact", "Compact player"), ("bar-minimal", "Minimal")]
    left = (W - strip.size[0]) // 2
    y = 352
    f = font("Medium", 50)
    for name, label in rows:
        ImageDraw.Draw(img).text((left + 6, y), label, font=f, fill=CREAM + (232,))
        y += 74
        row = strip.copy()
        widget = comp(name)
        wy = round(slice_h * k)
        row.alpha_composite(widget, (dock_x - widget.size[0], wy))
        x = dock_x - widget.size[0]
        outline(row, (x + 2 * k - 7, wy + 4 * k - 7, x + widget.size[0] - 2 * k + 7, wy + widget.size[1] - 4 * k + 7), 6 * k + 7, width=3, alpha=170, glow_alpha=110)
        row = rounded(row, 24)
        place(img, row, (left, y), blur=36, offset=(0, 14), opacity=140)
        y += row.size[1] + 40
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
