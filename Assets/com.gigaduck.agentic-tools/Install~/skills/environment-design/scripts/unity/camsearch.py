"""Search the camera (position, yaw, fov) whose render best matches the concept's composition (luminance layout + edge correlation).
python camsearch.py        edit the itertools.product ranges below to bracket the room; writes .utmp/<slug>/camsearch_top.json
The fitted camera is a hint for the side-by-side comparison, not a reason to distort the room (the concept's perspective may be inconsistent)."""
import sys, math, itertools, json, os
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402
import bo
retry = bo.retry
import numpy as np
from PIL import Image
REF = os.path.join(envcfg.REF_DIR, envcfg.CFG.get("references", {}).get("persp", "perspective.png"))
specs, meta = [], []
for x, z, yaw, fov in itertools.product([-2.5, -1.5, -0.5, 0.5], [-2.8, -2.0, -1.2], [0, 10, 20, 30], [85, 100]):
    ty = math.radians(yaw)
    tx, tz = x + math.sin(ty) * 5, z + math.cos(ty) * 5
    specs.append(f"{x} 1.45 {z} {tx:.3f} 1.35 {tz:.3f} {fov} 418 235 pp")
    meta.append((x, z, yaw, fov))
retry(lambda: (bo.shots('cs', specs), 'ok')[1])
ref = np.asarray(Image.open(REF).convert('L').resize((418, 235), Image.LANCZOS)).astype(float)
def feat(a):
    gx = np.abs(np.diff(a, axis=1))[:-1, :]; gy = np.abs(np.diff(a, axis=0))[:, :-1]
    return np.concatenate([gx.ravel(), gy.ravel()])
rf = feat(ref); rf = (rf - rf.mean()) / (rf.std() + 1e-6)
rl = (ref - ref.mean()) / (ref.std() + 1e-6)
res = []
for i, m in enumerate(meta):
    im = np.asarray(Image.open(os.path.join(envcfg.SHOTS, f'cs_{i:02d}.png')).convert('L').resize((418, 235), Image.LANCZOS)).astype(float)
    f = feat(im); f = (f - f.mean()) / (f.std() + 1e-6)
    l = (im - im.mean()) / (im.std() + 1e-6)
    # coarse luminance layout correlation (blurred) + edge correlation
    sm = lambda a: np.asarray(Image.fromarray(a).resize((42, 24), Image.BILINEAR)).astype(float)
    c1 = np.corrcoef(sm(rl).ravel(), sm(l).ravel())[0, 1]
    c2 = float((rf * f).mean())
    res.append((c1 + c2, c1, c2, i, m))
res.sort(reverse=True)
for r in res[:8]: print(round(r[0], 3), round(r[1], 3), round(r[2], 3), r[3], r[4])
json.dump([(r[3], r[4]) for r in res[:6]], open(os.path.join(envcfg.WORK, 'camsearch_top.json'), 'w'))
