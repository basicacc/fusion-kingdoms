using System.Numerics;

namespace FusionKingdoms.Sim;

public enum MapSize { Small, Normal, Large }
public enum MapStyle { Random, Valleys, Highlands, Wildwood, Lakes, Plains }

// Everything a player can tune before a match.
public sealed record MapSettings(
    MapSize Size = MapSize.Normal, MapStyle Style = MapStyle.Random, float Resources = 1f, float StartingStock = 1f,
    float Minutes = 20f, float Wildlife = 1f)
{
    public static MapSettings Default = new();
}

// A new map every match. Terrain comes from layered noise (height, moisture, river ridges) cut at percentiles so
// each style keeps a sensible mix; roads are least-cost paths with bridges; resources and wild spots are placed by
// rules that guarantee every Keep a woodlot, a quarry and fields. The map is point-mirrored, so it is always fair.
public static class MapGen
{
    sealed record StyleMix(float Water, float Marsh, float Forest, float Hills, float Rock, float Rivers);

    static StyleMix Mix(MapStyle s) => s switch
    {
        MapStyle.Highlands => new(0.03f, 0.03f, 0.16f, 0.20f, 0.09f, 0.015f),
        MapStyle.Wildwood => new(0.03f, 0.06f, 0.40f, 0.07f, 0.03f, 0.02f),
        MapStyle.Lakes => new(0.14f, 0.07f, 0.18f, 0.07f, 0.03f, 0.01f),
        MapStyle.Plains => new(0.02f, 0.03f, 0.12f, 0.06f, 0.02f, 0.012f),
        _ => new(0.05f, 0.05f, 0.24f, 0.10f, 0.04f, 0.025f),
    };

    public static (int w, int h) Dimensions(MapSize s) => s switch
    {
        MapSize.Small => (144, 108),
        MapSize.Large => (240, 180),
        _ => (192, 144),
    };

    public static Match Create1v1(MindFactory f, IReadOnlyDictionary<string, Mind> baseMinds, IEnumerable<Mind> playerLibrary,
        int seed, string playerName = "Blue Crown", string aiName = "Red Crown", IEnumerable<Mind>? secondLibrary = null,
        MapSettings? settings = null)
    {
        settings ??= MapSettings.Default;
        var rng = new Random(seed);
        var style = settings.Style == MapStyle.Random ? (MapStyle)(1 + rng.Next(5)) : settings.Style;
        var (W, H) = Dimensions(settings.Size);
        var w = new World(W, H, seed);
        var gen = new Gen(w, rng, Mix(style), settings);
        gen.Terrain();

        var kingdoms = new Kingdom[2];
        for (int team = 0; team < 2; team++)
        {
            var pos = team == 0 ? gen.Keep0 : gen.Mirror(gen.Keep0);
            var keep = w.AddBuilding(BuildingType.Keep, team, pos);
            keep.Radius = 2f;
            float s = settings.StartingStock;
            keep.Stored[(int)ResourceType.Food] = 100f * s;
            keep.Stored[(int)ResourceType.Timber] = 120f * s;
            keep.Stored[(int)ResourceType.Stone] = 40f * s;
            keep.Stored[(int)ResourceType.Lumen] = 25f * s;
            var k = new Kingdom { Team = team, Name = team == 0 ? playerName : aiName, IsAi = team == 1 && secondLibrary == null, Keep = keep };
            k.Library.AddRange(team == 0 ? playerLibrary : secondLibrary ?? baseMinds.Values);
            kingdoms[team] = k;
        }

        var match = new Match(w, kingdoms, f, seed) { Length = settings.Minutes * 60f, Style = style };
        gen.HomeBuildings(match);
        gen.Resources();
        foreach (var k in kingdoms)
        {
            k.Ruler = w.AddUnit(k.Team, Species.Human, match.FreeNear(k.Home + new Vector2(0f, k.Team == 0 ? 3f : -3f), 0.5f), new MoveOrderController());
            k.Ruler.IsRuler = true;
            foreach (var kind in new[] { "Farmer", "Farmer", "Builder", "Builder", "Scout" })
            {
                var m = k.Library.FirstOrDefault(x => x.Name == kind) ?? baseMinds[kind];
                match.Spawn(k, m, k.Home + new Vector2(k.Team == 0 ? 3f : -3f, 0f));
            }
        }
        gen.WildPlaces(match, f, baseMinds);
        w.Nav?.Invalidate();
        return match;
    }

    readonly record struct Cell(int X, int Y, float H, float M, float R);

    sealed class Gen(World w, Random rng, StyleMix mix, MapSettings settings)
    {
        readonly int W = w.Width, H = w.Height;
        public Vector2 Keep0;
        Vector2 Center => new((int)(W / 2f) + 0.5f, (int)(H / 2f) + 0.5f);
        readonly List<Vector2> _lumen = new();
        readonly List<Vector2> _poi = new();
        float[] _clear = [];

        public Vector2 Mirror(Vector2 p) => new(W - p.X, H - p.Y);
        bool FirstHalf(int x, int y) => (x + 0.5f) / W + (y + 0.5f) / H < 1f;
        Terrain At(int x, int y) => x < 0 || y < 0 || x >= W || y >= H ? Sim.Terrain.Rock : w.Map[y * W + x];
        Terrain At(Vector2 p) => At((int)p.X, (int)p.Y);

        // Set a tile and its mirror together.
        void Set(int x, int y, Terrain t)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            w.SetTerrain(x, y, t);
            w.SetTerrain(W - 1 - x, H - 1 - y, t);
        }

        static float Fade(float d, float inner, float outer) => d <= inner ? 1f : d >= outer ? 0f : 1f - (d - inner) / (outer - inner);

        static IEnumerable<(int x, int y)> Neighbours(int x, int y)
        {
            yield return (x + 1, y); yield return (x - 1, y); yield return (x, y + 1); yield return (x, y - 1);
        }

        static Vector2 Snap(Vector2 p) => new((int)p.X + 0.5f, (int)p.Y + 0.5f);

        // ---------- terrain ----------

        public void Terrain()
        {
            Keep0 = Snap(new Vector2(W * (0.10f + 0.10f * rng.NextSingle()), H * (0.16f + 0.22f * rng.NextSingle())));
            var height = new ValueNoise(rng.Next());
            var moist = new ValueNoise(rng.Next());
            var bog = new ValueNoise(rng.Next());
            float fh = 4.5f / W, fm = 6f / W, fb = 9f / W;
            // Averaging a field with its own mirror image makes it point-symmetric with no seam.
            float Sym(ValueNoise n, float f, float x, float y) => 0.5f * (n.Fbm5(x * f, y * f) + n.Fbm5((W - x) * f, (H - y) * f));

            _clear = new float[W * H];
            var cells = new Cell[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float dk = MathF.Min(Vector2.Distance(p, Keep0), Vector2.Distance(p, Mirror(Keep0)));
                _clear[y * W + x] = MathF.Max(Fade(dk, 11f, 17f), Fade(Vector2.Distance(p, Center), 8f, 13f));
                cells[y * W + x] = new Cell(x, y, Sym(height, fh, p.X, p.Y), Sym(moist, fm, p.X, p.Y), Sym(bog, fb, p.X, p.Y));
            }
            var wild = cells.Where(c => _clear[c.Y * W + c.X] < 0.2f).ToList();
            float Top(Func<Cell, float> key, float fraction)
            {
                var vals = wild.Select(key).OrderBy(v => v).ToList();
                return vals[Math.Clamp((int)((1f - fraction) * vals.Count), 0, vals.Count - 1)];
            }
            float rockT = Top(c => c.H, mix.Rock), hillT = Top(c => c.H, mix.Rock + mix.Hills);
            float waterT = Top(c => -c.H, mix.Water);
            float forestT = Top(c => c.M, mix.Forest);
            float marshT = Top(c => c.R, mix.Marsh * 2.5f);
            foreach (var c in cells)
            {
                // Near Keeps and the centre the land opens up gradually: thresholds rise with the clearing.
                float clear = _clear[c.Y * W + c.X];
                float lift = clear * 0.35f;
                var t = Sim.Terrain.Grass;
                if (clear > 0.6f) t = Sim.Terrain.Grass;
                else if (-c.H >= waterT + lift) t = Sim.Terrain.River;
                else if (c.H >= rockT + lift) t = Sim.Terrain.Rock;
                else if (c.H >= hillT + lift) t = c.M >= forestT ? Sim.Terrain.Forest : Sim.Terrain.Hills;
                else if (c.M >= forestT + lift) t = Sim.Terrain.Forest;
                else if (c.R >= marshT + lift && c.H < hillT) t = Sim.Terrain.Marsh;
                w.SetTerrain(c.X, c.Y, t);
            }
            Rivers();
            // Lake and river shores turn boggy here and there.
            for (int y = 1; y < H - 1; y++)
            for (int x = 1; x < W - 1; x++)
                if (FirstHalf(x, y) && At(x, y) == Sim.Terrain.Grass && _clear[y * W + x] < 0.3f
                    && Neighbours(x, y).Any(n => At(n.x, n.y) == Sim.Terrain.River) && rng.Next(4) == 0)
                    Set(x, y, Sim.Terrain.Marsh);
            Roads();
        }

        // Rivers are traced as meandering lines: one flows from the map edge through the middle (a ford at the
        // Lumen Heart), mirrored into a single river that splits the two realms; another winds along each flank.
        void Rivers()
        {
            int count = mix.Rivers <= 0.012f ? 1 : 2;
            var startMain = new Vector2(W * (0.45f + 0.3f * rng.NextSingle()), 0f);
            Trace(startMain, Center, mix.Rivers > 0.02f ? 2 : 1);
            if (count > 1)
            {
                var a = new Vector2(0f, H * (0.55f + 0.3f * rng.NextSingle()));
                var b = new Vector2(W * (0.3f + 0.2f * rng.NextSingle()), H);
                Trace(a, b, 1);
            }
        }

        void Trace(Vector2 from, Vector2 to, int halfWidth)
        {
            var p = from;
            float wander = rng.NextSingle() * MathF.Tau;
            for (int step = 0; step < W * 4 && Vector2.Distance(p, to) > 1.5f; step++)
            {
                var dir = Vector2.Normalize(to - p);
                wander += (rng.NextSingle() - 0.5f) * 0.5f;
                var bend = new Vector2(MathF.Cos(wander), MathF.Sin(wander)) * 0.7f;
                // Steer clear of the Keeps.
                foreach (var k in new[] { Keep0, Mirror(Keep0) })
                {
                    var away = p - k;
                    float d = away.Length();
                    if (d < 20f && d > 0.01f) bend += away / d * (20f - d) * 0.15f;
                }
                p += Vector2.Normalize(dir + bend);
                for (int dy = -halfWidth; dy <= halfWidth; dy++)
                for (int dx = -halfWidth; dx <= halfWidth; dx++)
                {
                    int x = (int)p.X + dx, y = (int)p.Y + dy;
                    if (x < 0 || y < 0 || x >= W || y >= H || dx * dx + dy * dy > halfWidth * halfWidth + 1) continue;
                    if (_clear[y * W + x] > 0.6f) continue;   // fords at the Keeps' fields and the Heart
                    Set(x, y, Sim.Terrain.River);
                }
            }
        }

        // ---------- roads ----------

        void Roads()
        {
            var toCenter = Center - Keep0;
            var side = Vector2.Normalize(new Vector2(-toCenter.Y, toCenter.X));
            var well1 = Keep0 + toCenter * (0.5f + 0.1f * rng.NextSingle()) + side * (W * (0.08f + 0.05f * rng.NextSingle()));
            var well2 = Keep0 + toCenter * (0.72f + 0.08f * rng.NextSingle()) - side * (W * (0.08f + 0.05f * rng.NextSingle()));
            foreach (var p in new[] { well1, well2 })
            {
                var q = Snap(Vector2.Clamp(p, new Vector2(5f), new Vector2(W - 5f, H - 5f)));
                ClearBlob(q, 3f, Sim.Terrain.Grass);
                _lumen.Add(q);
            }
            Path(Keep0, Center);
            Path(Keep0, _lumen[0]);
            Path(_lumen[1], Center);
        }

        void ClearBlob(Vector2 c, float r, Terrain t)
        {
            for (int y = (int)(c.Y - r); y <= (int)(c.Y + r); y++)
            for (int x = (int)(c.X - r); x <= (int)(c.X + r); x++)
                if (Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) <= r) Set(x, y, t);
        }

        static float Cost(Terrain t) => t switch
        {
            Sim.Terrain.Road => 0.4f, Sim.Terrain.Grass => 1f, Sim.Terrain.Farmland => 1f, Sim.Terrain.Hills => 2.5f,
            Sim.Terrain.Forest => 3f, Sim.Terrain.Marsh => 4f, Sim.Terrain.River => 14f, _ => 40f,
        };

        // Cheapest path over the terrain; it becomes a 2-wide road, bridging water and cutting through rock.
        void Path(Vector2 from, Vector2 to)
        {
            int s = (int)from.Y * W + (int)from.X, g = (int)to.Y * W + (int)to.X;
            var dist = new float[W * H];
            var prev = new int[W * H];
            Array.Fill(dist, float.MaxValue);
            Array.Fill(prev, -1);
            var pq = new PriorityQueue<int, float>();
            dist[s] = 0f;
            pq.Enqueue(s, 0f);
            while (pq.TryDequeue(out int cur, out _))
            {
                if (cur == g) break;
                int cx = cur % W, cy = cur / W;
                foreach (var (nx, ny) in Neighbours(cx, cy))
                {
                    if (nx < 1 || ny < 1 || nx >= W - 1 || ny >= H - 1) continue;
                    int n = ny * W + nx;
                    float nd = dist[cur] + Cost(w.Map[n]) * (0.85f + 0.3f * Hash(nx, ny));
                    if (nd >= dist[n]) continue;
                    dist[n] = nd;
                    prev[n] = cur;
                    pq.Enqueue(n, nd + Vector2.Distance(new(nx, ny), to) * 0.5f);
                }
            }
            for (int c = g; c >= 0; c = prev[c])
            {
                int x = c % W, y = c / W;
                foreach (var (ox, oy) in new[] { (0, 0), (1, 0), (0, 1) })
                    Set(x + ox, y + oy, Sim.Terrain.Road);
            }
        }

        static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093 ^ y * 19349663 ^ 0x5bd1e995);
                h = (h ^ (h >> 13)) * 1274126177;
                return (h & 0xFFFF) / 65535f;
            }
        }

        // ---------- buildings, fields and resources ----------

        bool Open(Vector2 c, float r, bool allowHills = false)
        {
            for (int y = (int)MathF.Floor(c.Y - r); y <= (int)(c.Y + r); y++)
            for (int x = (int)MathF.Floor(c.X - r); x <= (int)(c.X + r); x++)
            {
                if (MathF.Abs(x + 0.5f - c.X) > r || MathF.Abs(y + 0.5f - c.Y) > r) continue;
                var t = At(x, y);
                if (t != Sim.Terrain.Grass && !(allowHills && t == Sim.Terrain.Hills)) return false;
            }
            return true;
        }

        Vector2? Find(Vector2 around, float rmin, float rmax, Func<Vector2, bool> ok, int tries = 600)
        {
            for (int i = 0; i < tries; i++)
            {
                float a = rng.NextSingle() * MathF.Tau, d = rmin + (rmax - rmin) * MathF.Sqrt(rng.NextSingle());
                var p = Snap(around + new Vector2(MathF.Cos(a), MathF.Sin(a)) * d);
                if (p.X < 3 || p.Y < 3 || p.X > W - 3 || p.Y > H - 3 || !FirstHalf((int)p.X, (int)p.Y)) continue;
                if (ok(p)) return p;
            }
            return null;
        }

        // The Cradle and two starting Farms beside each Keep, on clear ground and off the roads.
        public void HomeBuildings(Match m)
        {
            var taken = new List<(Vector2 p, float r)> { (Keep0, 3f) };
            bool Free(Vector2 p, float r) => Open(p, r) && taken.All(t => Vector2.Distance(t.p, p) > t.r + r + 1.2f);
            var cradle = Find(Keep0, 5f, 8f, p => Free(p, 1.6f)) ?? Keep0 + new Vector2(6f, 1f);
            taken.Add((cradle, 1.6f));
            var farms = new List<Vector2>();
            for (int i = 0; i < 2; i++)
                if (Find(Keep0, 5f, 11f, p => Free(p, 1.6f)) is { } f) { taken.Add((f, 1.6f)); farms.Add(f); }
            foreach (var team in new[] { 0, 1 })
            {
                Vector2 P(Vector2 v) => team == 0 ? v : Mirror(v);
                var c = w.AddBuilding(BuildingType.Cradle, team, P(cradle));
                c.Radius = Defs.Of(BuildingType.Cradle).Radius;
                foreach (var f in farms)
                {
                    var fb = w.AddBuilding(BuildingType.Farm, team, P(f));
                    fb.Radius = 1.5f;
                    m.Plough(fb);
                }
            }
        }

        void Node(ResourceType t, Vector2 p, float amount)
        {
            w.AddNode(t, p, amount);
            w.AddNode(t, Mirror(p), amount);
        }

        bool NearAny(Vector2 p, float r) =>
            w.Buildings.Any(b => Vector2.Distance(b.Pos, p) < b.Radius + r) || w.Nodes.Any(n => Vector2.Distance(n.Pos, p) < r);

        public void Resources()
        {
            float amt = settings.Resources;
            // Trees: mostly on forest edges, where they can be reached.
            for (int y = 1; y < H - 1; y++)
            for (int x = 1; x < W - 1; x++)
            {
                if (!FirstHalf(x, y) || At(x, y) != Sim.Terrain.Forest) continue;
                bool edge = Neighbours(x, y).Any(n => At(n.x, n.y) is Sim.Terrain.Grass or Sim.Terrain.Road or Sim.Terrain.Hills);
                if (rng.Next(edge ? 4 : 14) == 0) Node(ResourceType.Timber, new Vector2(x + 0.5f, y + 0.5f), 80f * amt);
            }
            // Stone: in the hills and along rock faces.
            for (int y = 1; y < H - 1; y++)
            for (int x = 1; x < W - 1; x++)
            {
                if (!FirstHalf(x, y)) continue;
                var t = At(x, y);
                if (t == Sim.Terrain.Hills && rng.Next(12) == 0) Node(ResourceType.Stone, new Vector2(x + 0.5f, y + 0.5f), 100f * amt);
                if (t == Sim.Terrain.Rock && rng.Next(5) == 0)
                    foreach (var (nx, ny) in Neighbours(x, y))
                        if (At(nx, ny) is Sim.Terrain.Grass or Sim.Terrain.Hills) { Node(ResourceType.Stone, new Vector2(nx + 0.5f, ny + 0.5f), 100f * amt); break; }
            }
            // Every Keep gets a woodlot and a quarry within a short walk, grown if nature didn't provide one.
            int Near(ResourceType t, float r) => w.Nodes.Count(n => n.Type == t && Vector2.Distance(n.Pos, Keep0) < r);
            if (Near(ResourceType.Timber, 18f) < 8 && Find(Keep0, 11f, 16f, p => Open(p, 2.5f) && !NearAny(p, 4f)) is { } wood)
            {
                ClearBlob(wood, 2.8f, Sim.Terrain.Forest);
                for (int i = 0; i < 10; i++)
                {
                    var p = Snap(wood + new Vector2(rng.NextSingle() * 5f - 2.5f, rng.NextSingle() * 5f - 2.5f));
                    if (At(p) == Sim.Terrain.Forest && !NearAny(p, 0.9f)) Node(ResourceType.Timber, p, 120f * amt);
                }
            }
            if (Near(ResourceType.Stone, 20f) < 4 && Find(Keep0, 11f, 17f, p => Open(p, 2f, true) && !NearAny(p, 4f)) is { } quarry)
            {
                ClearBlob(quarry, 2f, Sim.Terrain.Hills);
                foreach (var o in new Vector2[] { new(0, 0), new(1, 1), new(-1, 1), new(1, -1) })
                    Node(ResourceType.Stone, Snap(quarry + o), 200f * amt);
            }
            // Lumen: the Heart in the middle and two wells on each side.
            w.AddNode(ResourceType.Lumen, Center, 1000f * amt);
            foreach (var p in _lumen) Node(ResourceType.Lumen, p, 600f * amt);
        }

        // ---------- wild tribes and ruins ----------

        bool[]? _reach;

        // Tiles a Keep's subjects can walk to: wild places must be reachable, or tribes get stranded.
        bool Reachable(Vector2 p)
        {
            if (_reach == null)
            {
                _reach = new bool[W * H];
                var q = new Queue<int>();
                int s = (int)Keep0.Y * W + (int)Keep0.X;
                _reach[s] = true;
                q.Enqueue(s);
                while (q.Count > 0)
                {
                    int c = q.Dequeue();
                    foreach (var (nx, ny) in Neighbours(c % W, c / W))
                    {
                        if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                        int n = ny * W + nx;
                        if (_reach[n] || TerrainInfo.Blocked(w.Map[n])) continue;
                        _reach[n] = true;
                        q.Enqueue(n);
                    }
                }
            }
            int x = (int)p.X, y = (int)p.Y;
            if (x < 1 || y < 1 || x >= W - 1 || y >= H - 1 || !_reach[y * W + x]) return false;
            // Not a one-tile nook either: a little room around it must be reachable too.
            int room = 0;
            for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                if (x + dx >= 0 && y + dy >= 0 && x + dx < W && y + dy < H && _reach[(y + dy) * W + x + dx]) room++;
            return room >= 15;
        }

        Vector2? Spot(Func<Terrain, bool> terrain, float minFromKeep) =>
            Find(Center, 4f, Vector2.Distance(Center, Keep0) + 10f, p =>
                terrain(At(p)) && Reachable(p) && Vector2.Distance(p, Keep0) > minFromKeep && Vector2.Distance(p, Center) > 7f
                && !NearAny(p, 4f) && _poi.All(q => Vector2.Distance(q, p) > 9f), 2000);

        public void WildPlaces(Match m, MindFactory f, IReadOnlyDictionary<string, Mind> baseMinds)
        {
            Mind Wild(string name, string from, int level, Species body)
            {
                var mind = f.Make(name, Mutation.Mutate(baseMinds[from].Brain, level, new Random(rng.Next())), [baseMinds[from]], 1f, ["wild"]);
                mind.Vessel = body;
                return mind;
            }

            var dens = new (Mind mind, Func<Terrain, bool> where, int count, bool raids)[]
            {
                (Wild("Grove Sprite", "Scout", 1, Species.Sprite), t => t is Sim.Terrain.Forest or Sim.Terrain.Grass, 4, false),
                (Wild("Stone Troll", "Guard", 2, Species.Troll), t => t is Sim.Terrain.Hills or Sim.Terrain.Grass, 2, false),
                (Wild("Mire Fox", "Thief", 1, Species.FoxKin), t => t is Sim.Terrain.Marsh or Sim.Terrain.Forest or Sim.Terrain.Grass, 3, true),
                (Wild("Lumen Moth", "Hoarder", 1, Species.MothKin), t => t is Sim.Terrain.Grass or Sim.Terrain.Hills, 3, false),
                (Wild("Hermit Sage", "Healer", 1, Species.Sage), t => t is Sim.Terrain.Grass or Sim.Terrain.Forest, 1, false),
            };
            foreach (var (mind, where, count, raids) in dens)
            {
                if (Spot(where, 26f) is not { } at) continue;
                _poi.Add(at);
                foreach (var pos in new[] { at, Mirror(at) })
                {
                    var den = m.W.AddBuilding(BuildingType.Den, 2, pos);
                    den.Radius = 1f;
                    if (raids) den.Stored[(int)ResourceType.Food] = 15f;
                    int n = Math.Max(1, (int)MathF.Round(count * settings.Wildlife));
                    var tribe = new Tribe { Name = mind.Name, Mind = mind, Species = mind.Vessel, Den = den, Target = n, Raids = raids };
                    m.Tribes.Add(tribe);
                    for (int i = 0; i < n; i++)
                    {
                        var u = m.W.AddUnit(2, mind.Vessel, m.FreeNear(den.Pos, 1.5f), m.WildController(tribe));
                        u.Mind = mind;
                        if (raids) m.AimRaid(u);
                    }
                }
            }

            // Ruins on the flank: an Ancient Altar, the merchant's stop and the Sealed One's tomb.
            var ruin = Spot(t => t is Sim.Terrain.Grass or Sim.Terrain.Hills, 30f) ?? Center + new Vector2(-12f, 8f);
            _poi.Add(ruin);
            foreach (var pos in new[] { ruin, Mirror(ruin) })
            {
                var altar = m.W.AddBuilding(BuildingType.AncientAltar, 2, pos);
                altar.Radius = 1.2f;
                m.MerchantSpots.Add(m.FreeNear(pos + new Vector2(4f, 2f), 0f));
            }
            m.Merchant = m.W.AddBuilding(BuildingType.Merchant, 2, m.MerchantSpots[0]);
            m.Merchant.Radius = 0.8f;
            var sealedMind = f.Make("The Sealed One", Mutation.Mutate(baseMinds["Hunter"].Brain, 2, new Random(rng.Next())), [baseMinds["Hunter"]], 1f, ["sealed"]);
            sealedMind.Vessel = Species.Troll;
            m.SealedMind = sealedMind;
            m.SealedAt = m.FreeNear(rng.Next(2) == 0 ? ruin + new Vector2(-3f, -2f) : Mirror(ruin) + new Vector2(3f, 2f), 0f);
        }
    }
}

public sealed class ValueNoise(int seed)
{
    float Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    float Sample(float x, float y)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        float a = Hash(x0, y0), b = Hash(x0 + 1, y0), c = Hash(x0, y0 + 1), d = Hash(x0 + 1, y0 + 1);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    public float Fbm(float x, float y) => (Sample(x, y) * 0.6f + Sample(x * 2.1f, y * 2.1f) * 0.3f + Sample(x * 4.3f, y * 4.3f) * 0.1f);

    // Five octaves, for natural-looking terrain.
    public float Fbm5(float x, float y)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int o = 0; o < 5; o++)
        {
            sum += Sample(x + o * 17.3f, y - o * 9.1f) * amp;
            norm += amp;
            amp *= 0.5f;
            x *= 2.03f;
            y *= 2.03f;
        }
        return sum / norm;
    }
}
