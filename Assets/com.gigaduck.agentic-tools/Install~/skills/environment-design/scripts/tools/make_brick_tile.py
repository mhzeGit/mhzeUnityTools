"""Assemble the seamless masonry tile used by every brick surface of the environment.

python make_brick_tile.py [--brick <candidate>] [--mortar <candidate>]   (candidates default to the selection.json entries 'brick' and 'mortar_stone')

The masonry is no longer modelled brick by brick: walls, stairs, landing, footing and plinths are solid forms that carry this tile.
The tile is built ONLY from the two textures that were generated and approved earlier (no new image generation):
  brick  (04_brick.png)         a flat clay-brick surface  -> every brick is a random crop of it (flipped, tinted a little)
  mortar (05_mortar_stone.png)  the pale stone/mortar      -> luminance variation of the joints (darkened)
Layout: 4 bricks across x 8 courses, running bond (odd courses shifted half a brick), one tile = 0.96 m x 0.96 m,
i.e. a brick pitch of 0.24 m x 0.12 m (2.4 m panels hold exactly 10 bricks, 4.8 m walls exactly 40 courses).
Hand-painted look: rounded bricks, a dark painted rim, a light top edge, a shaded underside and right edge, cavity darkening.
Writes .utmp/<slug>/tex/brick_wall_2k.png (2048 x 2048, seamless), _tiles/brick_wall_after.png (2 x 2 preview) and
<env>/textures/brick-wall-tile.json (parameters, so the tile can be rebuilt).
"""
import json
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402
TEX = envcfg.TEX_WORK
OUT_DOC = envcfg.TEXT_DIR

TILE = 2048
COLS, ROWS = 4, 8
PITCH_X, PITCH_Y = TILE // COLS, TILE // ROWS        # 512 x 256 px  (0.24 m x 0.12 m at 0.96 m per tile)
JOINT = 22                                            # px  (about 10 mm)
CORNER = 20                                           # px  corner radius of a brick
SEED = 20261019
PARAMS = {"tile_px": TILE, "cols": COLS, "rows": ROWS, "joint_px": JOINT, "corner_px": CORNER, "seed": SEED, "metres_per_tile": 0.96,
          "brick_source": None, "mortar_source": None}


def smooth(x):
    x = np.clip(x, 0.0, 1.0)
    return x * x * (3 - 2 * x)


def brick_alpha_and_distances(w, h):
    """Rounded-rectangle coverage (0..1, antialiased) and the distance of every pixel to the nearest brick edge."""
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float64)
    xx += 0.5
    yy += 0.5
    dl, dr, dt, db = xx, w - xx, yy, h - yy
    edge = np.minimum(np.minimum(dl, dr), np.minimum(dt, db))
    # rounded corners: signed distance to the rounded box
    cx = np.minimum(dl, dr)
    cy = np.minimum(dt, db)
    qx = np.maximum(CORNER - cx, 0.0)
    qy = np.maximum(CORNER - cy, 0.0)
    sd = np.hypot(qx, qy) - CORNER                    # < 0 inside
    alpha = np.clip(0.5 - sd, 0.0, 1.0)
    return alpha, edge, dt, db, dl, dr


def main():
    rng = np.random.default_rng(SEED)
    sel = json.load(open(os.path.join(OUT_DOC, "selection.json")))["materials"]
    argv = sys.argv[1:]
    brick_name = argv[argv.index("--brick") + 1] if "--brick" in argv else sel["brick"]["candidate"]
    mortar_name = argv[argv.index("--mortar") + 1] if "--mortar" in argv else sel["mortar_stone"]["candidate"]
    PARAMS["brick_source"], PARAMS["mortar_source"] = brick_name, mortar_name
    from finalize_textures import find_candidate
    brick = np.asarray(Image.open(find_candidate(brick_name)).convert("RGB")).astype(np.float64)
    mortar = np.asarray(Image.open(find_candidate(mortar_name)).convert("RGB")).astype(np.float64)
    bh, bw_src = brick.shape[:2]
    bw, bhh = PITCH_X - JOINT, PITCH_Y - JOINT
    alpha, edge, dt, db, dl, dr = brick_alpha_and_distances(bw, bhh)

    # shading masks shared by every brick (hand-painted: light top, shaded underside and right side, dark rim)
    top_light = 1.0 + 0.11 * smooth(1.0 - dt / 12.0)
    bottom_dark = 1.0 - 0.34 * smooth(1.0 - db / 30.0)
    left_light = 1.0 + 0.04 * smooth(1.0 - dl / 16.0)
    right_dark = 1.0 - 0.16 * smooth(1.0 - dr / 22.0)
    cavity = 1.0 - 0.14 * smooth(1.0 - edge / 9.0)
    rim = 1.0 - 0.42 * smooth(1.0 - edge / 4.5)
    shade = (top_light * bottom_dark * left_light * right_dark * cavity * rim)[..., None]

    # mortar: dark warm brown carrying the luminance variation of the stone texture
    m_lum = (0.2126 * mortar[..., 0] + 0.7152 * mortar[..., 1] + 0.0722 * mortar[..., 2])
    m_lum = m_lum / m_lum.mean()
    my = rng.integers(0, mortar.shape[0] - TILE) if mortar.shape[0] > TILE else 0
    mx = rng.integers(0, mortar.shape[1] - TILE) if mortar.shape[1] > TILE else 0
    mt = np.asarray(Image.fromarray((np.clip(m_lum, 0, 3) / 3 * 255).astype(np.uint8)).resize((TILE, TILE), Image.LANCZOS)).astype(np.float64) / 255 * 3
    base_mortar = np.array([62.0, 42.0, 31.0])
    canvas = base_mortar[None, None, :] * np.clip(0.6 + 0.5 * mt, 0.55, 1.35)[..., None]

    manifest = []
    for r in range(ROWS):
        offset = PITCH_X // 2 if r % 2 else 0
        for c in range(COLS):
            sx = c * PITCH_X + offset + JOINT // 2
            sy = r * PITCH_Y + JOINT // 2
            y0 = rng.integers(0, bh - bhh)
            x0 = rng.integers(0, bw_src - bw)
            crop = brick[y0:y0 + bhh, x0:x0 + bw].copy()
            if rng.random() < 0.5:
                crop = crop[:, ::-1]
            if rng.random() < 0.5:
                crop = crop[::-1, :]
            gain = float(np.exp(rng.normal(0.0, 0.07)))
            tint = np.array([1.0 + rng.normal(0.0, 0.035), 1.0 + rng.normal(0.0, 0.02), 1.0 - rng.normal(0.0, 0.04)])
            crop = np.clip(crop * gain * tint, 0, 255) * shade
            # a few darker soot specks and chips on some bricks
            if rng.random() < 0.35:
                for _ in range(rng.integers(1, 4)):
                    px, py = rng.integers(26, bw - 26), rng.integers(20, bhh - 20)
                    rad = rng.integers(4, 11)
                    yy, xx = np.ogrid[:bhh, :bw]
                    spot = smooth(1.0 - np.hypot(xx - px, yy - py) / rad)
                    crop = crop * (1.0 - 0.35 * spot)[..., None]
            for wrap in (-TILE, 0, TILE):                      # bricks that straddle the right edge continue on the left
                x_a = sx + wrap
                lo, hi = max(0, x_a), min(TILE, x_a + bw)
                if hi <= lo:
                    continue
                sub = canvas[sy:sy + bhh, lo:hi]
                a = alpha[:, lo - x_a:hi - x_a][..., None]
                canvas[sy:sy + bhh, lo:hi] = sub * (1 - a) + crop[:, lo - x_a:hi - x_a] * a
            manifest.append({"row": r, "col": c, "crop": [int(x0), int(y0)], "gain": round(gain, 3)})

    out = np.clip(canvas + 0.5, 0, 255).astype(np.uint8)
    path = os.path.join(TEX, "brick_wall_2k.png")
    Image.fromarray(out).save(path)
    os.makedirs(os.path.join(TEX, "_tiles"), exist_ok=True)
    t = Image.new("RGB", (TILE * 2, TILE * 2))
    im = Image.fromarray(out)
    for ix in range(2):
        for iy in range(2):
            t.paste(im, (ix * TILE, iy * TILE))
    t.resize((900, 900), Image.LANCZOS).save(os.path.join(TEX, "_tiles", "brick_wall_after.png"))
    # seamlessness: wrap difference vs typical neighbour difference
    f = out.astype(np.float64)
    stats = {"wrap_x": round(float(np.abs(f[:, 0] - f[:, -1]).mean()), 2), "adjacent_x": round(float(np.abs(f[:, 1:] - f[:, :-1]).mean()), 2),
             "wrap_y": round(float(np.abs(f[0] - f[-1]).mean()), 2), "adjacent_y": round(float(np.abs(f[1:] - f[:-1]).mean()), 2),
             "mean_rgb": [round(float(v), 1) for v in f.reshape(-1, 3).mean(0)]}
    os.makedirs(OUT_DOC, exist_ok=True)
    json.dump({"params": PARAMS, "edge_stats": stats, "bricks": manifest}, open(os.path.join(OUT_DOC, "brick-wall-tile.json"), "w"), indent=1)
    print(path, Image.open(path).size, stats)


if __name__ == "__main__":
    main()
