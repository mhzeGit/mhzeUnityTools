"""Run gm_run.py stages for several groups in parallel.

python gm_batch.py <jobs> <stage,stage,...> <group> [<group> ...]      (jobs = number of parallel groups)
Each group's output goes to .utmp/<slug>/gmat/<group>/batch_<first stage>.log; a summary line per group is printed at the end.
"""
import os
import subprocess
import sys
import time
from concurrent.futures import ThreadPoolExecutor

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402


def run(group, stages):
    t0 = time.time()
    work = os.path.join(envcfg.GMAT_WORK, group)
    os.makedirs(work, exist_ok=True)
    log = os.path.join(work, f"batch_{stages[0]}.log")
    with open(log, "w", encoding="utf-8") as f:
        res = subprocess.run([sys.executable, os.path.join(HERE, "gm_run.py"), group, *stages], stdout=f, stderr=subprocess.STDOUT, cwd=HERE)
    return group, res.returncode, time.time() - t0, log


def main():
    jobs = int(sys.argv[1])
    stages = sys.argv[2].split(",")
    groups = sys.argv[3:]
    with ThreadPoolExecutor(max_workers=jobs) as ex:
        for group, rc, secs, log in ex.map(lambda g: run(g, stages), groups):
            print(f"{group:14s} rc={rc} {secs:5.0f}s  {log}")


if __name__ == "__main__":
    main()
