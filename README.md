# Fusion Kingdoms

A strategy game where every unit is a real neural network. Breed minds, fuse them, and steal your rival's best.

- **Play:** https://endureeyes.itch.io/fusion-kingdoms (Windows and Linux builds)
- **Engine:** Godot 4.7.2 (.NET / C#)

## How it works
- Each unit runs its own small neural network (47 → 64 → 64 → 18, 8,402 weights), trained with PPO reinforcement learning.
- Specialist brains (Farmer, Builder, Guard, Scout, Thief, Hunter, Healer, Hoarder) are combined by weight fusion into 36 classes.
- AI rulers plan the economy, defence, research and campaigns, adapt to what they scout, and are tuned by self-play.

## Layout
| Folder | What |
|---|---|
| `scripts/` | Godot game code (UI, match view, menus, audio) |
| `sim/` | Simulation library: units, brains, fusion, map generator, AI ruler |
| `sim-native/` | NativeAOT build of the sim for fast Python training |
| `training/` | PyTorch PPO training scripts |
| `brains/` | Trained brain weights used by the game |
| `tools/headless/` | Headless test tool: AI-vs-AI matches and the automated test suite |
| `tools/art`, `tools/audio` | Scripts that generate all art and sound |

## Run from source
Open the folder in Godot 4.7.2 .NET and press Play.

Tests: `dotnet run --project tools/headless -c Release -- <seed> 1 npctest`
