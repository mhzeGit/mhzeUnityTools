"""Contact sheet of the per-asset review renders of a group.

python gm_sheet.py <group> <tag> [views=q,qb,top] [width=430] [assets=a,b,c]  -> .utmp/<slug>/gmat/<group>/review/_sheet_<tag>.png
"""
import json
import os
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402
ROOT = envcfg.ROOT


def main():
    group, tag = sys.argv[1], sys.argv[2]
    views = (sys.argv[3] if len(sys.argv) > 3 else "q,qb,top").split(",")
    width = int(sys.argv[4]) if len(sys.argv) > 4 else 430
    spec = json.load(open(os.path.join(envcfg.GROUPS_DIR, group + ".json")))
    names = sys.argv[5].split(",") if len(sys.argv) > 5 else [a["name"] for a in spec["assets"]]
    rev = os.path.join(ROOT, spec["work"], "review")
    h = int(width * 700 / 900)
    sheet = Image.new("RGB", (len(views) * (width + 6) + 6, len(names) * (h + 22) + 6), (20, 20, 24))
    d = ImageDraw.Draw(sheet)
    for r, n in enumerate(names):
        y = 4 + r * (h + 22)
        d.text((8, y), f"{n}  [{tag}]  views: {', '.join(views)}", fill=(255, 220, 120))
        for c, v in enumerate(views):
            p = os.path.join(rev, f"{n}_{tag}_{v}.png")
            if os.path.exists(p):
                im = Image.open(p).convert("RGB").resize((width, h), Image.LANCZOS)
                sheet.paste(im, (6 + c * (width + 6), y + 16))
    out = os.path.join(rev, f"_sheet_{tag}.png")
    sheet.save(out)
    print(out, sheet.size)


if __name__ == "__main__":
    main()
