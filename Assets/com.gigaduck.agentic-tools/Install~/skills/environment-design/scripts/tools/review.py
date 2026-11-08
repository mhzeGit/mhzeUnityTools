"""Compose a review sheet for an asset: [reference crop | three-quarter | back three-quarter] over [front | side | top | wire].

python review.py <Asset> <tag> [refcrop.png]   -> .utmp/<slug>/renders/<Asset>_<tag>_sheet.png
"""
import os
import sys
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402
R = os.path.join(envcfg.WORK, "renders")


def tile(path, w, h, cap):
    if path and os.path.exists(path):
        im = Image.open(path).convert("RGB")
    else:
        im = Image.new("RGB", (w, h), (30, 30, 34))
    im.thumbnail((w, h), Image.LANCZOS)
    t = Image.new("RGB", (w, h), (24, 24, 28))
    t.paste(im, ((w - im.size[0]) // 2, (h - im.size[1]) // 2))
    d = ImageDraw.Draw(t)
    try:
        f = ImageFont.truetype("arial.ttf", 15)
    except Exception:
        f = ImageFont.load_default()
    d.text((6, 4), cap, fill=(255, 225, 110), font=f)
    return t


def main():
    asset, tag = sys.argv[1], sys.argv[2]
    ref = sys.argv[3] if len(sys.argv) > 3 else None
    base = os.path.join(R, f"{asset}_{tag}")
    w, h = 600, 470
    row1 = [tile(ref, w, h, "reference"), tile(base + "_q.png", w, h, f"{asset} {tag}: three-quarter"), tile(base + "_qb.png", w, h, "back three-quarter")]
    row2 = [tile(base + "_front.png", w, h, "front (camera at -Y)"), tile(base + "_side.png", w, h, "side"), tile(base + "_top.png", w, h, "top")]
    wire = base + "_wire.png"
    sheet = Image.new("RGB", (w * 3 + 8, h * 2 + 4 + (h + 4 if os.path.exists(wire) else 0)), (10, 10, 12))
    for i, t in enumerate(row1):
        sheet.paste(t, (i * (w + 4), 0))
    for i, t in enumerate(row2):
        sheet.paste(t, (i * (w + 4), h + 4))
    if os.path.exists(wire):
        sheet.paste(tile(wire, w, h, "wireframe (front-facing edges)"), (0, 2 * (h + 4)))
    out = base + "_sheet.png"
    sheet.save(out)
    print(out, sheet.size)


main()
