using System.Numerics;

namespace FusionKingdoms.Sim;

public static class Maps
{
    // Mind Lab test arena: your Keep on the left, a rival Keep on the right, every resource in between.
    public static World Lab(int seed)
    {
        const int w = 40, h = 26;
        var world = new World(w, h, seed) { Time = 30f };
        var rng = new Random(seed);

        void Fill(Terrain t, int x0, int y0, int x1, int y1, float chance = 1f)
        {
            for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                if (rng.NextSingle() < chance) world.SetTerrain(x, y, t);
        }

        Fill(Terrain.Forest, 13, 2, 18, 7, 0.85f);
        Fill(Terrain.Forest, 13, 18, 19, 23, 0.85f);
        Fill(Terrain.Rock, 22, 9, 23, 11);
        Fill(Terrain.Hills, 20, 8, 25, 13, 0.25f);
        Fill(Terrain.Marsh, 25, 19, 29, 23, 0.7f);
        Fill(Terrain.Road, 7, 13, 33, 13);

        var k0 = world.AddBuilding(BuildingType.Keep, 0, new Vector2(5.5f, 13f));
        world.AddBuilding(BuildingType.BuildSite, 0, new Vector2(9.5f, 8.5f));
        var k1 = world.AddBuilding(BuildingType.Keep, 1, new Vector2(34.5f, 13f));
        k1.Stored[(int)ResourceType.Food] = 60f;
        k1.Stored[(int)ResourceType.Timber] = 25f;
        k1.Stored[(int)ResourceType.Lumen] = 10f;

        foreach (var (x, y) in new[] { (3, 9), (5, 9), (3, 17), (5, 17), (8, 16) })
        {
            world.SetTerrain(x, y, Terrain.Farmland);
            world.AddNode(ResourceType.Food, new Vector2(x + 0.5f, y + 0.5f));
        }
        foreach (var (x, y) in new[] { (37, 9), (35, 17), (31, 16) })
        {
            world.SetTerrain(x, y, Terrain.Farmland);
            world.AddNode(ResourceType.Food, new Vector2(x + 0.5f, y + 0.5f));
        }
        for (int x = 0; x < w; x++)
        for (int y = 0; y < h; y++)
            if (world.Map[y * w + x] == Terrain.Forest && rng.Next(6) == 0)
                world.AddNode(ResourceType.Timber, new Vector2(x + 0.5f, y + 0.5f), 60f);
        foreach (var p in new Vector2[] { new(21.5f, 10.5f), new(24.5f, 9.5f), new(23f, 12.5f) })
            world.AddNode(ResourceType.Stone, p, 60f);
        foreach (var p in new Vector2[] { new(27.5f, 6.5f), new(28.5f, 15.5f), new(26f, 11f) })
            world.AddNode(ResourceType.Lumen, p, 50f);

        var ruler = world.AddUnit(0, Species.Human, k0.Pos + new Vector2(0f, 2.2f), null);
        ruler.IsRuler = true;

        foreach (var p in new Vector2[] { new(16f, 4.5f), new(16.5f, 20.5f) })
            world.AddUnit(2, Species.Sprite, p, new SpriteScript(p));
        var tp = new Vector2(23.5f, 7.5f);
        world.AddUnit(2, Species.Troll, tp, new TrollScript(tp));
        return world;
    }
}
