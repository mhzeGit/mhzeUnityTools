import sys
from PIL import Image, ImageDraw, ImageFont

src, out, x0, y0, x1, y1, scale, step = sys.argv[1], sys.argv[2], *map(int, sys.argv[3:9])
im = Image.open(src).convert("RGB")
crop = im.crop((x0, y0, x1, y1)).resize(((x1 - x0) * scale, (y1 - y0) * scale), Image.LANCZOS)
d = ImageDraw.Draw(crop, "RGBA")
try:
    font = ImageFont.truetype("arial.ttf", 13)
except Exception:
    font = ImageFont.load_default()
gx = (x0 // step) * step
while gx <= x1:
    if gx >= x0:
        X = (gx - x0) * scale
        major = (gx % (step * 2) == 0)
        d.line([(X, 0), (X, crop.size[1])], fill=(0, 255, 255, 150 if major else 70), width=1)
        if major:
            d.text((X + 2, 2), str(gx), fill=(255, 255, 0, 255), font=font)
    gx += step
gy = (y0 // step) * step
while gy <= y1:
    if gy >= y0:
        Y = (gy - y0) * scale
        major = (gy % (step * 2) == 0)
        d.line([(0, Y), (crop.size[0], Y)], fill=(255, 0, 255, 150 if major else 70), width=1)
        if major:
            d.text((2, Y + 2), str(gy), fill=(255, 255, 0, 255), font=font)
    gy += step
crop.save(out)
print(out, crop.size)
