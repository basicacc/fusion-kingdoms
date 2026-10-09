"""Fusion grid: every pair of specialists at 50/50, under two fusion formulas.

  average:   r*A + (1-r)*B
  arithmetic (task arithmetic): base + min(1, 2r)*(A - base) + min(1, 2(1-r))*(B - base)

Skill = specialty score with the trigger present (context=1, special_only). For each blend we report
how much of each parent's advantage survives: (blend - other parent) / (parent - other parent).
"""
import itertools
import sys
from pathlib import Path

import numpy as np

import fksim

BRAINS = Path(__file__).resolve().parent.parent / "brains"
KINDS = [k for k in fksim.KINDS if k != "base"]
EP = 128
SUFFIX = sys.argv[1] if len(sys.argv) > 1 else ""


def load(k):
    p = BRAINS / f"{k}{SUFFIX}.best.bin"
    return np.fromfile(p if p.exists() else BRAINS / f"{k}.best.bin", np.float32)


def skill(w, task):
    return fksim.evaluate(w, task, EP, context=1, special_only=True)


def average(a, b, r, base):
    return r * a + (1 - r) * b


def arithmetic(a, b, r, base):
    return base + min(1.0, 2 * r) * (a - base) + min(1.0, 2 * (1 - r)) * (b - base)


base = np.fromfile(BRAINS / "base.best.bin", np.float32)
w = {k: load(k) for k in KINDS}
own = {k: {t: skill(w[k], t) for t in KINDS} for k in KINDS}
everyday = {k: fksim.evaluate(w[k], "base", EP) for k in KINDS}

print(f"base brain everyday score {fksim.evaluate(base, 'base', EP):.2f}")
print("specialty scores with trigger present (rows = mind, cols = task), last col = everyday task")
print(" " * 9 + "".join(f"{t[:7]:>8}" for t in KINDS) + "  everyday")
for k in KINDS:
    print(f"{k:9s}" + "".join(f"{own[k][t]:8.2f}" for t in KINDS) + f"  {everyday[k]:8.2f}")

for name, fuse in [("average", average), ("arithmetic", arithmetic)]:
    print(f"\n== {name} fusion, 50/50: share of each parent's advantage kept")
    keeps = []
    for a, b in itertools.combinations(KINDS, 2):
        ga, gb = own[a][a] - own[b][a], own[b][b] - own[a][b]
        if ga < 0.5 or gb < 0.5:
            print(f"{a:8s} + {b:8s}  n/a (parents score about the same on one task)")
            continue
        m = fuse(w[a], w[b], 0.5, base)
        ka = (skill(m, a) - own[b][a]) / ga
        kb = (skill(m, b) - own[a][b]) / gb
        ev = fksim.evaluate(m, "base", EP)
        _, traits, _ = fksim.trial(m)
        keeps.append((ka, kb))
        tag = "BOTH" if min(ka, kb) > 0.5 else "hollow?" if max(ka, kb) < 0.2 else ""
        print(f"{a:8s} + {b:8s}  keep {ka:5.2f} / {kb:5.2f}  everyday {ev:5.2f}  {tag:7s} {', '.join(traits)}")
    k = np.array(keeps)
    print(f"{len(k)} pairs: {(k.min(1) > 0.5).sum()} keep both (>50% each), {(k.max(1) < 0.2).sum()} lose both, "
          f"mean kept {k.mean():.2f}")
