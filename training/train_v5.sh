#!/usr/bin/env bash
# v5: fine-tune the weakest specialists from their own current weights (same base, so fusion stays consistent),
# held to the base outside their trigger and kick-started by themselves. Output: brains/<kind>.v5.best.bin
set -euo pipefail
cd "$(dirname "$0")"
run() {
  .venv/bin/python -u ppo.py --kind "$1" --name "$1.v5" --init "../brains/$1.best.bin" --init-critic ../brains/base.critic.pt \
    --hold ../brains/base.best.bin --hold-weight 0.5 --teacher "../brains/$1.best.bin" --kick 0.3 \
    --lr 1e-4 --anchor 1e-3 --steps "${STEPS:-10e6}" --envs 256 --threads 4 --seed 21 > "logs/$1.v5.log" 2>&1
}
for k in guard healer hoarder thief; do run "$k" & done
wait
echo done > logs/v5.done
