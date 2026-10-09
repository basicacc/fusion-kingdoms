"""PPO trainer for Fusion Kingdoms brains.

Base:       python ppo.py --kind base --steps 30e6
Specialist: python ppo.py --kind farmer --init ../brains/base.bin --lr 1e-4 --anchor 1e-3 --steps 10e6
"""
import argparse
import time
from pathlib import Path

import numpy as np
import torch
import torch.nn as nn

import fksim

BRAINS = Path(__file__).resolve().parent.parent / "brains"


class Actor(nn.Module):
    def __init__(self):
        super().__init__()
        self.l1 = nn.Linear(fksim.OBS, fksim.H1)
        self.l2 = nn.Linear(fksim.H1, fksim.H2)
        self.l3 = nn.Linear(fksim.H2, fksim.MOVES + fksim.ACTIONS)
        nn.init.orthogonal_(self.l3.weight, 0.01)
        nn.init.zeros_(self.l3.bias)

    def forward(self, x):
        return self.l3(torch.tanh(self.l2(torch.tanh(self.l1(x)))))

    def flat(self):
        parts = [self.l1.weight, self.l1.bias, self.l2.weight, self.l2.bias, self.l3.weight, self.l3.bias]
        return torch.cat([p.detach().reshape(-1) for p in parts]).numpy().astype(np.float32)

    def load_flat(self, w):
        w = torch.as_tensor(w)
        i = 0
        for p in [self.l1.weight, self.l1.bias, self.l2.weight, self.l2.bias, self.l3.weight, self.l3.bias]:
            n = p.numel()
            p.data.copy_(w[i:i + n].reshape(p.shape))
            i += n
        assert i == fksim.WEIGHTS


def critic():
    return nn.Sequential(nn.Linear(fksim.OBS, 128), nn.Tanh(), nn.Linear(128, 128), nn.Tanh(), nn.Linear(128, 1))


def dists(logits):
    m = torch.distributions.Categorical(logits=logits[:, :fksim.MOVES])
    a = torch.distributions.Categorical(logits=logits[:, fksim.MOVES:])
    return m, a


def imitate(teacher, obs, student_logits):
    """Cross-entropy from the teacher's move and action choices to the student's."""
    with torch.no_grad():
        tl = teacher(obs)
    M = fksim.MOVES
    return (-(torch.softmax(tl[:, :M], -1) * torch.log_softmax(student_logits[:, :M], -1)).sum(-1)
            - (torch.softmax(tl[:, M:], -1) * torch.log_softmax(student_logits[:, M:], -1)).sum(-1)).mean()


def save_brain(actor, name):
    BRAINS.mkdir(exist_ok=True)
    actor.flat().tofile(BRAINS / f"{name}.bin")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--kind", default="base", choices=fksim.KINDS)
    ap.add_argument("--name")
    ap.add_argument("--init")
    ap.add_argument("--init-critic")
    ap.add_argument("--steps", type=float, default=30e6)
    ap.add_argument("--envs", type=int, default=512)
    ap.add_argument("--horizon", type=int, default=64)
    ap.add_argument("--lr", type=float, default=3e-4)
    ap.add_argument("--ent", type=float, default=0.01)
    ap.add_argument("--anchor", type=float, default=0.0)
    ap.add_argument("--seed", type=int, default=0)
    ap.add_argument("--threads", type=int, default=8)
    ap.add_argument("--context", type=float, default=0.6, help="share of episodes with the specialist trigger")
    ap.add_argument("--teacher", help="kickstart: brain whose choices to imitate in trigger episodes")
    ap.add_argument("--kick", type=float, default=1.0, help="teacher weight, fades to 0 by mid-training")
    ap.add_argument("--kick-constant", action="store_true", help="keep the teacher weight constant instead of fading")
    ap.add_argument("--hold", help="brain to imitate whenever the trigger is absent (keeps the specialty confined)")
    ap.add_argument("--hold-weight", type=float, default=0.5)
    args = ap.parse_args()
    name = args.name or args.kind

    torch.manual_seed(args.seed)
    torch.set_num_threads(args.threads)
    actor, value = Actor(), critic()
    anchor = None
    if args.init:
        actor.load_flat(np.fromfile(args.init, np.float32))
        anchor = [p.detach().clone() for p in actor.parameters()]
    if args.init_critic:
        value.load_state_dict(torch.load(args.init_critic))
    teacher = None
    if args.teacher:
        teacher = Actor()
        teacher.load_flat(np.fromfile(args.teacher, np.float32))
        teacher.requires_grad_(False)
    holder = None
    if args.hold:
        holder = Actor()
        holder.load_flat(np.fromfile(args.hold, np.float32))
        holder.requires_grad_(False)
    opt = torch.optim.Adam(list(actor.parameters()) + list(value.parameters()), lr=args.lr, eps=1e-5)

    env = fksim.VecEnv(args.envs, args.kind, args.seed)
    env.set_context_chance(args.context)
    obs = torch.as_tensor(env.reset())
    ctx = torch.as_tensor(env.ctx())
    N, T = args.envs, args.horizon
    iters = int(args.steps // (N * T))
    gamma, lam, clip, epochs, mb = 0.99, 0.95, 0.2, 4, 4096
    best, recent = -1e9, []
    t0 = time.time()

    for it in range(iters):
        for g in opt.param_groups:
            g["lr"] = args.lr * (1 - it / iters)
        b_obs = torch.zeros(T, N, fksim.OBS)
        b_m = torch.zeros(T, N, dtype=torch.long)
        b_a = torch.zeros(T, N, dtype=torch.long)
        b_lp = torch.zeros(T, N)
        b_r = torch.zeros(T, N)
        b_d = torch.zeros(T, N)
        b_v = torch.zeros(T, N)
        b_c = torch.zeros(T, N, dtype=torch.bool)
        fade = 1.0 if args.kick_constant else max(0.0, 1 - 2 * it / iters)
        kick = args.kick * fade if teacher is not None else 0.0
        with torch.no_grad():
            for t in range(T):
                logits = actor(obs)
                dm, da = dists(logits)
                m, a = dm.sample(), da.sample()
                b_obs[t], b_m[t], b_a[t], b_c[t] = obs, m, a, ctx
                b_lp[t] = dm.log_prob(m) + da.log_prob(a)
                b_v[t] = value(obs).squeeze(-1)
                o, r, d = env.step(m.numpy(), a.numpy())
                b_r[t], b_d[t] = torch.as_tensor(r), torch.as_tensor(d, dtype=torch.float32)
                obs = torch.as_tensor(o)
                ctx = torch.as_tensor(env.ctx())
            last_v = value(obs).squeeze(-1)
            adv = torch.zeros(T, N)
            gae = torch.zeros(N)
            for t in reversed(range(T)):
                nv = last_v if t == T - 1 else b_v[t + 1]
                nonterm = 1.0 - b_d[t]
                delta = b_r[t] + gamma * nv * nonterm - b_v[t]
                gae = delta + gamma * lam * nonterm * gae
                adv[t] = gae
            ret = adv + b_v

        f_obs, f_m, f_a = b_obs.reshape(-1, fksim.OBS), b_m.reshape(-1), b_a.reshape(-1)
        f_lp, f_adv, f_ret = b_lp.reshape(-1), adv.reshape(-1), ret.reshape(-1)
        f_c = b_c.reshape(-1)
        for _ in range(epochs):
            perm = torch.randperm(N * T)
            for s in range(0, N * T, mb):
                idx = perm[s:s + mb]
                logits = actor(f_obs[idx])
                dm, da = dists(logits)
                lp = dm.log_prob(f_m[idx]) + da.log_prob(f_a[idx])
                ratio = (lp - f_lp[idx]).exp()
                ad = f_adv[idx]
                ad = (ad - ad.mean()) / (ad.std() + 1e-8)
                pg = -torch.min(ratio * ad, ratio.clamp(1 - clip, 1 + clip) * ad).mean()
                vl = 0.5 * (value(f_obs[idx]).squeeze(-1) - f_ret[idx]).pow(2).mean()
                ent = (dm.entropy() + da.entropy()).mean()
                loss = pg + 0.5 * vl - args.ent * ent
                c = f_c[idx]
                if kick > 0 and c.any():
                    loss = loss + kick * imitate(teacher, f_obs[idx][c], logits[c])
                if holder is not None and (~c).any():
                    loss = loss + args.hold_weight * imitate(holder, f_obs[idx][~c], logits[~c])
                if anchor is not None and args.anchor > 0:
                    loss = loss + args.anchor * sum(((p - a0) ** 2).sum() for p, a0 in zip(actor.parameters(), anchor))
                opt.zero_grad()
                loss.backward()
                nn.utils.clip_grad_norm_(list(actor.parameters()) + list(value.parameters()), 0.5)
                opt.step()

        mean, count = env.stats()
        if count:
            recent = (recent + [mean])[-10:]
        steps = (it + 1) * N * T
        if it % 10 == 0 or it == iters - 1:
            avg = np.mean(recent) if recent else float("nan")
            print(f"[{name}] it {it}/{iters} steps {steps/1e6:.1f}M return {avg:.3f} "
                  f"ent {ent.item():.2f} sps {steps/(time.time()-t0):.0f}", flush=True)
            save_brain(actor, name)
            torch.save(value.state_dict(), BRAINS / f"{name}.critic.pt")
            if recent and avg > best:
                best = avg
                save_brain(actor, f"{name}.best")
    env.close()
    print(f"[{name}] done, best avg return {best:.3f}")


if __name__ == "__main__":
    main()
