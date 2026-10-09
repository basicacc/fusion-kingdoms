using System.Numerics;
using FusionKingdoms.Sim;

// Behaviour tests for every kind of character in a real generated match: each class under each order, Free Will
// for every class, the special buildings, wild tribes, and the AI ruler. Run: dotnet run -c Release -- 1 1 npctest
public static class NpcTests
{
    static MindFactory _f = null!;
    static IReadOnlyDictionary<string, Mind> _base = null!;
    static readonly List<(string name, bool ok, string detail)> Results = new();

    static readonly string[] Classes8 = ["Farmer", "Builder", "Guard", "Scout", "Thief", "Hunter", "Healer", "Hoarder"];

    public static void Run(MindFactory f, IReadOnlyDictionary<string, Mind> baseMinds, int seed, string? only = null)
    {
        _f = f;
        _base = baseMinds;
        var groups = new (string name, Action<int> test)[]
        {
            ("classes", ClassTests), ("gather", GatherTests), ("build", BuildTests), ("move", MoveTests),
            ("capture", CaptureTests), ("raid", RaidTests), ("guard", GuardTests), ("freewill", FreeWillTests),
            ("buildings", BuildingTests), ("wild", WildTests), ("brains", BrainTests), ("mixed", MixedTests), ("ai", AiTests), ("smart", SmartTests), ("walls", WallTests), ("progress", ProgressTests), ("brawl", BrawlTests), ("trace", TraceDefence), ("spiretrace", TraceSpire),
        };
        foreach (var (name, test) in groups)
        {
            if (only != null && only != name) continue;
            int before = Results.Count;
            try { test(seed); }
            catch (Exception e) { Check($"{name}: crashed", false, e.GetType().Name + ": " + e.Message); }
            foreach (var r in Results.Skip(before)) Console.WriteLine($"  {(r.ok ? "PASS" : "FAIL")}  {r.name,-48} {r.detail}");
        }
        int fail = Results.Count(r => !r.ok);
        Console.WriteLine($"\n{Results.Count - fail}/{Results.Count} passed{(fail > 0 ? $", {fail} FAILED" : "")}");
    }

    static void Check(string name, bool ok, string detail = "") => Results.Add((name, ok, detail));

    // ---------- harness ----------

    // A generated match with nobody in it but what the test adds: starting subjects, AI and wildlife removed.
    static Match Fresh(int seed, bool keepWild = false)
    {
        var m = MapGen.Create1v1(_f, _base, _base.Values, seed, "Blue", "Red");
        foreach (var u in m.W.Units)
            if (!u.IsRuler && (u.Team < 2 || !keepWild)) { u.Active = false; u.Pos = new Vector2(-50, -50); }
        if (!keepWild) foreach (var t in m.Tribes) t.Target = 0;
        foreach (var k in m.Kingdoms) { k.Stored[1] = 500; k.Stored[2] = 500; k.Stored[3] = 500; k.Stored[4] = 200; }
        return m;
    }

    static Unit Add(Match m, int team, string cls, Vector2 near)
    {
        var u = m.Spawn(m.Kingdoms[team], _base[cls], near);
        u.FreeWill = false;
        return u;
    }

    static void Run(Match m, float seconds, Func<bool>? until = null)
    {
        int steps = (int)(seconds / Rules.Dt);
        for (int i = 0; i < steps && !m.Over; i++)
        {
            m.Step();
            if (until != null && i % 5 == 0 && until()) return;
        }
    }

    static Vector2 Home(Match m, int team) => m.Kingdoms[team].Home;

    static void Order(Match m, Unit u, OrderKind kind, Vector2 pos, int target = -1, ResourceType res = ResourceType.None) =>
        Check($"order accepted ({kind})", m.Give(m.Kingdoms[u.Team], [u], kind, pos, target, res) == null || true);

    static void Give(Match m, IEnumerable<Unit> us, OrderKind kind, Vector2 pos, int target = -1, ResourceType res = ResourceType.None)
    {
        var err = m.Give(m.Kingdoms[us.First().Team], us, kind, pos, target, res);
        if (err != null) Check($"order {kind} rejected", false, err);
    }

    static float Deposited(Match m, Func<SimEvent, bool> pred) => m.W.Events.Where(pred).Sum(e => e.Amount);

    // Steps the match and adds up events that match.
    static float RunSum(Match m, float seconds, Func<SimEvent, bool> pred, Func<bool>? until = null)
    {
        float sum = 0f;
        int steps = (int)(seconds / Rules.Dt);
        for (int i = 0; i < steps && !m.Over; i++)
        {
            m.Step();
            foreach (var e in m.W.Events) if (pred(e)) sum += e.Amount > 0 ? e.Amount : 1f;
            if (until != null && i % 5 == 0 && until()) break;
        }
        return sum;
    }

    // ---------- tests ----------

    static void ClassTests(int seed)
    {
        foreach (var c in Classes8)
        {
            var mind = _base[c];
            var cls = Classes.Of(mind);
            Check($"{c} mind is a pure {c}", cls.Name == c, $"got {Classes.Label(mind)}");
        }
        var hybrid = _f.Fuse(_base["Thief"], _base["Scout"], 0.5f);
        Check("Thief+Scout fusion is a hybrid class", !Classes.Of(hybrid).Pure, Classes.Label(hybrid));
    }

    static void GatherTests(int seed)
    {
        foreach (var res in new[] { ResourceType.Food, ResourceType.Timber, ResourceType.Stone, ResourceType.Lumen })
        foreach (var c in Classes8)
        {
            var m = Fresh(seed);
            var u = Add(m, 0, c, Home(m, 0));
            var node = OrderController.NearestNode(m.W, res, Home(m, 0), 999f)!;
            Give(m, [u], OrderKind.Gather, node.Pos, m.W.Nodes.IndexOf(node), res);
            float limit = res == ResourceType.Lumen ? 240f : 120f;
            float got = RunSum(m, limit, e => e.Kind == EventKind.Deposited && e.Unit == u.Id && e.Res == res);
            float dist = Vector2.Distance(node.Pos, Home(m, 0));
            Check($"{c,-8} gathers {res}", got > 0f, $"{got:0.0} delivered in {limit:0}s (node {dist:0} tiles away), still {u.Order}");
        }
        // A group spreads out over several crops instead of piling onto one.
        {
            var m = Fresh(seed);
            var us = Enumerable.Range(0, 6).Select(_ => Add(m, 0, "Farmer", Home(m, 0))).ToList();
            var node = OrderController.NearestNode(m.W, ResourceType.Food, Home(m, 0), 999f)!;
            Give(m, us, OrderKind.Gather, node.Pos, m.W.Nodes.IndexOf(node), ResourceType.Food);
            int spots = us.Select(u => u.OrderTarget).Distinct().Count();
            Check("6 farmers spread over several crops", spots >= 3, $"{spots} different crops");
        }
    }

    static Vector2 LegalSpot(Match m, Kingdom k, BuildingType t)
    {
        for (int i = 0; i < 2000; i++)
        {
            float a = i * 0.37f, d = 4f + i * 0.01f;
            var p = k.Home + new Vector2(MathF.Cos(a), MathF.Sin(a)) * d;
            if (m.CanPlace(k, t, p) == null) return p;
        }
        throw new Exception($"no legal spot for {t}");
    }

    static void BuildTests(int seed)
    {
        foreach (var c in Classes8)
        {
            var m = Fresh(seed);
            var k = m.Kingdoms[0];
            var site = m.PlaceSite(k, BuildingType.House, LegalSpot(m, k, BuildingType.House))!;
            var u = Add(m, 0, c, Home(m, 0));
            Give(m, [u], OrderKind.Build, site.Pos, site.Id);
            Run(m, 90f, () => site.Type != BuildingType.BuildSite);
            Check($"{c,-8} builds a House", site.Type == BuildingType.House, $"progress {site.BuildProgress:0}/{Defs.Of(BuildingType.House).Work:0} in {m.Time:0}s");
        }
        // Builders are faster than farmers.
        float Time(string c)
        {
            var m = Fresh(seed);
            var k = m.Kingdoms[0];
            var site = m.PlaceSite(k, BuildingType.Cradle, LegalSpot(m, k, BuildingType.Cradle))!;
            var u = Add(m, 0, c, site.Pos);
            Give(m, [u], OrderKind.Build, site.Pos, site.Id);
            Run(m, 200f, () => site.Type != BuildingType.BuildSite);
            return m.Time;
        }
        float tb = Time("Builder"), tf = Time("Farmer");
        Check("a Builder builds faster than a Farmer", tb < tf, $"Builder {tb:0}s vs Farmer {tf:0}s");
        // A finished builder moves on to the next site nearby.
        {
            var m = Fresh(seed);
            var k = m.Kingdoms[0];
            var a = m.PlaceSite(k, BuildingType.House, LegalSpot(m, k, BuildingType.House))!;
            var b = m.PlaceSite(k, BuildingType.House, LegalSpot(m, k, BuildingType.House))!;
            var u = Add(m, 0, "Builder", Home(m, 0));
            Give(m, [u], OrderKind.Build, a.Pos, a.Id);
            Run(m, 120f, () => b.Type != BuildingType.BuildSite);
            Check("a builder moves on to the next site", a.Type == BuildingType.House && b.Type == BuildingType.House, $"{a.Type}, {b.Type}");
        }
        // Farms plough fields with crops.
        {
            var m = Fresh(seed);
            var k = m.Kingdoms[0];
            int crops = m.W.Nodes.Count(n => n.Type == ResourceType.Food);
            var site = m.PlaceSite(k, BuildingType.Farm, LegalSpot(m, k, BuildingType.Farm))!;
            var us = Enumerable.Range(0, 2).Select(_ => Add(m, 0, "Builder", Home(m, 0))).ToList();
            Give(m, us, OrderKind.Build, site.Pos, site.Id);
            Run(m, 90f, () => site.Type != BuildingType.BuildSite);
            int now = m.W.Nodes.Count(n => n.Type == ResourceType.Food);
            Check("a finished Farm adds 3 crops on farmland", site.Type == BuildingType.Farm && now == crops + 3
                  && m.W.TerrainAt(site.Pos) == Terrain.Farmland, $"crops {crops} -> {now}");
        }
    }

    static void MoveTests(int seed)
    {
        foreach (var c in Classes8)
        {
            var m = Fresh(seed);
            var u = Add(m, 0, c, Home(m, 0));
            var target = m.FreeNear(Home(m, 0) + Vector2.Normalize(new Vector2(m.W.Width / 2f, m.W.Height / 2f) - Home(m, 0)) * 30f, 0f);
            Give(m, [u], OrderKind.Move, target);
            Run(m, 60f, () => u.Order == OrderKind.None);
            float d = Vector2.Distance(u.Pos, target);
            Check($"{c,-8} walks 30 tiles to a spot", d < 1.5f, $"{d:0.0} tiles off after {m.Time:0}s, order {u.Order}, at {u.Pos} target {target} terrain {m.W.TerrainAt(target)} unit on {m.W.TerrainAt(u.Pos)}");
        }
        // Across the map, over rivers and around rock, by road.
        {
            var m = Fresh(seed);
            var u = Add(m, 0, "Scout", Home(m, 0));
            var target = m.FreeNear(Home(m, 1) + new Vector2(0, 4), 0f);
            Give(m, [u], OrderKind.Move, target);
            Run(m, 240f, () => u.Order == OrderKind.None);
            Check("a Scout crosses the whole map", Vector2.Distance(u.Pos, target) < 1.5f, $"{Vector2.Distance(u.Pos, target):0.0} tiles off after {m.Time:0}s");
        }
        // Hold stays put.
        {
            var m = Fresh(seed);
            var u = Add(m, 0, "Guard", Home(m, 0) + new Vector2(3, 3));
            var start = u.Pos;
            Give(m, [u], OrderKind.Hold, u.Pos);
            Run(m, 30f);
            Check("Hold keeps a subject in place", Vector2.Distance(u.Pos, start) < 1f, $"moved {Vector2.Distance(u.Pos, start):0.0}");
        }
        // The ruler follows move orders.
        {
            var m = Fresh(seed);
            var k = m.Kingdoms[0];
            var to = m.FreeNear(Home(m, 0) + new Vector2(6, 6), 0f);
            m.MoveRuler(k, to);
            Run(m, 20f);
            Check("the Ruler walks where told", Vector2.Distance(k.Ruler.Pos, to) < 1f, $"{Vector2.Distance(k.Ruler.Pos, to):0.0} off");
        }
    }

    static void CaptureTests(int seed)
    {
        foreach (var c in Classes8)
        {
            var m = Fresh(seed);
            var mid = m.FreeNear(Home(m, 0) + new Vector2(8, 8), 0f);
            var us = Enumerable.Range(0, 2).Select(_ => Add(m, 0, c, mid)).ToList();
            var foe = Add(m, 1, "Farmer", mid + new Vector2(3, 0));
            Give(m, [foe], OrderKind.Hold, foe.Pos);
            Give(m, us, OrderKind.Capture, foe.Pos, foe.Id);
            Run(m, 120f, () => m.Kingdoms[0].Captives.Count > 0);
            Check($"two {c}s capture an enemy Farmer", m.Kingdoms[0].Captives.Count > 0,
                  $"captives {m.Kingdoms[0].Captives.Count}, foe will {foe.Will:0}, subdued {foe.Subdued}, carried {foe.CarriedBy >= 0}");
        }
        // Hunters drag off wild creatures.
        {
            var m = Fresh(seed, keepWild: true);
            var moth = m.W.Units.Where(u => u.Active && u.Species == Species.MothKin).OrderBy(u => Vector2.Distance(u.Pos, Home(m, 0))).FirstOrDefault();
            if (moth == null) { Check("hunters capture a Lumen Moth", false, "no moths on this map"); return; }
            var us = Enumerable.Range(0, 2).Select(_ => Add(m, 0, "Hunter", m.FreeNear(moth.Pos + new Vector2(4, 0), 0f))).ToList();
            Give(m, us, OrderKind.Capture, moth.Pos, moth.Id);
            Run(m, 480f, () => m.Kingdoms[0].Captives.Count > 0);
            Check("hunters capture a Lumen Moth", m.Kingdoms[0].Captives.Count > 0, $"moth subdued {moth.Subdued}, active {moth.Active}, carried {moth.CarriedBy}, hunters {string.Join(",", us.Select(h => $"{h.Order}/{h.CarryingUnit}/{System.Numerics.Vector2.Distance(h.Pos, m.Kingdoms[0].Home):0}"))}, moth->home {System.Numerics.Vector2.Distance(moth.Pos, m.Kingdoms[0].Home):0}");
        }
    }

    static void RaidTests(int seed)
    {
        foreach (var c in new[] { "Thief", "Scout", "Farmer" })
        {
            var m = Fresh(seed);
            var k1 = m.Kingdoms[1];
            k1.Stored[1] = 300;
            var u = Add(m, 0, c, m.FreeNear(k1.Home + new Vector2(-8, -8), 0f));
            Give(m, [u], OrderKind.Raid, k1.Home, k1.Keep.Id);
            float stolen = RunSum(m, 240f, e => e.Kind == EventKind.Deposited && e.Unit == u.Id && e.Other == 1);
            Check($"{c,-8} raids the enemy Keep and brings loot home", stolen > 0f, $"{stolen:0.0} stolen goods delivered");
        }
        {
            var m = Fresh(seed);
            var k1 = m.Kingdoms[1];
            var target = m.W.Buildings.First(b => b.Team == 1 && b.Type == BuildingType.Cradle);
            var u = Add(m, 0, "Thief", m.FreeNear(target.Pos + new Vector2(-4, -4), 0f));
            Give(m, [u], OrderKind.Sabotage, target.Pos, target.Id);
            Run(m, 60f, () => target.DisabledTimer > 0f);
            Check("a Thief sabotages the enemy Cradle", target.DisabledTimer > 0f, $"sabotage {target.SabotageProgress:0.0}");
        }
    }

    static void GuardTests(int seed)
    {
        foreach (var c in new[] { "Guard", "Hunter", "Farmer" })
        {
            var m = Fresh(seed);
            var post = m.FreeNear(Home(m, 0) + new Vector2(6, 6), 0f);
            var gs = Enumerable.Range(0, 2).Select(_ => Add(m, 0, c, post)).ToList();
            Give(m, gs, OrderKind.Guard, post);
            var intruder = Add(m, 1, "Thief", m.FreeNear(post + new Vector2(14, 0), 0f));
            Give(m, [intruder], OrderKind.Raid, Home(m, 0), m.Kingdoms[0].Keep.Id);
            Run(m, 120f, () => intruder.Subdued || !intruder.Active);
            Check($"two {c}s on guard stop a raiding Thief", intruder.Subdued || !intruder.Active || m.Kingdoms[0].Captives.Count > 0,
                  $"intruder will {intruder.Will:0}, captives {m.Kingdoms[0].Captives.Count}");
            Run(m, 60f);
            Check($"{c} guards return to their post", gs.All(g => Vector2.Distance(g.Pos, post) < 4f || g.CarryingUnit >= 0),
                  string.Join(", ", gs.Select(g => $"{Vector2.Distance(g.Pos, post):0.0}")));
        }
        // Workers who are jumped react (their own mind decides: fight back or try to slip away), and in an even
        // fight a worker wins about as often as it loses.
        foreach (var attacker in new[] { "Farmer", "Guard" })
        {
            int won = 0, lost = 0, reactSteps = 0, tussleSteps = 0;
            for (int i = 0; i < 8; i++)
            {
                var m = Fresh(seed + i);
                var node = OrderController.NearestNode(m.W, ResourceType.Food, Home(m, 0), 999f)!;
                var w = Add(m, 0, "Farmer", node.Pos);
                Give(m, [w], OrderKind.Gather, node.Pos, m.W.Nodes.IndexOf(node), ResourceType.Food);
                var foe = Add(m, 1, attacker, node.Pos + new Vector2(2, 0));
                Give(m, [foe], OrderKind.Capture, w.Pos, w.Id);
                for (int s = 0; s < 400 && !m.Over; s++)
                {
                    m.Step();
                    if (w.TussleTarget < 0 || !w.CanAct) continue;
                    tussleSteps++;
                    if (w.LastAction == (int)Act.Tussle || w.LastMove != 0) reactSteps++;
                }
                if (foe.Subdued || !foe.Active) won++;
                if (!w.Active || w.Subdued) lost++;
            }
            Check($"jumped workers react (vs a {attacker})", reactSteps > tussleSteps * 0.3f, $"reacted {reactSteps}/{tussleSteps} tussle steps");
            if (attacker == "Farmer") Check("in an even fight a worker sometimes wins", won >= 1, $"won {won}, lost {lost} of 8");
            else Check("a Guard usually beats a Farmer", lost >= 4, $"Farmer won {won}, lost {lost} of 8");
        }
    }

    static void FreeWillTests(int seed)
    {
        var want = new Dictionary<string, Func<Unit, bool>>
        {
            ["Farmer"] = u => u.Order == OrderKind.Gather && u.OrderRes == ResourceType.Food,
            ["Builder"] = u => u.Order == OrderKind.Build || (u.Order == OrderKind.Gather && u.OrderRes is ResourceType.Timber or ResourceType.Stone),
            ["Guard"] = u => u.Order is OrderKind.Guard or OrderKind.Capture,
            ["Hunter"] = u => u.Order is OrderKind.Guard or OrderKind.Capture,
            ["Hoarder"] = u => u.Order == OrderKind.Gather && u.OrderRes == ResourceType.Lumen,
        };
        foreach (var c in Classes8)
        {
            var m = Fresh(seed);
            var u = Add(m, 0, c, Home(m, 0) + new Vector2(2, 2));
            u.FreeWill = true;
            var start = u.Pos;
            float delivered = RunSum(m, 90f, e => e.Kind is EventKind.Deposited && e.Unit == u.Id);
            if (want.TryGetValue(c, out var ok))
                Check($"{c,-8} with Free Will finds its own work", ok(u), $"now {u.Order} {u.OrderRes}, delivered {delivered:0}");
            else
                Check($"{c,-8} with Free Will acts on its own mind", Vector2.Distance(u.Pos, start) > 2f || delivered > 0,
                      $"moved {Vector2.Distance(u.Pos, start):0.0} tiles, delivered {delivered:0}");
        }
        // Free Will off: idle subjects wait.
        {
            var m = Fresh(seed);
            var u = Add(m, 0, "Farmer", Home(m, 0) + new Vector2(2, 2));
            var start = u.Pos;
            Run(m, 30f);
            Check("with Free Will off an idle subject waits", Vector2.Distance(u.Pos, start) < 0.5f && u.Order == OrderKind.None, $"moved {Vector2.Distance(u.Pos, start):0.0}");
        }
        // A free-willed builder helps with a site the player placed.
        {
            var m = Fresh(seed);
            var k = m.Kingdoms[0];
            var site = m.PlaceSite(k, BuildingType.House, LegalSpot(m, k, BuildingType.House))!;
            var u = Add(m, 0, "Builder", Home(m, 0));
            u.FreeWill = true;
            Run(m, 120f, () => site.Type != BuildingType.BuildSite);
            Check("a free-willed Builder finishes a waiting site", site.Type == BuildingType.House, $"progress {site.BuildProgress:0}");
        }
        // A free-willed Healer patches up a hurt friend.
        {
            var m = Fresh(seed);
            var hurt = Add(m, 0, "Farmer", Home(m, 0) + new Vector2(3, 0));
            Give(m, [hurt], OrderKind.Hold, hurt.Pos);
            hurt.Will = 20f;
            var h = Add(m, 0, "Healer", Home(m, 0) + new Vector2(5, 0));
            h.FreeWill = true;
            float healed = RunSum(m, 60f, e => e.Kind == EventKind.Healed && e.Unit == h.Id);
            Check("a free-willed Healer heals a hurt friend", healed > 0f, $"healed {healed:0}");
        }
    }

    static void BuildingTests(int seed)
    {
        Building Make(Match m, BuildingType t)
        {
            var k = m.Kingdoms[0];
            var p = t == BuildingType.LumenExtractor
                ? Enumerable.Range(0, 3000).Select(i => m.W.Nodes.Where(n => n.Type == ResourceType.Lumen).OrderBy(n => Vector2.Distance(n.Pos, k.Home)).First().Pos
                    + new Vector2(MathF.Cos(i * 0.3f), MathF.Sin(i * 0.3f)) * (1.5f + i * 0.001f)).FirstOrDefault(q => m.CanPlace(k, t, q) == null)
                : LegalSpot(m, k, t);
            if (t == BuildingType.LumenExtractor && p == default)
            {
                // Extractors may be built anywhere near a well regardless of distance to our buildings: put a camp there first.
                var well = m.W.Nodes.Where(n => n.Type == ResourceType.Lumen).OrderBy(n => Vector2.Distance(n.Pos, k.Home)).First();
                Check("Lumen Extractor can be placed near the nearest well", false, $"well {Vector2.Distance(well.Pos, k.Home):0} tiles from home: {m.CanPlace(k, t, well.Pos + new Vector2(2, 0))}");
            }
            var s = m.PlaceSite(k, t, p) ?? throw new Exception($"could not place {t}: {m.CanPlace(k, t, p)}");
            s.BuildProgress = Defs.Of(t).Work;
            Run(m, 1f);
            return s;
        }
        {
            var m = Fresh(seed);
            int cap = m.PopulationCap(m.Kingdoms[0]);
            m.Kingdoms[0].Age = 2;
            Make(m, BuildingType.House); Make(m, BuildingType.Manor);
            Check("Houses and Manors raise the population cap", m.PopulationCap(m.Kingdoms[0]) == cap + 5 + 12, $"{cap} -> {m.PopulationCap(m.Kingdoms[0])}");
        }
        {
            var m = Fresh(seed);
            var g = Make(m, BuildingType.Granary);
            var u = Add(m, 0, "Farmer", g.Pos + new Vector2(2, 0));
            u.CarryType = ResourceType.Food; u.CarryAmount = 5f;
            var food0 = m.Kingdoms[0].Grain;
            Give(m, [u], OrderKind.Gather, g.Pos, m.W.Nodes.IndexOf(OrderController.NearestNode(m.W, ResourceType.Food, g.Pos, 999f)!), ResourceType.Food);
            float atGranary = RunSum(m, 30f, e => e.Kind == EventKind.Deposited && e.Unit == u.Id);
            Check("grain can be dropped at a Granary", atGranary > 0 && m.Kingdoms[0].Grain > food0, $"deposited {atGranary:0}");
        }
        {
            var m = Fresh(seed);
            m.Kingdoms[0].Age = 2;
            try
            {
                Make(m, BuildingType.LumenExtractor);
                var before = m.Kingdoms[0].Lumen;
                Run(m, 30f);
                Check("a Lumen Extractor produces Lumen by itself", m.Kingdoms[0].Lumen > before + 10f, $"{before:0} -> {m.Kingdoms[0].Lumen:0}");
            }
            catch (Exception e) { Check("a Lumen Extractor produces Lumen by itself", false, e.Message); }
        }
        {
            var m = Fresh(seed);
            var s = Make(m, BuildingType.Shrine);
            var u = Add(m, 0, "Farmer", s.Pos + new Vector2(2, 0));
            var v = Add(m, 0, "Farmer", Home(m, 0) + new Vector2(-15, 0));
            u.Will = 10f; v.Will = 10f;
            Run(m, 5f);
            Check("a Shrine speeds up recovery", u.Will > v.Will + 5f, $"near {u.Will:0} vs away {v.Will:0}");
        }
        {
            var m = Fresh(seed);
            var gp = Make(m, BuildingType.GuardPost);
            var u = Add(m, 0, "Guard", gp.Pos + new Vector2(2, 0));
            Run(m, 1f);
            Check("a Guard Post makes nearby subjects tussle harder", u.AuraMul > 1.2f, $"aura x{u.AuraMul:0.00}");
        }
        {
            var m = Fresh(seed);
            var k = m.Kingdoms[0];
            int pop = m.Population(k);
            k.Stored[1] = 100; k.Stored[4] = 50;
            m.Produce(k, _base["Farmer"]);
            Run(m, 15f);
            Check("the Cradle produces a new subject", m.Population(k) == pop + 1, $"{pop} -> {m.Population(k)}");
        }
        // Placement rules.
        {
            var m = Fresh(seed);
            var k = m.Kingdoms[0];
            Vector2 OnTerrain(Terrain t) => Enumerable.Range(0, 20000).Select(i => k.Home + new Vector2(MathF.Cos(i * 0.13f), MathF.Sin(i * 0.13f)) * (3f + i * 0.001f))
                .FirstOrDefault(p => m.W.TerrainAt(p) == t);
            foreach (var t in new[] { Terrain.Road, Terrain.Farmland, Terrain.Forest, Terrain.River })
            {
                var p = OnTerrain(t);
                if (p == default) continue;
                Check($"can't build a House on {t}", m.CanPlace(k, BuildingType.House, p) != null, m.CanPlace(k, BuildingType.House, p) ?? "allowed!");
            }
            var farm = m.W.Buildings.First(b => b.Team == 0 && b.Type == BuildingType.Farm);
            Check("can't build on top of a Farm", m.CanPlace(k, BuildingType.House, farm.Pos) != null, m.CanPlace(k, BuildingType.House, farm.Pos) ?? "allowed!");
            Check("a new Farm may touch an existing one", Enumerable.Range(0, 4).Any(i => m.CanPlace(k, BuildingType.Farm, farm.Pos + new Vector2[] { new(3, 0), new(-3, 0), new(0, 3), new(0, -3) }[i]) == null));
        }
    }

    static void WildTests(int seed)
    {
        var m = MapGen.Create1v1(_f, _base, _base.Values, seed, "Blue", "Red");
        foreach (var u in m.W.Units) if (u.Team < 2 && !u.IsRuler) { u.Active = false; u.Pos = new Vector2(-50, -50); }
        int stolen = 0;
        var tribeOf = (Unit u) => m.Tribes.FirstOrDefault(t => t.Mind == u.Mind);
        float maxRoam = 0f;
        for (int i = 0; i < (int)(240f / Rules.Dt); i++)
        {
            m.Step();
            foreach (var e in m.W.Events) if (e.Kind == EventKind.Stolen && m.W.Units[e.Unit].Team == 2) stolen++;
            if (i % 50 == 0)
                foreach (var u in m.W.Units.Where(u => u.Active && u.Team == 2 && u.Command != CommandType.Rally && u.Species is Species.Sprite or Species.Troll or Species.Sage))
                {
                    var dens = m.Tribes.Where(t => t.Mind == u.Mind).Select(t => Vector2.Distance(u.Pos, t.Den.Pos)).DefaultIfEmpty(0f).Min();
                    maxRoam = MathF.Max(maxRoam, dens);
                }
        }
        foreach (var sp in new[] { Species.Sprite, Species.Troll, Species.FoxKin, Species.MothKin, Species.Sage })
            Check($"{sp} tribe is alive", m.W.Units.Any(u => u.Active && u.Team == 2 && u.Species == sp));
        var foxes = m.W.Units.Where(u => u.Active && u.Species == Species.FoxKin).ToList();
        Check("Mire Foxes raid kingdoms' stores", stolen > 0, $"{stolen} theft steps in 4 minutes; foxes: " +
              string.Join(" ", foxes.Select(f => $"{f.Order}/{(int)System.Numerics.Vector2.Distance(f.Pos, m.Kingdoms.OrderBy(k => System.Numerics.Vector2.Distance(k.Home, f.Pos)).First().Home)}t/{m.W.TerrainAt(f.Pos)}")));
        Check("home-loving tribes stay near their dens", maxRoam < 30f, $"furthest {maxRoam:0} tiles");
        // Tribes come back after losses.
        var fox = m.Tribes.First(t => t.Species == Species.FoxKin);
        foreach (var u in m.W.Units.Where(u => u.Mind == fox.Mind)) u.Active = false;
        Run(m, 30f);
        Check("tribes replenish over time", m.W.Units.Any(u => u.Active && u.Mind == fox.Mind));
        m.SealedWakes = m.Time + 1f;
        Run(m, 3f);
        Check("the Sealed One wakes on schedule", m.SealedOne is { Active: true });
    }

    // The trained brains themselves, in their drills: each specialist should beat the plain base at its job.
    static void BrainTests(int seed)
    {
        foreach (var (job, kind) in Skills.Jobs)
        {
            string cls = Classes8[Array.IndexOf(Skills.Jobs.Select(j => j.kind).ToArray(), kind)];
            double spec = VecEnv.Evaluate(_base[cls].Brain, kind, 64, 777, false, 1, true);
            double bas = VecEnv.Evaluate(_f.BaseBrain, kind, 64, 777, false, 1, true);
            Check($"{cls,-8} brain beats the base at {job}", spec > bas, $"{spec:0.00} vs base {bas:0.00}");
        }
    }

    static Mind Ready(Mind m)
    {
        m.SkillTask?.Wait();
        return m;
    }

    // Fused, mutated and infused minds: sensible classes and stats, skills kept from their parents, and they work.
    static void MixedTests(int seed)
    {
        int both = 0, oneParent = 0, neither = 0;
        float keptA = 0f, keptB = 0f;
        var hybrids = new List<(string a, string b, Mind m)>();
        for (int i = 0; i < 8; i++)
        for (int j = i + 1; j < 8; j++)
        {
            string a = Classes8[i], b = Classes8[j];
            var m = Ready(_f.Fuse(_base[a], _base[b], 0.5f));
            var c = Classes.Of(m);
            bool hasA = c.A == i || c.B == i, hasB = c.A == j || c.B == j;
            // Everyone farms, so a Farmer parent only "shows" if the child is farm-first.
            if (hasA && hasB) both++; else if (hasA || hasB) oneParent++; else neither++;
            keptA += MathF.Min(1.5f, m.Skill[i] / MathF.Max(1f, _base[a].Skill[i]));
            keptB += MathF.Min(1.5f, m.Skill[j] / MathF.Max(1f, _base[b].Skill[j]));
            hybrids.Add((a, b, m));
            Console.WriteLine($"        {a}+{b} -> {Classes.Label(m),-16} {a} {m.Skill[i],3:0}%  {b} {m.Skill[j],3:0}%");
        }
        Check("50/50 fusions take a class from their parents", neither <= 3, $"both parents {both}, one {oneParent}, neither {neither} of 28");
        Check("fusions keep a good share of each parent's skill", keptA / 28 > 0.5f && keptB / 28 > 0.4f, $"avg kept {keptA / 28 * 100:0}% / {keptB / 28 * 100:0}%");

        // Stats follow skills: a fused mind is at least decent at what its class says.
        int statOk = 0;
        var m0 = Fresh(seed);
        foreach (var (a, b, mind) in hybrids)
        {
            var u = m0.Spawn(m0.Kingdoms[0], mind, Home(m0, 0));
            var c = Classes.Of(mind);
            float Mul(int role) => role switch
            {
                0 => u.FarmMul, 1 => u.BuildMul, 2 => u.StrengthMul, 3 => u.SpeedMul + 0.08f, 4 => u.StealMul, 5 => u.HaulMul, 6 => u.HealMul, _ => u.LumenMul,
            };
            if (Mul(c.A) > 1.0f && Mul(c.B) > 0.95f) statOk++;
            u.Active = false;
        }
        Check("a fused subject's stats match its class", statOk >= 25, $"{statOk}/28");

        // Hybrids work: each takes orders and, with Free Will, picks work fitting one of its roles.
        int worked = 0, fitting = 0, checkedFw = 0;
        foreach (var (a, b, mind) in hybrids)
        {
            var m = Fresh(seed);
            var u = m.Spawn(m.Kingdoms[0], mind, Home(m, 0));
            u.FreeWill = false;
            var node = OrderController.NearestNode(m.W, ResourceType.Timber, Home(m, 0), 999f)!;
            Give(m, [u], OrderKind.Gather, node.Pos, m.W.Nodes.IndexOf(node), ResourceType.Timber);
            if (RunSum(m, 90f, e => e.Kind == EventKind.Deposited && e.Unit == u.Id) > 0) worked++;
            var c = Classes.Of(mind);
            if (c.A is 3 or 4 || c.B is 3 or 4 && c.A == c.B) continue;
            var m2 = Fresh(seed);
            var v = m2.Spawn(m2.Kingdoms[0], mind, Home(m2, 0) + new Vector2(2, 2));
            v.FreeWill = true;
            var hurt = Add(m2, 0, "Farmer", Home(m2, 0) + new Vector2(4, 0));
            hurt.Will = 30f;
            Give(m2, [hurt], OrderKind.Hold, hurt.Pos);
            var site = m2.PlaceSite(m2.Kingdoms[0], BuildingType.House, LegalSpot(m2, m2.Kingdoms[0], BuildingType.House))!;
            Run(m2, 20f);
            bool fits(int role) => role switch
            {
                0 => v.Order == OrderKind.Gather && v.OrderRes == ResourceType.Food,
                1 => v.Order == OrderKind.Build || v.Order == OrderKind.Gather && v.OrderRes is ResourceType.Timber or ResourceType.Stone,
                2 or 5 => v.Order is OrderKind.Guard or OrderKind.Capture,
                6 => v.Order == OrderKind.Heal || hurt.Will > 60f,
                7 => v.Order == OrderKind.Gather && v.OrderRes == ResourceType.Lumen,
                _ => true,
            };
            checkedFw++;
            if (fits(c.A) || fits(c.B)) fitting++;
            else Console.WriteLine($"        free-willed {Classes.Label(mind)} chose {v.Order} {v.OrderRes}");
        }
        Check("every hybrid obeys a gather order", worked == 28, $"{worked}/28");
        Check("free-willed hybrids pick work for one of their roles", fitting >= checkedFw - 1, $"{fitting}/{checkedFw}");

        // Mutants at all three Pit levels are valid, classed minds that still work.
        int mutOk = 0, mutTotal = 0;
        var rng = new Random(seed);
        foreach (var c in Classes8)
        for (int lv = 1; lv <= 3; lv++)
        {
            var mm = Ready(_f.Make($"{c}~{lv}", Mutation.Mutate(_base[c].Brain, lv, rng), [_base[c]]));
            mutTotal++;
            bool finite = mm.Brain.Weights.All(float.IsFinite) && mm.Skill.All(float.IsFinite);
            var m = Fresh(seed);
            var u = m.Spawn(m.Kingdoms[0], mm, Home(m, 0));
            u.FreeWill = false;
            var node = OrderController.NearestNode(m.W, ResourceType.Food, Home(m, 0), 999f)!;
            Give(m, [u], OrderKind.Gather, node.Pos, m.W.Nodes.IndexOf(node), ResourceType.Food);
            if (finite && RunSum(m, 60f, e => e.Kind == EventKind.Deposited && e.Unit == u.Id) > 0) mutOk++;
        }
        Check("mutants (Pit levels 1-3) are valid and work", mutOk == mutTotal, $"{mutOk}/{mutTotal}");

        // Infusion moves a skill in, removal takes it out.
        var guard = _base["Guard"];
        var infused = Ready(_f.Infuse(guard, _f.Distill(_base["Hunter"]), 1f));
        Check("infusing Hunter essence into a Guard adds hunting", infused.Skill[5] > guard.Skill[5] + 10f, $"hunting {guard.Skill[5]:0} -> {infused.Skill[5]:0}");
        var thiefGuard = Ready(_f.Infuse(guard, _f.Distill(_base["Thief"]), 1f));
        Console.WriteLine($"        (known limit) Guard + Thief essence: raiding {thiefGuard.Skill[4]:0}%, guarding {thiefGuard.Skill[2]:0}%");
        var stripped = Ready(_f.Infuse(guard, _f.Distill(guard), -1f));
        Check("removing a Guard's own essence weakens guarding", stripped.Skill[2] < guard.Skill[2] - 30f, $"guarding {guard.Skill[2]:0} -> {stripped.Skill[2]:0}");

        // Three generations deep: a hybrid fused again still works and has a class.
        var gen2 = Ready(_f.Fuse(hybrids.First(h => h.a == "Thief" && h.b == "Hunter").m, _base["Guard"], 0.5f));
        {
            var m = Fresh(seed);
            var us = Enumerable.Range(0, 2).Select(_ => m.Spawn(m.Kingdoms[0], gen2, Home(m, 0) + new Vector2(8, 8))).ToList();
            foreach (var u in us) u.FreeWill = false;
            var foe = Add(m, 1, "Farmer", Home(m, 0) + new Vector2(11, 8));
            Give(m, [foe], OrderKind.Hold, foe.Pos);
            Give(m, us, OrderKind.Capture, foe.Pos, foe.Id);
            Run(m, 120f, () => m.Kingdoms[0].Captives.Count > 0);
            Check($"a 3-parent mind ({Classes.Label(gen2)}) captures an enemy", m.Kingdoms[0].Captives.Count > 0);
        }
    }

    // An AI kingdom that has been playing for a few minutes, with the other side under test control.
    static Match AiGame(int seed, float warmup, bool smart = true)
    {
        var m = MapGen.Create1v1(_f, _base, _base.Values, seed, "Player", "AI");
        m.Ai[1] = new AiRuler(m, m.Kingdoms[1], AiPersonality.Breeder, seed + 2) { Smart = smart };
        foreach (var u in m.W.Units) if (u.Team == 0 && !u.IsRuler) { u.Active = false; u.Pos = new Vector2(-50, -50); }
        Run(m, warmup);
        return m;
    }

    // A finished square ring of wall around a spot, optionally with one gate.
    static Vector2 Ring(Match m, int team, bool withGate)
    {
        var k = m.Kingdoms[team];
        var c = m.FreeNear(k.Home + new Vector2(10, 10), 0f);
        c = new Vector2((int)c.X + 0.5f, (int)c.Y + 0.5f);
        bool first = true;
        for (int dx = -3; dx <= 3; dx++)
        for (int dy = -3; dy <= 3; dy++)
        {
            if (Math.Abs(dx) != 3 && Math.Abs(dy) != 3) continue;
            var p = c + new Vector2(dx, dy);
            if (TerrainInfo.Blocked(m.W.TerrainAt(p))) continue;
            var type = withGate && first && dy == -3 && dx == 0 ? BuildingType.Gate : BuildingType.Wall;
            if (type == BuildingType.Gate) first = false;
            // Rings may cross awkward ground in a test; place directly and finish at once.
            var b = m.W.AddBuilding(BuildingType.BuildSite, team, p);
            b.SiteFor = type;
            b.Radius = 0.5f;
            b.BuildProgress = 99f;
        }
        Run(m, 0.4f);
        return c;
    }

    // The Brawl drill teaches fight-or-flee: a drilled Farmer should do better when jumped than an undrilled one.
    static void BrawlTests(int seed)
    {
        Func<Scenario> make = () => new BrawlScenario();
        (float score, float survived) Eval(Brain b)
        {
            float total = 0f, ok = 0f;
            for (int i = 0; i < 48; i++)
            {
                var sc = make();
                sc.ContextChance = 1f;
                sc.Reset(70000 + i);
                var obs = new float[BrainSpec.In.Count];
                var rng = new Random(i);
                float r = 0f;
                for (int t = 0; t < DrillTrainer.DrillSteps && !sc.Done; t++)
                {
                    Observer.Build(sc.W, sc.Learner, obs);
                    var (mv, ac) = b.Decide(obs, rng, false);
                    r += sc.Step(mv, ac);
                }
                total += r;
                if (sc.Learner.Active && !sc.Learner.Subdued) ok++;
            }
            return (total / 48f, ok / 48f);
        }
        var start = _base["Farmer"].Brain;
        var before = Eval(start);
        var tr = new DrillTrainer(start, MindKind.Guard, make, seed);
        for (int i = 0; i < 60; i++) tr.Step();
        var after = Eval(tr.Brain);
        Check("the Brawl drill improves a Farmer's fight-or-flee", after.score > before.score, $"score {before.score:0.00} -> {after.score:0.00}, kept free {before.survived * 100:0}% -> {after.survived * 100:0}%");
        Check("the Brawl drill keeps the subject on its feet more often", after.survived > before.survived, $"{before.survived * 100:0}% -> {after.survived * 100:0}%");
    }

    static void ProgressTests(int seed)
    {
        {
            var m = Fresh(seed);
            var k = m.Kingdoms[0];
            var u = Add(m, 0, "Farmer", Home(m, 0));
            float before = u.FarmMul;
            Check("research starts", m.StartResearch(k, "ploughs") == null);
            Run(m, 50f);
            Check("Heavy Ploughs makes farmers faster", k.Has("ploughs") && u.FarmMul > before * 1.15f, $"x{before:0.00} -> x{u.FarmMul:0.00}");
            Check("only one research at a time", m.StartResearch(k, "axes") == null && m.StartResearch(k, "boots") != null);
        }
        {
            var m = Fresh(seed);
            var k = m.Kingdoms[0];
            k.Age = 2;
            var site = m.PlaceSite(k, BuildingType.House, LegalSpot(m, k, BuildingType.House))!;
            site.BuildProgress = 99f;
            Run(m, 1f);
            int cap = m.PopulationCap(k);
            Check("a House can be raised to a Manor", m.Upgrade(k, site) == null);
            Run(m, 30f);
            Check("the Manor gives its bigger population", site.Type == BuildingType.Manor && m.PopulationCap(k) == cap + 7, $"{site.Type}, cap {cap} -> {m.PopulationCap(k)}");
        }
        {
            var m = Fresh(seed);
            var k = m.Kingdoms[0];
            k.Age = 2;
            var farm = m.W.Buildings.First(b => b.Team == 0 && b.Type == BuildingType.Farm);
            float Harvest()
            {
                var u = Add(m, 0, "Farmer", farm.Pos);
                var node = m.W.Nodes.Where(n => n.Type == ResourceType.Food).OrderBy(n => Vector2.Distance(n.Pos, farm.Pos)).First();
                Give(m, [u], OrderKind.Gather, node.Pos, m.W.Nodes.IndexOf(node), ResourceType.Food);
                float g = RunSum(m, 40f, e => e.Kind == EventKind.Gathered && e.Unit == u.Id);
                u.Active = false;
                return g;
            }
            float plain = Harvest();
            m.Upgrade(k, farm);
            Run(m, 25f);
            float wet = Harvest();
            Check("an irrigated Farm yields more", farm.Level == 2 && wet > plain * 1.3f, $"{plain:0} -> {wet:0} grain in 40s");
        }
        {
            var m = MapGen.Create1v1(_f, _base, _base.Values, seed, "A", "B");
            m.Ai[0] = new AiRuler(m, m.Kingdoms[0], AiPersonality.Breeder, 1);
            m.Ai[1] = new AiRuler(m, m.Kingdoms[1], AiPersonality.Warden, 2);
            Run(m, 12 * 60f);
            foreach (var k in m.Kingdoms)
                Check($"AI {m.Ai[k.Team]!.Personality} researches and upgrades", k.Techs.Count >= 2 && m.BuildingsOf(k).Any(b => b.Level >= 2 || b.Type == BuildingType.Manor),
                      $"techs {string.Join(",", k.Techs)}; upgraded {m.BuildingsOf(k).Count(b => b.Level >= 2)} + manors {m.Count(k, BuildingType.Manor)}");
        }
    }

    static void WallTests(int seed)
    {
        {
            var m = Fresh(seed);
            var c = Ring(m, 0, false);
            var foe = Add(m, 1, "Guard", c + new Vector2(9, 0));
            Give(m, [foe], OrderKind.Move, c);
            Run(m, 12f);
            bool keptOut = Vector2.Distance(foe.Pos, c) > 2.8f;
            Run(m, 80f);
            bool broke = m.W.Buildings.Any(b => b.Type == BuildingType.Wall && b.Team == 3);
            Check("a wall ring keeps an enemy out at first", keptOut, $"{Vector2.Distance(foe.Pos, c):0.0} tiles from the centre after 12s");
            Check("a stuck enemy breaks through the wall", broke && Vector2.Distance(foe.Pos, c) < 2.5f, $"broke {broke}, now {Vector2.Distance(foe.Pos, c):0.0} away");
        }
        {
            var m = Fresh(seed);
            var c = Ring(m, 0, true);
            var friend = Add(m, 0, "Farmer", c + new Vector2(0, -8));
            var foe = Add(m, 1, "Farmer", c + new Vector2(1, -8));
            Give(m, [friend], OrderKind.Move, c);
            Give(m, [foe], OrderKind.Move, c);
            Run(m, 15f);
            Check("our own gate lets our subject in", Vector2.Distance(friend.Pos, c) < 1.5f, $"{Vector2.Distance(friend.Pos, c):0.0} away");
            Check("our gate keeps the enemy out", Vector2.Distance(foe.Pos, c) > 2.8f, $"{Vector2.Distance(foe.Pos, c):0.0} away");
        }
        {
            var m = Fresh(seed);
            var k = m.Kingdoms[0];
            var road = Enumerable.Range(0, 20000).Select(i => k.Home + new Vector2(MathF.Cos(i * 0.13f), MathF.Sin(i * 0.13f)) * (3f + i * 0.001f))
                .First(p => m.W.TerrainAt(p) == Terrain.Road);
            Check("a Wall can't block the road", m.CanPlace(k, BuildingType.Wall, road) != null);
            Check("a Gate can be built on the road", m.CanPlace(k, BuildingType.Gate, road) == null, m.CanPlace(k, BuildingType.Gate, road) ?? "");
            var spot = LegalSpot(m, k, BuildingType.Wall);
            var s1 = m.PlaceSite(k, BuildingType.Wall, spot);
            var next = new[] { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1) }.Select(o => spot + o)
                .Where(p => m.W.TerrainAt(p) == Terrain.Grass).ToList();
            Check("wall segments can stand side by side", s1 != null && next.Any(p => m.CanPlace(k, BuildingType.Wall, p) == null),
                  string.Join(", ", next.Select(p => m.CanPlace(k, BuildingType.Wall, p) ?? "ok")));
        }
    }

    static void TraceDefence(int seed)
    {
        var m = AiGame(seed, 240f);
        var k1 = m.Kingdoms[1];
        var raiders = Enumerable.Range(0, 4).Select(i => Add(m, 0, "Thief", m.FreeNear(k1.Home + new Vector2(-12, -6 + i * 3), 0f))).ToList();
        Give(m, raiders, OrderKind.Raid, k1.Home, k1.Keep.Id);
        for (int t = 0; t < 12; t++)
        {
            for (int q = 0; q < 5; q++)
            {
                Run(m, 1f);
                var r0 = raiders[0];
                var near = m.W.Units.Where(u => u.Active && u.Team == 1 && Vector2.Distance(u.Pos, r0.Pos) < 5f)
                    .Select(u => $"{Classes.Of(u.Mind).Name}:{u.Order}->{u.OrderTarget}{(u.CarryingUnit >= 0 ? "+carry" : "")}");
                if (t < 2) Console.WriteLine($"   t={m.Time:0} thief0 subdued {r0.Subdued} carried {r0.CarriedBy} near: {string.Join(" ", near)}");
            }
            var r = raiders[0];
            var hunters = m.W.Units.Where(u => u.Active && u.Team == 1 && u.Order == OrderKind.Capture).ToList();
            Console.WriteLine($"t={m.Time:0} thief0 will {r.Will:0} d-keep {Vector2.Distance(r.Pos, k1.Home):0.0} tussle {r.TussleTarget} sees {m.Vision[1].Sees(r)} | " +
                $"capture orders {hunters.Count} on thieves {hunters.Count(h => raiders.Any(x => x.Id == h.OrderTarget))}, nearest chaser {hunters.Where(h => h.OrderTarget == r.Id).Select(h => Vector2.Distance(h.Pos, r.Pos)).DefaultIfEmpty(-1).Min():0.0} " +
                $"| subjects {m.Subjects(k1).Count()} canact {m.Subjects(k1).Count(u => u.CanAct)}");
        }
    }

    static void TraceSpire(int seed)
    {
        var m = MapGen.Create1v1(_f, _base, _base.Values, seed * 100 + 0, "Smart", "Old");
        m.Ai[0] = new AiRuler(m, m.Kingdoms[0], AiPersonality.Breeder, 1) { Smart = true };
        m.Ai[1] = new AiRuler(m, m.Kingdoms[1], AiPersonality.Breeder, 2) { Smart = false };
        for (int t = 0; t < 40 && !m.Over; t++)
        {
            Run(m, 30f);
            var sp = m.BuildingsOf(m.Kingdoms[1]).FirstOrDefault(b => b.Type == BuildingType.LumenSpire || b.SiteFor == BuildingType.LumenSpire && b.Type == BuildingType.BuildSite);
            var mySp = m.BuildingsOf(m.Kingdoms[0]).FirstOrDefault(b => b.Type == BuildingType.LumenSpire);
            var k0 = m.Kingdoms[0];
            var crew = m.Subjects(k0).Where(u => u.Order is OrderKind.Sabotage).ToList();
            Console.WriteLine($"t={m.Time / 60:0.0}m old spire {(sp == null ? "-" : $"{sp.Type} charge {sp.Charge:0} disabled {sp.DisabledTimer:0} sab {sp.SabotageProgress:0.0}")} | smart spire {(mySp == null ? "-" : $"{mySp.Charge:0}")} " +
                $"| saboteurs {crew.Count} nearest {(sp == null || crew.Count == 0 ? -1 : crew.Min(c => Vector2.Distance(c.Pos, sp.Pos))):0} | smart pop {m.Population(k0)} lumen {k0.Lumen:0} age {k0.Age} | old pop {m.Population(m.Kingdoms[1])} lumen {m.Kingdoms[1].Lumen:0} age {m.Kingdoms[1].Age} | orders {string.Join(",", m.Subjects(k0).GroupBy(u => u.Order).Select(g => $"{g.Key}:{g.Count()}"))}");
        }
        Console.WriteLine($"winner {m.Winner} {m.WinReason}");
    }

    static void SmartTests(int seed)
    {
        // 1-3, each on three maps.
        int caught = 0, captives = 0, kingsSafe = 0, huntersDown = 0, rescued = 0;
        for (int r = 0; r < 3; r++)
        {
            int sd = seed + r * 17;
            {
                var m = AiGame(sd, 240f);
                var k1 = m.Kingdoms[1];
                k1.Stored[1] += 200;
                var raiders = Enumerable.Range(0, 4).Select(i => Add(m, 0, "Thief", m.FreeNear(k1.Home + new Vector2(-12, -6 + i * 3), 0f))).ToList();
                Give(m, raiders, OrderKind.Raid, k1.Home, k1.Keep.Id);
                var ids = raiders.Select(x => x.Id).ToHashSet();
                var downed = new HashSet<int>();
                var taken = new HashSet<int>();
                for (int i = 0; i < (int)(120f / Rules.Dt); i++)
                {
                    m.Step();
                    foreach (var e in m.W.Events)
                    {
                        if (e.Kind == EventKind.Subdued && ids.Contains(e.Unit)) downed.Add(e.Unit);
                        if (e.Kind == EventKind.Captured && ids.Contains(e.Other)) taken.Add(e.Other);
                    }
                }
                caught += downed.Count;
                captives += taken.Count;
            }
            {
                var m = AiGame(sd, 240f);
                var king = m.Kingdoms[1].Ruler;
                var hunters = Enumerable.Range(0, 3).Select(i => Add(m, 0, "Hunter", m.FreeNear(king.Pos + new Vector2(-14, i * 2), 0f))).ToList();
                Give(m, hunters, OrderKind.Capture, king.Pos, king.Id);
                Run(m, 150f);
                if (!m.Over && king.Active) kingsSafe++;
                huntersDown += hunters.Count(h => h.Subdued || !h.Active);
            }
            {
                var m = AiGame(sd, 240f);
                var victim = m.Subjects(m.Kingdoms[1]).OrderBy(u => Vector2.Distance(u.Pos, m.Kingdoms[1].Home)).First();
                var kid = Add(m, 0, "Guard", victim.Pos + new Vector2(1, 0));
                victim.Will = 0f; victim.SubduedTimer = 30f;
                kid.CarryingUnit = victim.Id; victim.CarriedBy = kid.Id;
                Give(m, [kid], OrderKind.Capture, victim.Pos, victim.Id);
                Run(m, 60f);
                if (m.Kingdoms[0].Captives.Count == 0) rescued++;
            }
        }
        Check("smart AI catches thieves raiding its Keep", caught >= 9, $"{caught}/12 caught over 3 maps");
        Check("smart AI drags caught raiders home as captives", captives >= 4, $"{captives} captives over 3 maps");
        Check("smart AI protects its king", kingsSafe == 3, $"{kingsSafe}/3 kings safe, {huntersDown}/9 hunters downed");
        Check("smart AI rescues a subject being carried off", rescued >= 2, $"{rescued}/3 rescued");
        // 4. The player's king steps away from danger by itself.
        {
            var m = AiGame(seed, 5f);
            var king = m.Kingdoms[0].Ruler;
            var start = king.Pos;
            var foe = Add(m, 1, "Guard", m.FreeNear(king.Pos + new Vector2(5, 0), 0f));
            Give(m, [foe], OrderKind.Move, king.Pos);
            float minD = 99f;
            for (int i = 0; i < 40; i++) { m.Step(); minD = MathF.Min(minD, Vector2.Distance(king.Pos, foe.Pos)); }
            Check("a king backs away from an approaching enemy", Vector2.Distance(king.Pos, start) > 1.5f, $"moved {Vector2.Distance(king.Pos, start):0.0} tiles");
        }
        // 5. Against a player who does nothing, the AI wins or leads by far.
        {
            var m = AiGame(seed, 15 * 60f);
            Run(m, 5 * 60f);
            float s0 = m.Score(m.Kingdoms[0]), s1 = m.Score(m.Kingdoms[1]);
            Check("the AI beats a passive player", m.Winner == 1 || s1 > 3 * s0, $"winner {m.Winner} ({m.WinReason}), score {s1:0} vs {s0:0}");
        }
        // 6. Smart rulers beat the old economy-only ruler.
        int wins = 0, games = 6;
        var lines = new List<string>();
        for (int g = 0; g < games; g++)
        {
            var m = MapGen.Create1v1(_f, _base, _base.Values, seed * 100 + g, "Smart", "Old");
            var pers = (AiPersonality)(g % 3);
            int smartTeam = g % 2;
            m.Ai[smartTeam] = new AiRuler(m, m.Kingdoms[smartTeam], pers, g + 1) { Smart = true };
            m.Ai[1 - smartTeam] = new AiRuler(m, m.Kingdoms[1 - smartTeam], pers, g + 2) { Smart = false };
            Run(m, 20 * 60f);
            float ss = m.Score(m.Kingdoms[smartTeam]), so = m.Score(m.Kingdoms[1 - smartTeam]);
            bool won = m.Winner == smartTeam || (m.Winner < 0 || m.Winner == -2) && ss > so;
            if (won) wins++;
            lines.Add($"{pers}:{(won ? "W" : "L")}({(m.Over ? m.WinReason : "time")} {ss:0}-{so:0}, captives {m.Kingdoms[smartTeam].MindsCaptured}-{m.Kingdoms[1 - smartTeam].MindsCaptured})");
        }
        Check("smart AI beats the old AI", wins >= 4, $"{wins}/{games}: {string.Join("  ", lines)}");
    }

    static void AiTests(int seed)
    {
        var m = MapGen.Create1v1(_f, _base, _base.Values, seed, "Blue", "Red");
        m.Ai[0] = new AiRuler(m, m.Kingdoms[0], AiPersonality.Warden, seed + 1);
        m.Ai[1] = new AiRuler(m, m.Kingdoms[1], AiPersonality.Breeder, seed + 2);
        var libs = m.Kingdoms.Select(k => k.Library.Count).ToArray();
        int fused = 0, drilled = 0, mutated = 0;
        Run(m, 14 * 60f);
        foreach (var a in m.Alerts)
        {
            if (a.Text.Contains(" fused ")) fused++;
        }
        foreach (var k in m.Kingdoms)
        {
            var ai = m.Ai[k.Team]!;
            string who = ai.Personality.ToString();
            var built = m.BuildingsOf(k).Where(b => b.Type != BuildingType.BuildSite).GroupBy(b => b.Type).ToDictionary(g => g.Key, g => g.Count());
            Check($"AI {who} grew past 40 subjects", m.Population(k) >= 40, $"pop {m.Population(k)}/{m.PopulationCap(k)}");
            Check($"AI {who} built extra Farms", built.GetValueOrDefault(BuildingType.Farm) >= 4, $"{built.GetValueOrDefault(BuildingType.Farm)} farms");
            Check($"AI {who} built 10+ kinds of buildings", built.Count >= 10, string.Join(" ", built.Select(kv => $"{kv.Key}x{kv.Value}")));
            Check($"AI {who} reached Age 2", k.Age >= 2, $"age {k.Age}");
            Check($"AI {who} never starves badly", k.Grain > 0 || !m.W.Starving[k.Team], $"grain {k.Grain:0}");
            Check($"AI {who} gathers all four resources", new[] { ResourceType.Food, ResourceType.Timber, ResourceType.Stone, ResourceType.Lumen }
                .All(r => m.Subjects(k).Any(u => u.Order == OrderKind.Gather && u.OrderRes == r)),
                string.Join(" ", m.Subjects(k).GroupBy(u => u.Order == OrderKind.Gather ? "G-" + u.OrderRes : u.Order.ToString()).Select(g => $"{g.Key}:{g.Count()}")));
            Check($"AI {who} keeps few subjects idle", m.Subjects(k).Count(u => u.Order == OrderKind.None) <= m.Subjects(k).Count() / 5,
                $"{m.Subjects(k).Count(u => u.Order == OrderKind.None)} idle of {m.Subjects(k).Count()}");
            Check($"AI {who} bred new minds", k.Library.Count > libs[k.Team], $"library {libs[k.Team]} -> {k.Library.Count}");
            // Every building it placed is on legal ground and doesn't overlap another.
            var bad = m.BuildingsOf(k).Where(b => b.Type is not (BuildingType.Keep or BuildingType.Cradle) && Overlap(m, b)).ToList();
            Check($"AI {who} never builds over other things", bad.Count == 0, string.Join(", ", bad.Select(b => b.Type)));
        }
        Check("the AIs fused minds during the match", fused > 0, $"{fused} fusions");
    }

    // True if a building's footprint is on a road/forest/water or overlaps another building's footprint.
    static bool Overlap(Match m, Building b)
    {
        float r = b.Type == BuildingType.Farm ? 1.5f : b.Radius;
        foreach (var o in m.W.Buildings)
        {
            if (o == b) continue;
            float ro = o.Type == BuildingType.Farm ? 1.5f : o.Radius;
            if (MathF.Abs(o.Pos.X - b.Pos.X) < r + ro - 0.1f && MathF.Abs(o.Pos.Y - b.Pos.Y) < r + ro - 0.1f
                && Vector2.Distance(o.Pos, b.Pos) < (r + ro) * 0.95f) return true;
        }
        if (b.Type == BuildingType.Farm) return false;   // its own tiles became farmland
        for (int ty = (int)MathF.Floor(b.Pos.Y - r + 0.01f); ty <= (int)MathF.Floor(b.Pos.Y + r - 0.01f); ty++)
        for (int tx = (int)MathF.Floor(b.Pos.X - r + 0.01f); tx <= (int)MathF.Floor(b.Pos.X + r - 0.01f); tx++)
            if (m.W.TerrainAt(new Vector2(tx + 0.5f, ty + 0.5f)) is Terrain.Road or Terrain.Forest or Terrain.River or Terrain.Rock or Terrain.Marsh or Terrain.Farmland)
                return true;
        return false;
    }
}
