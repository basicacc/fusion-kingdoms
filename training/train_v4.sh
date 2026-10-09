#!/usr/bin/env bash
# v4: retrain on the post-overhaul rules (building = work at the site). Continues from the current brains so
# fusion stays compatible: base first, then all eight specialists from the new base, each kick-started by its
# current version (except the Builder, whose job changed). Outputs brains/<kind>.v4.best.bin for gating.
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p logs
.venv/bin/python -u ppo.py --kind base --name base.v4 --init ../brains/base.best.bin --init-critic ../brains/base.critic.pt \
  --lr 1e-4 --steps "${BASE_STEPS:-15e6}" --seed 11 > logs/base.v4.log 2>&1

run() {
  local teacher=()
  [ "$1" != "builder" ] && [ -f "../brains/$1.best.bin" ] && teacher=(--teacher "../brains/$1.best.bin" --kick 0.5 --kick-constant)
  .venv/bin/python -u ppo.py --kind "$1" --name "$1.v4" --init ../brains/base.v4.best.bin --init-critic ../brains/base.v4.critic.pt \
    --hold ../brains/base.v4.best.bin --hold-weight 0.5 "${teacher[@]}" \
    --lr 3e-4 --anchor 1e-3 --steps "${STEPS:-12e6}" --envs 256 --threads 4 --seed 16 > "logs/$1.v4.log" 2>&1
}

for batch in "thief hunter guard builder" "healer farmer scout hoarder"; do
  for k in $batch; do run "$k" & done
  wait
done
echo "v4 done" > logs/v4.done
