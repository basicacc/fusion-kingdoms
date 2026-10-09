using FusionKingdoms.Sim;
using Godot;

namespace FusionKingdoms;

// Paints the whole map once: tile variants, soft blended borders between terrains, shorelines, decorations,
// tree trunks on the ground layer and tree canopies on a separate layer drawn above units.
public static class TerrainBaker
{
    const int T = 16;
    const int Variants = 6;

    // Higher priority terrain spills over the edges of lower priority neighbours.
    static int Priority(Terrain t) => t switch
    {
        Terrain.River => 0, Terrain.Marsh => 1, Terrain.Grass => 2, Terrain.Forest => 3,
        Terrain.Hills => 4, Terrain.Farmland => 5, Terrain.Road => 6, _ => 7,
    };

    static uint Hash(int x, int y, int salt)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + salt * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177;
            return h ^ (h >> 16);
        }
    }

    public sealed record Result(Image Ground, Image Canopy, Image Water, Image Mini);

    public static Result Bake(World w, int seed)
    {
        var tiles = ArtLibrary.Img("terrain/tiles");
        var decor = ArtLibrary.Img("terrain/decor");
        var trunks = ArtLibrary.Img("terrain/tree_trunks");
        var canopies = ArtLibrary.Img("terrain/tree_canopies");
        int W = w.Width * T, H = w.Height * T;
        var ground = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
        var canopy = Image.CreateEmpty(W, H, false, Image.Format.Rgba8);
        var water = Image.CreateEmpty(w.Width, w.Height, false, Image.Format.L8);

        Terrain At(int x, int y) => w.Map[Math.Clamp(y, 0, w.Height - 1) * w.Width + Math.Clamp(x, 0, w.Width - 1)];
        Rect2I Src(Terrain t, int x, int y) =>
            new((int)(Hash(x, y, seed) % Variants) * T, (int)t * T, T, T);

        // 1. Base tiles.
        for (int y = 0; y < w.Height; y++)
        for (int x = 0; x < w.Width; x++)
        {
            var t = At(x, y);
            ground.BlitRect(tiles, Src(t, x, y), new Vector2I(x * T, y * T));
            if (t is Terrain.River or Terrain.Marsh) water.SetPixel(x, y, new Color(t == Terrain.River ? 1f : 0.6f, 0, 0));
        }

        // 2. Organic borders: a neighbour with higher priority bleeds into this tile along a wobbly edge.
        for (int y = 0; y < w.Height; y++)
        for (int x = 0; x < w.Width; x++)
        {
            var t = At(x, y);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var n = At(x + dx, y + dy);
                if (Priority(n) <= Priority(t)) continue;
                var src = Src(n, x + dx, y + dy);
                for (int py = 0; py < T; py++)
                for (int px = 0; px < T; px++)
                {
                    float depth = 3.2f + (Hash(x * T + px, y * T + py, 7) % 100) / 100f * 1.6f
                                  + MathF.Sin((x * T + px) * 0.45f + (y * T + py) * 0.3f) * 0.9f;
                    float dist = (dx, dy) switch
                    {
                        (0, -1) => py, (0, 1) => T - 1 - py, (-1, 0) => px, (1, 0) => T - 1 - px,
                        (-1, -1) => MathF.Sqrt(px * px + py * py), (1, -1) => MathF.Sqrt((T - 1 - px) * (T - 1 - px) + py * py),
                        (-1, 1) => MathF.Sqrt(px * px + (T - 1 - py) * (T - 1 - py)),
                        _ => MathF.Sqrt((T - 1 - px) * (T - 1 - px) + (T - 1 - py) * (T - 1 - py)),
                    };
                    if (dist >= depth) continue;
                    var c = tiles.GetPixel(src.Position.X + px, src.Position.Y + py);
                    // A darker rim where higher ground meets lower ground reads as a soft edge.
                    if (dist >= depth - 1f) c = c.Darkened(0.18f);
                    ground.SetPixel(x * T + px, y * T + py, c);
                }
            }
        }

        // 3. Shorelines: foam on the water side, wet mud on the land side.
        var foam = new Color("cfefff");
        var mud = new Color("5a4a36");
        for (int py = 1; py < H - 1; py++)
        for (int px = 1; px < W - 1; px++)
        {
            bool wet = At(px / T, py / T) == Terrain.River;
            if (!wet) continue;
            foreach (var (ox, oy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = px + ox, ny = py + oy;
                if (At(nx / T, ny / T) == Terrain.River) continue;
                if (Hash(px, py, 3) % 3 != 0) ground.SetPixel(px, py, foam);
                ground.SetPixel(nx, ny, mud);
            }
        }

        // 4. Decorations on open ground.
        for (int y = 0; y < w.Height; y++)
        for (int x = 0; x < w.Width; x++)
        {
            var t = At(x, y);
            uint h = Hash(x, y, seed + 11);
            int kind = -1;
            if (t == Terrain.Grass && h % 100 < 5)
            {
                int[] calm = [0, 4, 1, 5, 0, 4, 6, 10, 1, 4, 2];
                kind = calm[(int)(h / 100 % (uint)calm.Length)];
            }
            else if (t == Terrain.Hills && h % 100 < 12) kind = h % 2 == 0 ? 1 : 7;
            else if (t == Terrain.Forest && h % 100 < 10) kind = h % 2 == 0 ? 2 : 3;
            if (kind < 0) continue;
            var src = new Rect2I(kind % 6 * T, kind / 6 * T, T, T);
            ground.BlendRect(decor, src, new Vector2I(x * T + (int)(h >> 8) % 5 - 2, y * T + (int)(h >> 12) % 5 - 2));
        }

        // 5. Trees: trunks on the ground, canopies on their own layer.
        for (int y = 0; y < w.Height; y++)
        for (int x = 0; x < w.Width; x++)
        {
            if (At(x, y) != Terrain.Forest) continue;
            uint h = Hash(x, y, seed + 29);
            if (h % 100 < 22) continue;
            int variant = (int)(h % 8);
            int ox = x * T - 4 + (int)(h >> 4) % 7 - 3, oy = y * T - 16 + (int)(h >> 9) % 5 - 2;
            var src = new Rect2I(variant * 24, 0, 24, 32);
            ground.BlendRect(trunks, src, new Vector2I(ox, oy));
            canopy.BlendRect(canopies, src, new Vector2I(ox, oy));
        }

        // A small map picture for the minimap.
        var mini = ground.Duplicate() as Image ?? throw new InvalidOperationException();
        mini.Resize(w.Width * 2, w.Height * 2, Image.Interpolation.Bilinear);
        return new Result(ground, canopy, water, mini);
    }
}
