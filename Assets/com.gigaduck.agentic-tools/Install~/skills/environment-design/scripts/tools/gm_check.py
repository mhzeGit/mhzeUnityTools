"""Check that every Graphmat component belongs to exactly one region (a component that welds two OBJ parts is silently assigned to the
region of its seed face).  python gm_check.py [group ...]   (default: all groups with a meshinfo.json)"""
import glob
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402
import gm_pipeline  # noqa: E402


def check(group):
    spec = gm_pipeline.load(os.path.join(envcfg.GROUPS_DIR, group + ".json"))
    w = spec["_work"]
    mi = os.path.join(w, "meshinfo.json")
    if not os.path.exists(mi):
        return None
    d = json.load(open(mi))
    parts = gm_pipeline.parse_obj_parts(os.path.join(w, "model.obj"))
    owner = {}
    expected = {}
    for name, start, count in parts:
        r = gm_pipeline.region_of(name)
        expected[r] = expected.get(r, 0) + count
        for t in range(start, start + count):
            owner[t] = r
    assigned = {}
    for c in d["components"]:
        r = owner[c["seedFace"]]
        assigned[r] = assigned.get(r, 0) + c["triangles"]
    bad = {r: (expected[r], assigned.get(r, 0)) for r in expected if expected[r] != assigned.get(r, 0)}
    return {"ok": not bad, "bad(expected, assigned)": bad, "regions": expected}


if __name__ == "__main__":
    groups = sys.argv[1:] or sorted(os.path.basename(p)[:-5] for p in glob.glob(os.path.join(envcfg.GROUPS_DIR, "*.json")))
    for g in groups:
        print(g, check(g))
