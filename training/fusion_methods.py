"""Compare fusion formulas over every specialist pair at 50/50.

A good formula keeps both parents' specialties AND the everyday work every subject needs.
Reports per formula: pairs keeping >50% of both skills, pairs losing both, mean skill kept,
and mean everyday score of the blends relative to the base brain.
"""
import itertools
from pathlib import Path

import numpy as np

import fksim

BRAINS = Path(__file__).resolve().parent.parent / "brains"
KINDS = [k for k in fksim.KINDS if k != "base"]
EP = 96

base = np.fromfile(BRAINS / "base.best.bin", np.float32)
w = {k: np.fromfile(BRAINS / f"{k}.best.bin", np.float32) for k in KINDS}


def skill(x, task):
    return fksim.evaluate(x, task, EP, context=1, special_only=True)


def arithmetic(lam):
    def f(a, b):
        return base + lam * ((a - base) + (b - base))
    return f


def ties(density=0.2, lam=1.0):
    """TIES merging: keep each parent's largest changes, elect a sign per weight, average the agreeing ones."""
    def f(a, b):
        deltas = []
        for d in (a - base, b - base):
            k = int(len(d) * density)
            cut = np.partition(np.abs(d), -k)[-k]
            deltas.append(np.where(np.abs(d) >= cut, d, 0.0))
        d = np.stack(deltas)
        sign = np.sign(d.sum(0))
        agree = (np.sign(d) == sign) & (d != 0)
        merged = np.where(agree, d, 0.0).sum(0) / np.maximum(agree.sum(0), 1)
        return (base + lam * merged).astype(np.float32)
    return f


METHODS = {
    "arith 1.0": arithmetic(1.0),
    "arith 0.75": arithmetic(0.75),
    "ties 20%": ties(0.2, 1.0),
    "ties 20% x1.5": ties(0.2, 1.5),
}

own = {k: {t: skill(w[k], t) for t in KINDS} for k in KINDS}
base_everyday = fksim.evaluate(base, "base", EP)
print(f"base everyday {base_everyday:.2f}")
print("specialists: own skill, everyday")
for k in KINDS:
    print(f"  {k:8s} {own[k][k]:6.2f}  {fksim.evaluate(w[k], 'base', EP):6.2f}")

pairs = [(a, b) for a, b in itertools.combinations(KINDS, 2)
         if own[a][a] - own[b][a] >= 0.5 and own[b][b] - own[a][b] >= 0.5]
print(f"\n{len(pairs)} comparable pairs")
for name, fuse in METHODS.items():
    keeps, everyday, rows = [], [], []
    for a, b in pairs:
        m = fuse(w[a], w[b])
        ka = (skill(m, a) - own[b][a]) / (own[a][a] - own[b][a])
        kb = (skill(m, b) - own[a][b]) / (own[b][b] - own[a][b])
        ev = fksim.evaluate(m, "base", EP) / base_everyday
        keeps.append((ka, kb))
        everyday.append(ev)
        rows.append(f"    {a:8s}+{b:8s} {ka:5.2f} / {kb:5.2f}  everyday {ev:4.2f}")
    k = np.array(keeps)
    good = ((k.min(1) > 0.5) & (np.array(everyday) > 0.6)).sum()
    print(f"\n== {name}: keep both {(k.min(1) > 0.5).sum()}, lose both {(k.max(1) < 0.2).sum()}, "
          f"mean kept {k.mean():.2f}, mean everyday {np.mean(everyday):.2f}, both skills AND everyday>60%: {good}")
    print("\n".join(rows))
