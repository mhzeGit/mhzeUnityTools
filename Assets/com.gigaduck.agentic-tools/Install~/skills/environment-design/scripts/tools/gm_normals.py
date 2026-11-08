"""Report how far the exported corner normals are from the geometric face normals (a flat, hard-edged part must report ~0).

python gm_normals.py <group> [model.obj|uv.obj]
Prints per OBJ part: triangles, mean / max deviation in degrees and the share of corners deviating more than 5 degrees.
"""
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402


def load(path):
    v, vn, tris, part = [], [], [], []
    cur = None
    for line in open(path):
        if line.startswith("o "):
            cur = line[2:].strip()
        elif line.startswith("v "):
            v.append([float(x) for x in line.split()[1:4]])
        elif line.startswith("vn "):
            vn.append([float(x) for x in line.split()[1:4]])
        elif line.startswith("f "):
            c = [[int(i) if i else 0 for i in t.split("/")] for t in line.split()[1:]]
            for k in range(1, len(c) - 1):
                tris.append((c[0], c[k], c[k + 1]))
                part.append(cur)
    return np.array(v), np.array(vn), tris, part


def report(group, name="model.obj"):
    v, vn, tris, part = load(os.path.join(envcfg.GMAT_WORK, group, name))
    per = {}
    for t, pname in zip(tris, part):
        p = np.array([v[c[0] - 1] for c in t])
        n = np.cross(p[1] - p[0], p[2] - p[0])
        ln = np.linalg.norm(n)
        if ln < 1e-12:
            continue
        n /= ln
        for c in t:
            if len(c) >= 3 and c[2] > 0:
                nn = vn[c[2] - 1]
                nn = nn / np.linalg.norm(nn)
                per.setdefault(pname, []).append(np.degrees(np.arccos(np.clip(np.dot(n, nn), -1, 1))))
    for pname, devs in per.items():
        d = np.array(devs)
        print(f"{group:14s} {pname:42s} corners {len(d):6d}  mean {d.mean():5.1f}  max {d.max():5.1f}  >5deg {100 * (d > 5).mean():5.1f}%")


if __name__ == "__main__":
    report(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "model.obj")
