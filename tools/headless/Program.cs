// Runs AI-vs-AI matches with no rendering, to test the match rules and the AI ruler.
// dotnet run --project tools/headless -c Release -- [seed] [minutes]
using System.Diagnostics;
using System.Runtime.InteropServices;
using FusionKingdoms.Sim;

var root = Environment.GetEnvironmentVariable("FK_ROOT") ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
int seed = args.Length > 0 ? int.Parse(args[0]) : 1;
float minutes = args.Length > 1 ? float.Parse(args[1]) : 20f;

Brain Load(string name)
{
    var p = Path.Combine(root, "brains", $"{name}.best.bin");
    return new Brain(MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(p)).ToArray());
}

AiParams.Load(Path.Combine(root, "data", "ai_params.json"));
var factory = new MindFactory(TraitTable.Parse(File.ReadAllText(Path.Combine(root, "data/traits.json"))), Load("base"));
var kinds = new[] { "Farmer", "Builder", "Guard", "Scout", "Thief", "Hunter", "Healer", "Hoarder" };
var baseMinds = kinds.ToDictionary(k => k, k => factory.Make(k, Load(k.ToLowerInvariant()), []));
factory.SetSkillReference(baseMinds);
if (args.Contains("skilltest"))
{
    string Fmt(Mind m) => string.Join("  ", m.Skill.Select((v, i) => $"{Skills.Jobs[i].name[..4]} {v,3:0}"));
    var sw2 = Stopwatch.StartNew();
    foreach (var m in baseMinds.Values) Console.WriteLine($"{m.Name,-10} {Fmt(m)}");
    foreach (var (a, b) in new[] { ("Thief", "Guard"), ("Scout", "Healer"), ("Builder", "Hunter") })
    {
        var f = factory.Fuse(baseMinds[a], baseMinds[b], 0.5f);
        Console.WriteLine($"{f.Name,-10} {Fmt(f)}   traits {string.Join(", ", f.Traits.Select(t => t.Name))}");
    }
    Console.WriteLine($"({sw2.ElapsedMilliseconds} ms incl. 3 fusions)");
    return;
}

if (args.Contains("essencetest"))
{
    double Skill(Brain b, MindKind k) => VecEnv.Evaluate(b, k, 128, 1, false, 1, true);
    var thiefE = factory.Distill(baseMinds["Thief"]);
    var guardE = factory.Distill(baseMinds["Guard"]);
    var hunterE = factory.Distill(baseMinds["Hunter"]);
    Console.WriteLine($"Essences: {thiefE.Name}, {guardE.Name}, {hunterE.Name}");
    var g = baseMinds["Guard"];
    Console.WriteLine($"Guard alone:            raid {Skill(g.Brain, MindKind.Thief),5:0.00}  guard {Skill(g.Brain, MindKind.Guard),5:0.00}  hunt {Skill(g.Brain, MindKind.Hunter),5:0.00}");
    foreach (var s0 in new[] { 0.5f, 1f, 1.5f })
    {
        var m = factory.Infuse(g, thiefE, s0);
        Console.WriteLine($"Guard + Thief x{s0}:    raid {Skill(m.Brain, MindKind.Thief),5:0.00}  guard {Skill(m.Brain, MindKind.Guard),5:0.00}  traits {string.Join(", ", m.Traits.Select(t => t.Name))}");
    }
    var h = factory.Infuse(g, hunterE, 1f);
    Console.WriteLine($"Guard + Hunter x1:      hunt {Skill(h.Brain, MindKind.Hunter),5:0.00}  guard {Skill(h.Brain, MindKind.Guard),5:0.00}  traits {string.Join(", ", h.Traits.Select(t => t.Name))}");
    var r = factory.Infuse(g, guardE, -1f);
    Console.WriteLine($"Guard - Guard x1:       guard {Skill(r.Brain, MindKind.Guard),5:0.00}  (base {Skill(factory.BaseBrain, MindKind.Guard):0.00})  traits {string.Join(", ", r.Traits.Select(t => t.Name))}");
    return;
}

if (args.Contains("compare"))
{
    // Old brain set vs the .v4 set: each specialist at its job, everyday work, and whether fusions keep both skills.
    Brain LoadSet(string name, string suffix)
    {
        var p = Path.Combine(root, "brains", $"{name}{suffix}.best.bin");
        if (!File.Exists(p)) p = Path.Combine(root, "brains", $"{name}.best.bin");   // not retrained: keep the current one
        return new(MemoryMarshal.Cast<byte, float>(File.ReadAllBytes(p)).ToArray());
    }
    double Ev(Brain b, MindKind k) => VecEnv.Evaluate(b, k, 128, 4242, false, 1, true);
    var sets = args.SkipWhile(x => x != "compare").Skip(1).ToList();
    if (sets.Count == 0) sets = ["", ".v4"];
    foreach (var suffix in sets.Select(x => x == "current" ? "" : x))
    {
        var bb = LoadSet("base", suffix);
        var spec = kinds.ToDictionary(k => k, k => LoadSet(k.ToLowerInvariant(), suffix));
        Console.WriteLine($"== set '{(suffix == "" ? "current" : suffix)}'   base everyday {Ev(bb, MindKind.Base):0.00}");
        double total = 0;
        foreach (var (job, kind) in Skills.Jobs)
        {
            string k = kind.ToString();
            double s1 = Ev(spec[k], kind), s0 = Ev(bb, kind), everyday = Ev(spec[k], MindKind.Base);
            total += s1 - s0;
            Console.WriteLine($"   {k,-8} at {job,-9} {s1,6:0.00} (base {s0,5:0.00})   everyday {everyday,5:0.00}");
        }
        foreach (var (a2, b2) in new[] { ("Thief", "Guard"), ("Builder", "Hunter"), ("Scout", "Healer") })
        {
            var fused = Brain.Fuse(bb, spec[a2], spec[b2], 0.5f);
            var ka = Enum.Parse<MindKind>(a2); var kb = Enum.Parse<MindKind>(b2);
            Console.WriteLine($"   fused {a2}+{b2}: {a2} {Ev(fused, ka):0.00}/{Ev(spec[a2], ka):0.00}  {b2} {Ev(fused, kb):0.00}/{Ev(spec[b2], kb):0.00}");
        }
        Console.WriteLine($"   total specialist edge over base: {total:0.00}");
    }
    return;
}

if (args.Contains("difficulty"))
{
    Match.NoDrills = true;
    var pairs = new (AiDifficulty a, AiDifficulty b)[] { (AiDifficulty.Normal, AiDifficulty.Easy), (AiDifficulty.Hard, AiDifficulty.Normal), (AiDifficulty.Hard, AiDifficulty.Easy) };
    int games = 16;
    var res = new bool[pairs.Length, games];
    var margins = new float[pairs.Length, games];
    var jobs = Enumerable.Range(0, pairs.Length).SelectMany(p => Enumerable.Range(0, games).Select(g => (p, g))).ToList();
    Parallel.ForEach(jobs, job =>
    {
        var (p, g) = job;
        var m = MapGen.Create1v1(factory, baseMinds, baseMinds.Values, 6000 + g, "A", "B", null, new MapSettings(Minutes: 14f));
        int me = g % 2;
        var pers = (AiPersonality)(g / 2 % 3);
        m.Ai[me] = new AiRuler(m, m.Kingdoms[me], pers, g + 1, pairs[p].a);
        m.Ai[1 - me] = new AiRuler(m, m.Kingdoms[1 - me], pers, g + 2, pairs[p].b);
        for (int i = 0; i < 14 * 60 / Rules.Dt && !m.Over; i++) m.Step();
        float a = m.Score(m.Kingdoms[me]), b = m.Score(m.Kingdoms[1 - me]);
        res[p, g] = m.Winner == me || (m.Winner < 0 || m.Winner == -2) && a > b;
        if (p == 2)
        {
            Kingdom K(int t) => m.Kingdoms[t];
            string D(int t) => $"bonus {K(t).Bonus:0.00} farmMul {m.Subjects(K(t)).Select(u => u.FarmMul).DefaultIfEmpty(0).Average():0.00} pop {m.Population(K(t))} bld {m.BuildingsOf(K(t)).Count()} lumen {K(t).LumenCollected:0} capt {K(t).MindsCaptured} lost {K(t).Lost} score {m.Score(K(t)):0}";
            Console.WriteLine($"  game {g}: HARD {D(me)}  |  EASY {D(1 - me)}");
        }
        margins[p, g] = (a - b) / MathF.Max(1f, a + b);
    });
    for (int p = 0; p < pairs.Length; p++)
        Console.WriteLine($"{pairs[p].a} vs {pairs[p].b}: {Enumerable.Range(0, games).Count(g => res[p, g])}/{games} won, margin {Enumerable.Range(0, games).Average(g => margins[p, g]) * 100:+0;-0}%");
    return;
}

if (args.Contains("losses"))
{
    Match.NoDrills = true;
    var m = MapGen.Create1v1(factory, baseMinds, baseMinds.Values, 5003, "S", "O", null, new MapSettings(Minutes: 14f));
    int me = 1;
    m.Ai[me] = new AiRuler(m, m.Kingdoms[me], AiPersonality.Warden, 4) { Smart = true };
    m.Ai[0] = new AiRuler(m, m.Kingdoms[0], AiPersonality.Warden, 5) { Smart = false };
    var orderAtGrab = new Dictionary<int, string>();
    var where = new Dictionary<string, int>();
    for (int i = 0; i < 14 * 60 / Rules.Dt && !m.Over; i++)
    {
        foreach (var u in m.W.Units) if (u.Team == me && u.Active && !u.Subdued) orderAtGrab[u.Id] = $"{u.Order}{(u.Order == OrderKind.Gather ? "-" + u.OrderRes : "")}";
        m.Step();
        foreach (var e in m.W.Events)
        {
            if (e.Kind != EventKind.Captured) continue;
            var v = m.W.Units[e.Other];
            if (v.Team != me) continue;
            float dOwn = System.Numerics.Vector2.Distance(v.Pos, m.Kingdoms[me].Home), dFoe = System.Numerics.Vector2.Distance(v.Pos, m.Kingdoms[1 - me].Home);
            string key = $"{(dOwn < 25 ? "at home" : dFoe < 25 ? "at enemy" : "midfield")} by {(m.W.Units[e.Unit].Team == 2 ? "wild" : "enemy")} while {orderAtGrab.GetValueOrDefault(v.Id, "?")}";
            where[key] = where.GetValueOrDefault(key) + 1;
        }
    }
    foreach (var kv in where.OrderByDescending(k => k.Value)) Console.WriteLine($"{kv.Value,4}  {kv.Key}");
    return;
}

if (args.Contains("scoreparts"))
{
    Match.NoDrills = true;
    var lines = new string[6];
    Parallel.For(0, 6, g =>
    {
        var m = MapGen.Create1v1(factory, baseMinds, baseMinds.Values, 5000 + g, "S", "O", null, new MapSettings(Minutes: 14f));
        int me = g % 2;
        var pers = (AiPersonality)(g / 2 % 3);
        m.Ai[me] = new AiRuler(m, m.Kingdoms[me], pers, g + 1) { Smart = true };
        m.Ai[1 - me] = new AiRuler(m, m.Kingdoms[1 - me], pers, g + 2) { Smart = false };
        for (int i = 0; i < 14 * 60 / Rules.Dt && !m.Over; i++) m.Step();
        string Parts(Kingdom k) =>
            $"bld {m.BuildingsOf(k).Count(b => b.Type != BuildingType.BuildSite && !Match.IsWall(b.Type)) * 10,4:0} lumen {k.LumenCollected,5:0} capt {k.MindsCaptured * 25,4:0} wild {k.WildCaptured * 5,3:0} traits {k.Discovered.Count * 5,3:0} pop {m.Population(k) * 2,3:0} = {m.Score(k),5:0} | lost {k.Lost} captives {k.Captives.Count} grain {k.Grain:0} age {k.Age}";
        lines[g] = $"{pers,-9} {(m.Over ? m.WinReason : "time"),-22}\n   smart: {Parts(m.Kingdoms[me])}\n   old:   {Parts(m.Kingdoms[1 - me])}";
    });
    foreach (var l in lines) Console.WriteLine(l);
    return;
}

if (args.Contains("ablate"))
{
    // Smart AI with one feature switched off at a time, each vs the old AI on the same maps.
    Match.NoDrills = true;
    var variants = new List<(string name, AiRuler.Feature f)> { ("all", AiRuler.Feature.All), ("none", AiRuler.Feature.None) };
    foreach (var f in Enum.GetValues<AiRuler.Feature>().Where(f => f is not (AiRuler.Feature.None or AiRuler.Feature.All)))
        variants.Add(($"-{f}", AiRuler.Feature.All & ~f));
    int games = 8;
    var res = new (bool won, float margin)[variants.Count, games];
    var jobs = Enumerable.Range(0, variants.Count).SelectMany(v => Enumerable.Range(0, games).Select(g => (v, g))).ToList();
    Parallel.ForEach(jobs, job =>
    {
        var (v, g) = job;
        var m = MapGen.Create1v1(factory, baseMinds, baseMinds.Values, 5000 + g, "S", "O", null, new MapSettings(Minutes: 14f));
        int me = g % 2;
        var pers = (AiPersonality)(g / 2 % 3);
        m.Ai[me] = new AiRuler(m, m.Kingdoms[me], pers, g + 1) { Smart = true, Features = variants[v].f };
        m.Ai[1 - me] = new AiRuler(m, m.Kingdoms[1 - me], pers, g + 2) { Smart = false };
        for (int i = 0; i < 14 * 60 / Rules.Dt && !m.Over; i++) m.Step();
        float a = m.Score(m.Kingdoms[me]), b = m.Score(m.Kingdoms[1 - me]);
        res[v, g] = (m.Winner == me || (m.Winner < 0 || m.Winner == -2) && a > b, (a - b) / MathF.Max(1f, a + b));
    });
    for (int v = 0; v < variants.Count; v++)
    {
        var row = Enumerable.Range(0, games).Select(g => res[v, g]).ToList();
        Console.WriteLine($"{variants[v].name,-12} won {row.Count(r => r.won)}/{games}  margin {row.Average(r => r.margin) * 100:+0;-0}%");
    }
    return;
}

if (args.Contains("tune"))
{
    // Self-play tuning of the AI ruler's strategy: each generation tries the best-so-far plus mutations, every
    // candidate plays several full games on random maps against the old ruler; the best fitness survives.
    int gens = args.Length > 2 && int.TryParse(args[3], out var gg) ? gg : 8;
    Match.NoDrills = true;
    var path = Path.Combine(root, "data", "ai_params.json");
    AiParams.Load(path);
    var best = Enum.GetValues<AiPersonality>().ToDictionary(p => p, AiParams.For);
    var rngT = new Random(seed);
    // Plays every (candidate, game) pair in one parallel batch so all cores stay busy.
    // Opponents: the best smart rulers found so far (true self-play), per personality.
    List<(float fit, string info)> PlayAll(AiPersonality pers, List<AiParams> prms, int games, int baseSeed, float minutes = 14f)
    {
        var res = new (float f, bool won, float margin)[prms.Count, games];
        var jobs = Enumerable.Range(0, prms.Count).SelectMany(c => Enumerable.Range(0, games).Select(g => (c, g))).ToList();
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, job =>
        {
            var (c, g) = job;
            var m = MapGen.Create1v1(factory, baseMinds, baseMinds.Values, baseSeed + g, "Tuned", "Old", null, new MapSettings(Minutes: minutes));
            int me = g % 2;
            var opp = (AiPersonality)((g / 2 + (int)pers) % 3);
            m.Ai[me] = new AiRuler(m, m.Kingdoms[me], pers, baseSeed + g + 1) { Smart = true, P = prms[c].Clone() };
            m.Ai[1 - me] = new AiRuler(m, m.Kingdoms[1 - me], opp, baseSeed + g + 2) { Smart = true, P = best[opp].Clone() };
            for (int i = 0; i < minutes * 60 / Rules.Dt && !m.Over; i++) m.Step();
            float a = m.Score(m.Kingdoms[me]), b = m.Score(m.Kingdoms[1 - me]);
            bool won = m.Winner == me || (m.Winner < 0 || m.Winner == -2) && a > b;
            float margin = (a - b) / MathF.Max(1f, a + b);
            res[c, g] = ((won ? 1f : 0f) + margin * 0.5f, won, margin);
        });
        return Enumerable.Range(0, prms.Count).Select(c =>
        {
            var row = Enumerable.Range(0, games).Select(g => res[c, g]).ToList();
            return (row.Average(r => r.f), $"{row.Count(r => r.won)}/{games} won, margin {row.Average(r => r.margin) * 100:+0;-0}%");
        }).ToList();
    }
    // Optional: only these personalities (e.g. "only=Warden,Archmage").
    var only = args.FirstOrDefault(x => x.StartsWith("only="))?[5..].Split(',').Select(Enum.Parse<AiPersonality>).ToHashSet();
    foreach (var pers in Enum.GetValues<AiPersonality>().Where(p => only == null || only.Contains(p)))
    {
        var incumbent = best[pers];
        var (incFit, incInfo) = PlayAll(pers, [incumbent], 6, 9000)[0];
        Console.WriteLine($"[{pers}] start: {incInfo}  fit {incFit:0.00}  {incumbent}");
        for (int gen = 0; gen < gens; gen++)
        {
            var cands = Enumerable.Range(0, 4).Select(_ => incumbent.Mutate(rngT, gen < gens / 2 ? 0.35f : 0.15f)).ToList();
            int gSeed = 10000 + gen * 100;
            // The incumbent plays the same maps in the same batch, so the comparison is fair.
            var all = PlayAll(pers, [incumbent, .. cands], 6, gSeed);
            var incNow = all[0];
            var scored = cands.Select((c, i) => (c, r: all[i + 1])).ToList();
            var top = scored.OrderByDescending(x => x.r.fit).First();
            if (top.r.fit > incNow.fit)
            {
                incumbent = top.c;
                Console.WriteLine($"[{pers}] gen {gen}: NEW best {top.r.info} fit {top.r.fit:0.00} (was {incNow.fit:0.00})  {incumbent}");
            }
            else Console.WriteLine($"[{pers}] gen {gen}: kept ({incNow.info} fit {incNow.fit:0.00}; best challenger {top.r.fit:0.00})");
        }
        var (finFit, finInfo) = PlayAll(pers, [incumbent], 8, 77000)[0];
        Console.WriteLine($"[{pers}] FINAL on fresh maps: {finInfo}  fit {finFit:0.00}");
        best[pers] = incumbent;
        AiParams.Save(path, best);
    }
    return;
}

if (args.Contains("npctest"))
{
    int ix = Array.IndexOf(args, "npctest");
    NpcTests.Run(factory, baseMinds, seed, ix + 1 < args.Length ? args[ix + 1] : null);
    return;
}

if (args.Contains("mapgen"))
{
    // Renders generated maps to PPM images (one pixel per tile, scaled x3) for inspection.
    var dir = args.Last();
    foreach (var style in new[] { MapStyle.Valleys, MapStyle.Highlands, MapStyle.Wildwood, MapStyle.Lakes, MapStyle.Plains })
    for (int sd = 0; sd < 2; sd++)
    {
        var sw5 = Stopwatch.StartNew();
        var mm = MapGen.Create1v1(factory, baseMinds, baseMinds.Values, seed * 10 + sd, settings: new MapSettings(Style: style));
        long ms = sw5.ElapsedMilliseconds;
        var w = mm.W;
        int S3 = 3;
        var img = new byte[w.Width * S3 * w.Height * S3 * 3];
        void Px(int x, int y, (int r, int g, int b) c)
        {
            for (int dy = 0; dy < S3; dy++)
            for (int dx = 0; dx < S3; dx++)
            {
                int xx = x * S3 + dx, yy = y * S3 + dy;
                if (xx < 0 || yy < 0 || xx >= w.Width * S3 || yy >= w.Height * S3) continue;
                int i = (yy * w.Width * S3 + xx) * 3;
                img[i] = (byte)c.r; img[i + 1] = (byte)c.g; img[i + 2] = (byte)c.b;
            }
        }
        for (int y = 0; y < w.Height; y++)
        for (int x = 0; x < w.Width; x++)
            Px(x, y, w.Map[y * w.Width + x] switch
            {
                Terrain.Grass => (110, 170, 80), Terrain.Road => (200, 170, 110), Terrain.Farmland => (150, 110, 60),
                Terrain.Forest => (40, 100, 50), Terrain.Hills => (150, 150, 110), Terrain.Marsh => (80, 110, 100),
                Terrain.River => (60, 110, 200), _ => (90, 90, 100),
            });
        foreach (var n in w.Nodes)
            Px((int)n.Pos.X, (int)n.Pos.Y, n.Type switch { ResourceType.Food => (250, 220, 80), ResourceType.Timber => (120, 70, 30), ResourceType.Stone => (230, 230, 240), _ => (120, 250, 255) });
        foreach (var b in w.Buildings)
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                Px((int)b.Pos.X + dx, (int)b.Pos.Y + dy, b.Team == 0 ? (60, 100, 255) : b.Team == 1 ? (255, 70, 60) : (255, 0, 255));
        // Connectivity: can Keep 0 walk to Keep 1?
        var seen = new bool[w.Width * w.Height];
        var q = new Queue<int>();
        int k0 = (int)mm.Kingdoms[0].Home.Y * w.Width + (int)mm.Kingdoms[0].Home.X;
        q.Enqueue(k0); seen[k0] = true;
        while (q.Count > 0)
        {
            int c = q.Dequeue(); int cx = c % w.Width, cy = c / w.Width;
            foreach (var (nx, ny) in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
            {
                if (nx < 0 || ny < 0 || nx >= w.Width || ny >= w.Height) continue;
                int n = ny * w.Width + nx;
                if (seen[n] || TerrainInfo.Blocked(w.Map[n])) continue;
                seen[n] = true; q.Enqueue(n);
            }
        }
        int k1 = (int)mm.Kingdoms[1].Home.Y * w.Width + (int)mm.Kingdoms[1].Home.X;
        var counts = Enum.GetValues<Terrain>().Select(t => $"{t}:{w.Map.Count(m => m == t) * 100 / w.Map.Length}%");
        Console.WriteLine($"{style} {sd}: {ms} ms, connected {seen[k1]}, dens {mm.Tribes.Count}, nodes {w.Nodes.Count}, {string.Join(" ", counts)}");
        using var fs = File.Create(Path.Combine(dir, $"map_{style}_{sd}.ppm"));
        var hdr = System.Text.Encoding.ASCII.GetBytes($"P6\n{w.Width * S3} {w.Height * S3}\n255\n");
        fs.Write(hdr); fs.Write(img);
    }
    return;
}

if (args.Contains("activity"))
{
    var ma = MapGen.Create1v1(factory, baseMinds, baseMinds.Values, seed, "Blue", "Red");
    if (!args.Contains("noai0")) ma.Ai[0] = new AiRuler(ma, ma.Kingdoms[0], AiPersonality.Warden, seed + 1);
    ma.Ai[1] = new AiRuler(ma, ma.Kingdoms[1], AiPersonality.Breeder, seed + 2);
    int perMin = (int)(60 / Rules.Dt);
    var gath = new float[2, 6];
    var acts = new int[2, 9];
    var moving = new int[2];
    int samples = 0;
    for (int i = 1; i <= minutes * perMin && !ma.Over; i++)
    {
        ma.Step();
        foreach (var e in ma.W.Events)
            if (e.Kind == EventKind.Gathered && ma.W.Units[e.Unit].Team < 2) gath[ma.W.Units[e.Unit].Team, (int)e.Res] += e.Amount;
        foreach (var u in ma.W.Units)
            if (u.Active && u.Team < 2 && !u.IsRuler && u.Species == Species.Human) { acts[u.Team, u.LastAction]++; if (u.LastMove != 0) moving[u.Team]++; }
        samples++;
        if (i % (2 * perMin) == 0)
        {
            for (int t = 0; t < 2; t++)
            {
                int tot = 0; for (int a = 0; a < 9; a++) tot += acts[t, a];
                var k = ma.Kingdoms[t];
                Console.WriteLine($"min {i / perMin,2} {k.Name,-4} pop {ma.Population(k),2} food {gath[t, 1],6:0} timber {gath[t, 2],5:0} stone {gath[t, 3],5:0} lumen {gath[t, 4],4:0} | moving {moving[t] * 100 / Math.Max(1, tot),3}% " +
                    string.Join(" ", Enumerable.Range(0, 9).Select(a => $"{(Act)a}:{acts[t, a] * 100 / Math.Max(1, tot)}")) +
                    $" | orders {string.Join(",", ma.Subjects(k).GroupBy(u => u.Order == OrderKind.Gather ? "G-" + u.OrderRes : u.Order.ToString()).Select(g => $"{g.Key}:{g.Count()}"))} | grain {k.Grain:0} timber {k.Timber:0} stone {k.Stone:0} lumen {k.Lumen:0} age {k.Age} bld {ma.BuildingsOf(k).Count(b => b.Type != BuildingType.BuildSite)}");
            }
            Array.Clear(gath); Array.Clear(acts); Array.Clear(moving);
        }
    }
    return;
}

if (args.Contains("sanity"))
{
    Func<Scenario> make = () => new NorthScenario();
    var tr = new DrillTrainer(baseMinds["Farmer"].Brain, MindKind.Base, make, 7);
    for (int i = 0; i < 30; i++) { tr.Step(); if (i % 5 == 0) Console.WriteLine($"it {i} score {tr.Curve[^1]:0.00} entropy {tr.LastEntropy:0.00}"); }
    return;
}

if (args.Contains("lrtest"))
{
    float Ev(Brain b, MindKind k) => Enumerable.Range(0, 48).Average(s => DrillTrainer.RunDrill(b, k, 9000 + s));
    foreach (var lr in new[] { 1e-3f })
    {
        var tr = new DrillTrainer(baseMinds["Farmer"].Brain, MindKind.Thief, 7) { LearningRate = lr };
        var sw4 = Stopwatch.StartNew();
        for (int i = 0; i < 600; i++) { tr.Step(); if (i % 50 == 0) Console.WriteLine($"   it {i} moved {tr.LastMove:0.00000} clipped {tr.LastClipFrac:0.00} entropy {tr.LastEntropy:0.00}"); }
        Console.WriteLine($"lr {lr}: eval {Ev(tr.Brain, MindKind.Thief):0.00} ({sw4.ElapsedMilliseconds / 60} ms/gen) curve {string.Join(" ", tr.Curve.Where((_, i) => i % 50 == 0).Select(c => c.ToString("0.00")))}");
    }
    return;
}

if (args.Contains("gradcheck"))
{
    Console.WriteLine($"worst relative error {DrillTrainer.GradCheck(baseMinds["Farmer"].Brain, 3):0.0000}");
    return;
}

if (args.Contains("estune"))
{
    float Ev0(Brain b, MindKind k) => Enumerable.Range(0, 48).Average(s => DrillTrainer.RunDrill(b, k, 9000 + s));
    foreach (var (mind, kind) in new[] { ("Farmer", MindKind.Thief), ("Farmer", MindKind.Guard), ("Scout", MindKind.Hunter) })
    {
        Console.WriteLine($"{mind}: before {Ev0(baseMinds[mind].Brain, kind):0.00}");
        foreach (var (sig, lr) in new[] { (0f, 0f) })
        {
            var tr = new DrillTrainer(baseMinds[mind].Brain, kind, 7);
            for (int i = 0; i < 40; i++) tr.Step();
            Console.WriteLine($"   {kind} drill, after 40 gens {Ev0(tr.Brain, kind):0.00}");
        }
    }
    return;
}

if (args.Contains("mirrortest"))
{
    // Play a match to collect real tapes, then train a Guard against the enemy raiders and a Thief past their guards.
    var mm = MapGen.Create1v1(factory, baseMinds, baseMinds.Values, seed, "Blue", "Red");
    mm.Ai[0] = new AiRuler(mm, mm.Kingdoms[0], AiPersonality.Warden, seed + 1);
    mm.Ai[1] = new AiRuler(mm, mm.Kingdoms[1], AiPersonality.Breeder, seed + 2);
    for (int i = 0; i < minutes * 60 / Rules.Dt && !mm.Over; i++) mm.Step();
    foreach (var k in mm.Kingdoms)
    {
        Console.WriteLine($"{k.Name}: raider tapes {k.Mirror.CountOf(TapeMode.Raider)}, guard tapes {k.Mirror.CountOf(TapeMode.Guard)}, " +
                          $"drills seen: {string.Join(", ", k.Library.SelectMany(m => m.Drills).Distinct())}");
        foreach (var t in k.Mirror.Tapes.Take(3))
            Console.WriteLine($"   {t.Mode} {t.Count} steps from ({t.Pos[0].X:0.0},{t.Pos[0].Y:0.0}) to ({t.Pos[^1].X:0.0},{t.Pos[^1].Y:0.0}), acts {string.Join("", t.Act.Select(a => a))}");
    }
    foreach (var (mode, mind) in new[] { (TapeMode.Raider, "Guard"), (TapeMode.Guard, "Thief") })
    {
        var k = mm.Kingdoms.OrderByDescending(x => x.Mirror.CountOf(mode)).First();
        var tapes = k.Mirror.Tapes.Where(t => t.Mode == mode).ToList();
        if (tapes.Count < 3) { Console.WriteLine($"{mode}: not enough tapes"); continue; }
        var tr = new DrillTrainer(baseMinds[mind].Brain, mode == TapeMode.Raider ? MindKind.Guard : MindKind.Thief, () => new MirrorScenario(mode, tapes), 7);
        float Eval(Brain b) => Enumerable.Range(0, 32).Average(s => DrillTrainer.RunDrill(b, () => new MirrorScenario(mode, tapes), 9000 + s));
        float before = Eval(tr.Brain);
        var sw3 = Stopwatch.StartNew();
        for (int i = 0; i < 80; i++) tr.Step();
        Console.WriteLine($"Mirror {mode} with {mind}: before {before:0.00}  after 80 gens {Eval(tr.Brain):0.00}  ({sw3.ElapsedMilliseconds / 80} ms/gen)  curve {string.Join(" ", tr.Curve.Where((_, i) => i % 10 == 0).Select(c => c.ToString("0.0")))}");
    }
    return;
}

if (args.Contains("trace"))
{
    var mt = MapGen.Create1v1(factory, baseMinds, baseMinds.Values, 200);
    var k = mt.Kingdoms[1];
    var target = mt.Kingdoms[0].Home;
    foreach (var other in mt.W.Units) if (other.Team != 2) other.Active = other.IsRuler;
    var navr = new Navigator(mt.W);
    var p = k.Home;
    for (int i = 0; i < 40 && System.Numerics.Vector2.Distance(p, target) > 15f; i++) p = navr.Waypoint(p, target);
    var u = mt.Spawn(k, baseMinds["Thief"], p);
    u.Command = CommandType.Raid; u.CommandTarget = target;
    var obs = new float[BrainSpec.In.Count];
    for (int i = 0; i < 300; i++)
    {
        if (i % 25 == 0)
        {
            Observer.Build(mt.W, u, obs);
            var I = typeof(BrainSpec.In);
            Console.WriteLine($"t{i,3} pos ({u.Pos.X:0.0},{u.Pos.Y:0.0}) d={System.Numerics.Vector2.Distance(u.Pos, target):0.0} terr {mt.W.TerrainAt(u.Pos)} mv {u.LastMove} act {(Act)u.LastAction} | cmd ({obs[BrainSpec.In.CommandDir]:0.00},{obs[BrainSpec.In.CommandDir+1]:0.00}) cd {obs[BrainSpec.In.CommandDist]:0.00} raid {obs[BrainSpec.In.CmdRaid]} eb ({obs[BrainSpec.In.EnemyBuilding]:0.00},{obs[BrainSpec.In.EnemyBuilding+1]:0.00}) home ({obs[BrainSpec.In.HomeDir]:0.00},{obs[BrainSpec.In.HomeDir+1]:0.00}) hd {obs[BrainSpec.In.HomeDist]:0.00} food ({obs[0]:0.00},{obs[1]:0.00}) en ({obs[BrainSpec.In.Enemy]:0.00},{obs[BrainSpec.In.Enemy+1]:0.00}) ruler ({obs[BrainSpec.In.RulerDir]:0.00},{obs[BrainSpec.In.RulerDir+1]:0.00},{obs[BrainSpec.In.RulerDist]:0.00})");
        }
        mt.W.Step();
    }
    return;
}

if (args.Contains("labraid"))
{
    foreach (var name in new[] { "Thief", "Farmer" })
    {
        float stolen = 0, minD = 0;
        for (int trial = 0; trial < 8; trial++)
        {
            var w = Maps.Lab(300 + trial);
            var k1 = w.KeepOf(1)!;
            var u = w.AddUnit(0, Species.Human, w.KeepOf(0)!.Pos + new System.Numerics.Vector2(2, 0), new BrainController(baseMinds[name].Brain));
            u.Command = CommandType.Raid; u.CommandTarget = k1.Pos;
            float best = 99f;
            for (int i = 0; i < 600; i++)
            {
                w.Step();
                best = MathF.Min(best, System.Numerics.Vector2.Distance(u.Pos, k1.Pos));
                foreach (var e in w.Events) if (e.Kind == EventKind.Stolen && e.Unit == u.Id) stolen += e.Amount;
            }
            minD += best;
        }
        Console.WriteLine($"lab {name}: closest approach {minD / 8:0.0} tiles, stole {stolen / 8:0.0}");
    }
    return;
}

if (args.Contains("raidtest"))
{
    foreach (bool nav in new[] { true, false })
    foreach (bool nearHome in new[] { false, true })
    foreach (float dist in new[] { 15f, 25f, 40f })
    {
        float best = 0f, stolen = 0f;
        for (int trial = 0; trial < 4; trial++)
        {
            var mt = MapGen.Create1v1(factory, baseMinds, baseMinds.Values, 200 + trial);
            if (!nav) mt.W.Nav = null;
            var k = mt.Kingdoms[1];
            var target = mt.Kingdoms[0].Home;
            foreach (var other in mt.W.Units) if (other.Team != 2) other.Active = other.IsRuler;
            // Start on the road toward the enemy keep at the given distance (along the navigator path).
            var start = target;
            var navr = new Navigator(mt.W);
            var p = k.Home;
            for (int i = 0; i < 40 && System.Numerics.Vector2.Distance(p, target) > dist; i++) p = navr.Waypoint(p, target);
            var u = mt.Spawn(k, baseMinds["Thief"], p);
            if (nearHome) { var camp = mt.W.AddBuilding(BuildingType.Camp, 1, mt.FreeNear(p, 3f)); u.Station = camp; }
            u.Command = CommandType.Raid; u.CommandTarget = target;
            float d0 = System.Numerics.Vector2.Distance(u.Pos, target), minD = d0;
            for (int i = 0; i < 600; i++)
            {
                mt.W.Step();
                minD = MathF.Min(minD, System.Numerics.Vector2.Distance(u.Pos, target));
                foreach (var e in mt.W.Events) if (e.Kind == EventKind.Stolen && e.Unit == u.Id) stolen += e.Amount;
            }
            best += (d0 - minD) / d0;
        }
        Console.WriteLine($"nav {nav,-5} homeNear {nearHome,-5} start {dist,3:0} tiles away: got {best / 4 * 100,3:0}% of the way, stole {stolen / 4:0.0}");
    }
    return;
}

if (args.Contains("woodtest"))
{
    foreach (var res in new[] { ResourceType.Timber, ResourceType.Stone, ResourceType.Lumen })
    foreach (var (name, mind) in baseMinds.Where(kv => kv.Key is "Builder" or "Hoarder" or "Farmer"))
    {
        float wood = 0, food = 0, stone = 0, mt0Lumen = 0;
        for (int trial = 0; trial < 4; trial++)
        {
            var mt = MapGen.Create1v1(factory, baseMinds, baseMinds.Values, 100 + trial);
            var k = mt.Kingdoms[0];
            var tn = mt.NearestNode(res, k.Home)!;
            var camp = mt.W.AddBuilding(BuildingType.Camp, 0, mt.FreeNear(tn.Pos, 2f));
            camp.Radius = 1f;
            var u = mt.Spawn(k, mind, camp.Pos);
            u.Station = camp;
            foreach (var other in mt.W.Units) if (other != u && other.Team != 2) other.Active = other.IsRuler;
            for (int i = 0; i < 600; i++)
            {
                mt.W.Step();
                foreach (var e in mt.W.Events)
                    if (e.Kind == EventKind.Deposited && e.Unit == u.Id)
                    {
                        if (e.Res == ResourceType.Timber) wood += e.Amount;
                        if (e.Res == ResourceType.Food) food += e.Amount;
                        if (e.Res == ResourceType.Stone) stone += e.Amount;
                        if (e.Res == ResourceType.Lumen) mt0Lumen += e.Amount;
                    }
            }
        }
        float lumen = mt0Lumen;
        Console.WriteLine($"{name,-8} at a {res} camp, 2 min x4: lumen {lumen,5:0} timber {wood,5:0}  food {food,5:0}  stone {stone,5:0}");
    }
    return;
}

var size = args.Contains("small") ? MapSize.Small : args.Contains("large") ? MapSize.Large : MapSize.Normal;
var match = MapGen.Create1v1(factory, baseMinds, baseMinds.Values, seed, "Blue (Warden)", "Red (Breeder)", null, new MapSettings(Size: size));
Console.WriteLine($"Map {match.W.Width}x{match.W.Height} style {match.Style}");
var personalities = Enum.GetValues<AiPersonality>();
match.Ai[0] = new AiRuler(match, match.Kingdoms[0], personalities[seed % 3], seed + 1);
match.Ai[1] = new AiRuler(match, match.Kingdoms[1], personalities[(seed + 1) % 3], seed + 2,
    args.Contains("easy") ? AiDifficulty.Easy : args.Contains("hard") ? AiDifficulty.Hard : AiDifficulty.Normal);

var sw = Stopwatch.StartNew();
var gathered = new Dictionary<(int, ResourceType), float>();
var deposited = new Dictionary<(int, ResourceType), float>();
var combat = new Dictionary<(int, EventKind), int>();
bool debug = Environment.GetEnvironmentVariable("FK_DEBUG") == "1";
int alertsShown = 0;
while (!match.Over && match.Time < minutes * 60f)
{
    match.Step();
    foreach (var e in match.W.Events)
    {
        int team = match.W.Units[e.Unit].Team;
        if (e.Kind == EventKind.Gathered) gathered[(team, e.Res)] = gathered.GetValueOrDefault((team, e.Res)) + e.Amount;
        if (e.Kind == EventKind.Deposited) deposited[(team, e.Res)] = deposited.GetValueOrDefault((team, e.Res)) + e.Amount;
        if (e.Kind is EventKind.Stolen or EventKind.TussleStarted or EventKind.PickedUp or EventKind.Captured or EventKind.Revived or EventKind.Sabotaged)
            combat[(team, e.Kind)] = combat.GetValueOrDefault((team, e.Kind)) + 1;
        if (e.Kind == EventKind.Subdued) combat[(team, e.Kind)] = combat.GetValueOrDefault((team, e.Kind)) + 1;
    }
    if (debug && match.W.StepCount == 4200)
        foreach (var u in match.W.Units.Where(u => u.Active && u.Team == 1 && u.Mind != null && u.Command == CommandType.Raid))
            Console.WriteLine($"   {u.Mind!.Name,-10} cmd {u.Command,-8} dist {System.Numerics.Vector2.Distance(u.Pos, u.CommandTarget),5:0.0} home {System.Numerics.Vector2.Distance(u.Pos, match.Kingdoms[1].Home),5:0.0} station {(u.Station == null ? "-" : $"{System.Numerics.Vector2.Distance(u.Pos, u.Station.Pos):0.0}")} nearTimber {(match.NearestNode(ResourceType.Timber, u.Pos) is { } tn ? System.Numerics.Vector2.Distance(tn.Pos, u.Pos) : -1f):0.0} carry {u.CarryAmount:0.0} {u.CarryType} act {(Act)u.LastAction} move {u.LastMove}");
    for (; alertsShown < match.Alerts.Count; alertsShown++)
        Console.WriteLine($"  [{match.Alerts[alertsShown].Time / 60f:00.0}] {match.Alerts[alertsShown].Text}");
    if (match.W.StepCount % 600 == 0)
    {
        Console.WriteLine($"== minute {match.Time / 60f:0} ({sw.Elapsed.TotalSeconds:0.0}s real, {match.W.Units.Count(u => u.Active)} units)");
        foreach (var k in match.Kingdoms)
        {
            var b = match.BuildingsOf(k).GroupBy(x => x.Type).Select(g => $"{g.Key}x{g.Count()}");
            Console.WriteLine($"   {k.Name,-14} age {k.Age} pop {match.Population(k)}/{match.PopulationCap(k)} grain {k.Grain:0} timber {k.Timber:0} stone {k.Stone:0} lumen {k.Lumen:0} " +
                              $"captives {k.Captives.Count} library {k.Library.Count} score {match.Score(k):0}  [{string.Join(" ", b)}]");
        }
    }
}
Console.WriteLine($"\nResult: {(match.Winner >= 0 ? match.Kingdoms[match.Winner].Name : match.Winner == -2 ? "draw" : "unfinished")} by {match.WinReason} at minute {match.Time / 60f:0.0}, {sw.Elapsed.TotalSeconds:0.0}s real");
foreach (var kv in combat.OrderBy(k => k.Key)) Console.WriteLine($"team {kv.Key.Item1} {kv.Key.Item2}: {kv.Value}");
foreach (var kv in gathered.OrderBy(k => k.Key)) Console.WriteLine($"gathered team {kv.Key.Item1} {kv.Key.Item2}: {kv.Value:0}   deposited {deposited.GetValueOrDefault(kv.Key):0}");
Console.WriteLine("\nRed's War Report:\n" + match.Ai[1]!.WarReport(match.Winner == 1));


sealed class NorthScenario : Scenario
{
    protected override MindKind Kind => MindKind.Base;
    protected override float Special() => Learner.LastMove == 1 ? 0.1f : 0f;
}
