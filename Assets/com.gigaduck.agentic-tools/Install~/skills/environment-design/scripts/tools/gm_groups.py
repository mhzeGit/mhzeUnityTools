"""Write groups/<group>.json (the specs gm_run.py / gm_export.py / gm_pipeline.py read) from texture_groups.py.

python gm_groups.py        writes <env>/groups/<group>.json for every group in texture_groups.GROUPS

texture_groups.py (edited per environment, see templates/texture_groups.py and examples/boiler-room/texture_groups.py) defines
  TEXTURE_FILE  texture name -> file in .utmp/<slug>/tex (the finalized 2048 px albedo of that material)
  REGION        region prefix of the part names (<Region>__<Part>) -> (texture, tile in metres per texture repeat, textureRotation or None)
  GROUPS        group -> dict(assets=[(Asset, module[, grm])], regions=[...], ao_radius, [cube], [cylinder], [over])
A group never mixes assets that need very different texel densities (walls, hero props and floor get their own atlas; small props share one).
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402
import texture_groups as T  # noqa: E402


def build(name, g):
    assets = []
    for a in g["assets"]:
        assets.append({"name": a[0], "module": a[1], "grm": a[2] if len(a) > 2 else [0, 0, 0]})
    regions = {}
    for r in g["regions"]:
        tex, tile, rot = T.REGION[r]
        cfg = {"texture": tex, "tile": tile}
        if rot:
            cfg["textureRotation"] = rot
        cfg.update(g.get("over", {}).get(r, {}))
        regions[r] = cfg
    textures = sorted({cfg["texture"] for cfg in regions.values()})
    rad = g.get("ao_radius", 0.4)
    return {
        "group": name,
        "work": os.path.join(envcfg.GMAT_WORK, name),
        "gap": max(1.5, rad * 3),
        "assets": assets,
        "uv": {"padding": 8, "thorough": True},
        "ao": {"rays": 64, "radius": rad, "bias": 0.01, "strength": 1.0, "apply": 0.9},
        "textures": [{"name": t, "file": T.TEXTURE_FILE[t]} for t in textures],
        "regions": regions,
        **({"cube": g["cube"]} if "cube" in g else {}),
        **({"cylinder": g["cylinder"]} if g.get("cylinder") else {}),
    }


def main():
    os.makedirs(envcfg.GROUPS_DIR, exist_ok=True)
    for name, g in T.GROUPS.items():
        path = os.path.join(envcfg.GROUPS_DIR, name + ".json")
        json.dump(build(name, g), open(path, "w"), indent=1)
        print("wrote", path, len(g["assets"]), "assets")


if __name__ == "__main__":
    main()
