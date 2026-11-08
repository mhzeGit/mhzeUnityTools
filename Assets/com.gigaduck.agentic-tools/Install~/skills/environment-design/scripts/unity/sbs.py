#!/usr/bin/env python
"""Side-by-side of the concept (left) and a render (right).   python sbs.py shot.png out.png"""
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
import envcfg  # noqa: E402

REF = os.path.join(envcfg.REF_DIR, envcfg.CFG.get("references", {}).get("persp", "perspective.png"))
a = Image.open(REF).convert("RGB")
b = Image.open(sys.argv[1]).convert("RGB").resize(a.size)
w, h = 836, 470
s = Image.new("RGB", (w * 2 + 6, h), (20, 20, 24))
s.paste(a.resize((w, h), Image.LANCZOS), (0, 0))
s.paste(b.resize((w, h), Image.LANCZOS), (w + 6, 0))
s.save(sys.argv[2])
print(s.size)
