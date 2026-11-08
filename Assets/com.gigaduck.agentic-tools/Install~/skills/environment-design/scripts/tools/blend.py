#!/usr/bin/env python
"""Compose comparison images.

  python blend.py ref.png shot.png out.png [mode]
mode: side (default, 2 columns, scaled to 1100 wide each), overlay (50% blend), both
"""
import sys
from PIL import Image

ref, shot, out = sys.argv[1:4]
mode = sys.argv[4] if len(sys.argv) > 4 else "side"
a = Image.open(ref).convert("RGB")
b = Image.open(shot).convert("RGB")
b = b.resize(a.size, Image.LANCZOS)
if mode == "overlay":
    Image.blend(a, b, 0.5).save(out)
elif mode == "both":
    ov = Image.blend(a, b, 0.5)
    w, h = a.size
    sheet = Image.new("RGB", (w, h * 2 // 1), (20, 20, 20))
    half = w // 2
    sheet = Image.new("RGB", (w, h), (20, 20, 20))
    sheet.paste(a.resize((half, h // 2)), (0, 0))
    sheet.paste(b.resize((half, h // 2)), (half, 0))
    sheet.paste(ov.resize((w // 2, h // 2)), (w // 4, h // 2))
    sheet.save(out)
else:
    w = 1000
    h = int(a.size[1] * w / a.size[0])
    sheet = Image.new("RGB", (w * 2 + 6, h), (20, 20, 20))
    sheet.paste(a.resize((w, h), Image.LANCZOS), (0, 0))
    sheet.paste(b.resize((w, h), Image.LANCZOS), (w + 6, 0))
    sheet.save(out)
print(out)
