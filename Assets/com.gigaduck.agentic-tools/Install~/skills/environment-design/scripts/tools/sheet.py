#!/usr/bin/env python
"""Tile images with captions into one contact sheet.

  python sheet.py out.png cols cellw img1.png[:caption] img2.png[:caption] ...
"""
import sys
from PIL import Image, ImageDraw, ImageFont

out, cols, cellw = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
if out.startswith("/c/"):
    out = "C:/" + out[3:]
items = sys.argv[4:]
tiles = []
for it in items:
    path, _, cap = it.partition("::")
    if path.startswith("/c/"):
        path = "C:/" + path[3:]
    im = Image.open(path).convert("RGB")
    h = int(im.size[1] * cellw / im.size[0])
    tiles.append((im.resize((cellw, h), Image.LANCZOS), cap or path.split("/")[-1].split("\\")[-1]))
rows = (len(tiles) + cols - 1) // cols
cellh = max(t[0].size[1] for t in tiles) + 22
sheet = Image.new("RGB", (cols * cellw + (cols - 1) * 4, rows * cellh), (18, 18, 20))
try:
    font = ImageFont.truetype("arial.ttf", 14)
except Exception:
    font = ImageFont.load_default()
d = ImageDraw.Draw(sheet)
for i, (im, cap) in enumerate(tiles):
    x = (i % cols) * (cellw + 4)
    y = (i // cols) * cellh
    sheet.paste(im, (x, y + 20))
    d.text((x + 4, y + 3), cap, fill=(255, 230, 120), font=font)
sheet.save(out)
print(out, sheet.size)
