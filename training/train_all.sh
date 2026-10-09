#!/usr/bin/env bash
# Full pipeline: base brain, then the 8 context-gated specialists fine-tuned from it, 4 at a time.
# Trigger-gated distillation plus RL: in episodes with its trigger a specialist imitates its teacher
# (training/teachers/<kind>.bin, the best earlier version of that mind); without the trigger it imitates the
# base brain ("hold"). Both pulls are constant, so the specialty stays confined to its trigger, and PPO
# keeps improving on top of them.
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p logs
[ -n "${SKIP_BASE:-}" ] || .venv/bin/python -u ppo.py --kind base --steps "${BASE_STEPS:-30e6}" > logs/base.log 2>&1

run() {
  local teacher=()
  [ -f "teachers/$1.bin" ] && teacher=(--teacher "teachers/$1.bin" --kick 0.5 --kick-constant)
  .venv/bin/python -u ppo.py --kind "$1" --init ../brains/base.best.bin --init-critic ../brains/base.critic.pt \
    --hold ../brains/base.best.bin --hold-weight 0.5 "${teacher[@]}" \
    --lr 3e-4 --anchor 1e-3 --steps "${STEPS:-12e6}" --envs 256 --threads 4 --seed 6 > "logs/$1.log" 2>&1
}

for batch in "thief hunter guard builder" "healer farmer scout hoarder"; do
  for k in $batch; do run "$k" & done
  wait
done
echo "pipeline done"
