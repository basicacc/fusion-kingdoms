#!/usr/bin/env bash
# v6 (roadmap item 5): a multi-task base brain. Continues the current base on a batch where every environment runs
# a different specialist's drill (kind "all"), so the base itself learns a little of every job. Specialists are then
# fine-tuned from it, kick-started by the current specialists. Fusion adds smaller, less conflicting deltas on top of a
# base that already knows the basics, which should help clashing pairs like Thief+Guard keep both skills.
# Output: brains/<kind>.v6.best.bin; gate with: cd tools/headless && dotnet run -c Release -- 1 1 compare current .v6
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p logs
.venv/bin/python -u ppo.py --kind all --name base.v6 --init ../brains/base.best.bin --init-critic ../brains/base.critic.pt \
  --lr 1e-4 --context 0.5 --steps "${BASE_STEPS:-20e6}" --seed 31 > logs/base.v6.log 2>&1

run() {
  .venv/bin/python -u ppo.py --kind "$1" --name "$1.v6" --init ../brains/base.v6.best.bin --init-critic ../brains/base.v6.critic.pt \
    --hold ../brains/base.v6.best.bin --hold-weight 0.5 --teacher "../brains/$1.best.bin" --kick 0.5 --kick-constant \
    --lr 3e-4 --anchor 1e-3 --steps "${STEPS:-10e6}" --envs 256 --threads 4 --seed 36 > "logs/$1.v6.log" 2>&1
}
for batch in "thief hunter guard builder" "healer farmer scout hoarder"; do
  for k in $batch; do run "$k" & done
  wait
done
echo done > logs/v6.done
