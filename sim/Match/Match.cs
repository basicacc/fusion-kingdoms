using System.Numerics;

namespace FusionKingdoms.Sim;

public sealed record Alert(string Text, int Team, Vector2? At, float Time);

public sealed class Tribe
{
    public string Name = "";
    public Mind Mind = null!;
    public Species Species;
    public Building Den = null!;
    public int Target;
    public float Respawn;
    public bool Raids;
}

// A full match: the world, two kingdoms, wild tribes, and every rule above the unit level.
public sealed class Match
{
    public readonly World W;
    public readonly Kingdom[] Kingdoms;
    public readonly MindFactory Factory;
    public readonly Random Rng;
    public readonly List<Alert> Alerts = new();
    public readonly List<Tribe> Tribes = new();
    public readonly Visibility[] Vision;
    public readonly AiRuler?[] Ai = new AiRuler?[2];
    public readonly Dictionary<int, Mind[]> PitChoices = new();
    public int Winner = -1;
    public string WinReason = "";
    public bool IsMirror;
    public float Length = Defs.MatchLength;
    public MapStyle Style;
    public bool PermanentNight;
    public float SealedWakes = Defs.SealedOneWakes;
    public Mind? SealedMind;
    public Vector2 SealedAt;
    public Unit? SealedOne;
    public readonly Unit?[] Royal = new Unit?[2];
    public readonly ExternalController[] RoyalInput = [new(), new()];
    readonly IController?[] _royalSaved = new IController?[2];
    readonly float[] _royalTime = new float[2];
    float _eventTimer = 180f;
    float _tribeTimer;

    public Match(World w, Kingdom[] kingdoms, MindFactory factory, int seed)
    {
        W = w;
        Kingdoms = kingdoms;
        Factory = factory;
        Rng = new Random(seed);
        Vision = [new Visibility(w, 0, SightOf), new Visibility(w, 1, SightOf)];
        w.Nav = new Navigator(w);
    }

    public bool Over => Winner != -1;
    public float Time => W.Time;
    public Kingdom Enemy(Kingdom k) => Kingdoms[1 - k.Team];

    void Alert(string text, int team, Vector2? at = null) => Alerts.Add(new Alert(text, team, at, W.Time));

    // ---------- units ----------

    public Unit Spawn(Kingdom k, Mind m, Vector2 at)
    {
        var u = W.AddUnit(k.Team, m.Vessel, FreeNear(at, 1.5f), new OrderController(new BrainController(m.Brain)));
        u.Mind = m;
        ApplyStats(u);
        return u;
    }

    public void ApplyStats(Unit u)
    {
        Classes.ApplyStats(u);
        if (u.Team > 1) return;
        var k = Kingdoms[u.Team];
        float b = k.Bonus;
        u.FarmMul *= b * (k.Has("ploughs") ? 1.2f : 1f);
        u.MaterialMul *= b * (k.Has("axes") ? 1.2f : 1f);
        u.LumenMul *= b * (k.Has("lenses") ? 1.25f : 1f);
        u.BuildMul *= b;
        u.StrengthMul *= b * (k.Has("drill") ? 1.12f : 1f);
        u.StealMul *= b;
        if (k.Has("boots")) u.SpeedMul *= 1.08f;
    }

    // ---------- research and upgrades ----------

    public string? StartResearch(Kingdom k, string id)
    {
        var t = Progress.Tech(id);
        if (t == null) return "Unknown research";
        if (k.Has(id)) return "Already known";
        if (k.Researching != null) return "Already researching something";
        if (k.Age < t.Age) return $"Needs Age {t.Age}";
        if (!k.Spend(timber: t.Timber, stone: t.Stone, lumen: t.Lumen, gold: t.Gold)) return "Not enough resources";
        k.Researching = new Research { Id = id, Remaining = t.Time };
        return null;
    }

    public string? Upgrade(Kingdom k, Building b)
    {
        if (b.Team != k.Team || !b.Working) return "Pick one of your working buildings";
        if (b.UpgradeTimer > 0f) return "Already upgrading";
        var u = Progress.UpgradeFor(b);
        if (u == null) return "Nothing to upgrade here";
        if (k.Age < u.Age) return $"Needs Age {u.Age}";
        if (!k.Spend(timber: u.Timber, stone: u.Stone, lumen: u.Lumen)) return "Not enough resources";
        b.UpgradeTimer = u.Time;
        b.UpgradeTo = u.To;
        return null;
    }

    void StepProgress(Kingdom k)
    {
        if (k.Researching is { } r && (r.Remaining -= Rules.Dt) <= 0f)
        {
            k.Techs.Add(r.Id);
            k.Researching = null;
            Alert($"{k.Name} finished researching {Progress.Tech(r.Id)!.Name}", k.Team, k.Home);
            foreach (var u in Subjects(k)) ApplyStats(u);
        }
    }

    void StepUpgrades()
    {
        foreach (var b in W.Buildings)
        {
            if (b.UpgradeTimer <= 0f || (b.UpgradeTimer -= Rules.Dt) > 0f) continue;
            // Upgrading in place keeps the old footprint, so it never grows into its neighbours.
            if (b.UpgradeTo != b.Type) b.Type = b.UpgradeTo;
            else b.Level++;
            if (b.Type == BuildingType.Farm)
                foreach (var n in W.Nodes) if (n.Type == ResourceType.Food && Vector2.Distance(n.Pos, b.Pos) < 1.6f) n.Yield = 1.5f;
            if (b.Team < 2) Alert($"{Kingdoms[b.Team].Name} upgraded a {Defs.NameOf(b.Type)}", b.Team, b.Pos);
        }
    }

    // Swap the mind a subject runs on, keeping its orders.
    public static void SetMind(Unit u, Mind m)
    {
        u.Mind = m;
        if (u.Controller is OrderController oc) oc.Brain = new BrainController(m.Brain);
        else u.Controller = new OrderController(new BrainController(m.Brain));
        Classes.ApplyStats(u);
    }

    public Vector2 FreeNear(Vector2 at, float r)
    {
        for (int i = 0; i < 30; i++)
        {
            float a = Rng.NextSingle() * MathF.Tau, d = r + Rng.NextSingle() * 1.5f;
            var p = at + new Vector2(MathF.Cos(a), MathF.Sin(a)) * d;
            if (p.X < 1 || p.Y < 1 || p.X > W.Width - 1 || p.Y > W.Height - 1) continue;
            if (!TerrainInfo.Blocked(W.TerrainAt(p))) return p;
        }
        return Walkable(at);
    }

    public IEnumerable<Unit> Subjects(Kingdom k) => W.Units.Where(u => u.Team == k.Team && u.Active && !u.IsRuler);

    public int Population(Kingdom k) => Subjects(k).Count() + k.Training.Count + PitChoices.Keys.Count(id => W.Units[id].Team == k.Team);

    public int PopulationCap(Kingdom k) =>
        Math.Min(Defs.PopulationMax + k.PopBonus, k.PopBonus + Defs.KeepPopulation + Defs.HousePopulation * Count(k, BuildingType.House)
                                     + Defs.ManorPopulation * Count(k, BuildingType.Manor));

    public IEnumerable<Building> BuildingsOf(Kingdom k) => W.Buildings.Where(b => b.Team == k.Team && b.Type != BuildingType.Den);
    public int Count(Kingdom k, BuildingType t) => BuildingsOf(k).Count(b => b.Type == t);
    public Building? Working(Kingdom k, BuildingType t) => BuildingsOf(k).FirstOrDefault(b => b.Type == t && b.Working);

    public void Order(IEnumerable<Unit> units, CommandType cmd, Vector2 target)
    {
        foreach (var u in units)
        {
            if (u.IsRuler) continue;
            u.Command = cmd;
            u.CommandTarget = target;
            if (cmd == CommandType.None) u.Station = null;
        }
    }

    // Give an RTS order. Gather orders spread a group over the crops/trees/rocks around the clicked one;
    // Build orders send everyone to the same site.
    public string? Give(Kingdom k, IEnumerable<Unit> units, OrderKind kind, Vector2 pos, int target, ResourceType res)
    {
        var list = units.Where(u => u.Team == k.Team && u.Active && !u.IsRuler).ToList();
        if (list.Count == 0) return "Select some subjects first";
        switch (kind)
        {
            case OrderKind.Gather:
            {
                if (target < 0 || target >= W.Nodes.Count) return "Nothing to gather there";
                var node = W.Nodes[target];
                var spots = W.Nodes.Where(n => n.Type == node.Type && n.Amount > 0f && Vector2.Distance(n.Pos, node.Pos) < 7f)
                    .OrderBy(n => Vector2.Distance(n.Pos, node.Pos)).ToList();
                if (spots.Count == 0) return "That has run dry";
                var load = new int[spots.Count];
                foreach (var o in W.Units)
                    if (o.Active && o.Team == k.Team && o.Order == OrderKind.Gather && !list.Contains(o))
                        for (int i = 0; i < spots.Count; i++) if (o.OrderTarget == W.Nodes.IndexOf(spots[i])) load[i]++;
                foreach (var u in list)
                {
                    int best = 0;
                    for (int i = 1; i < spots.Count; i++) if (load[i] < load[best]) best = i;
                    load[best]++;
                    Set(u, OrderKind.Gather, node.Pos, W.Nodes.IndexOf(spots[best]), node.Type);
                }
                return null;
            }
            case OrderKind.Build:
                if (target < 0 || target >= W.Buildings.Count || W.Buildings[target].Type != BuildingType.BuildSite) return "That's not a build site";
                foreach (var u in list) Set(u, kind, W.Buildings[target].Pos, target, ResourceType.None);
                return null;
            case OrderKind.Capture:
                if (target < 0 || target >= W.Units.Count || W.Units[target].Team == k.Team) return "Pick someone to capture";
                foreach (var u in list) Set(u, kind, W.Units[target].Pos, target, ResourceType.None);
                return null;
            case OrderKind.Raid:
            case OrderKind.Sabotage:
                if (target < 0 || target >= W.Buildings.Count || W.Buildings[target].Team == k.Team) return "Pick an enemy building";
                foreach (var u in list) Set(u, kind, W.Buildings[target].Pos, target, ResourceType.None);
                return null;
            case OrderKind.Move:
            case OrderKind.Guard:
            {
                pos = Walkable(pos);
                // Fan out in a loose ring so a group doesn't pile onto one tile.
                for (int i = 0; i < list.Count; i++)
                {
                    float r = i == 0 ? 0f : 0.9f * MathF.Sqrt(i);
                    float a = i * 2.4f;
                    var spot = pos + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                    if (TerrainInfo.Blocked(W.TerrainAt(spot))) spot = pos;
                    Set(list[i], kind, spot, -1, ResourceType.None);
                }
                return null;
            }
            case OrderKind.Heal:
                if (target < 0 || target >= W.Units.Count || W.Units[target].Team != k.Team) return "Pick a friend to heal";
                foreach (var u in list.Where(u => u.Id != target)) Set(u, kind, W.Units[target].Pos, target, ResourceType.None);
                return null;
            case OrderKind.Hold:
            case OrderKind.None:
                foreach (var u in list) Set(u, kind, u.Pos, -1, ResourceType.None);
                return null;
        }
        return "Unknown order";
    }

    // The nearest tile a subject can actually stand on (clicks on water or rock land beside it).
    public Vector2 Walkable(Vector2 p)
    {
        if (!TerrainInfo.Blocked(W.TerrainAt(p))) return p;
        for (float r = 0.5f; r < 12f; r += 0.5f)
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * MathF.Tau;
                var q = p + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r;
                if (q.X > 0.5f && q.Y > 0.5f && q.X < W.Width - 0.5f && q.Y < W.Height - 0.5f && !TerrainInfo.Blocked(W.TerrainAt(q))) return q;
            }
        return p;
    }

    static void Set(Unit u, OrderKind kind, Vector2 pos, int target, ResourceType res)
    {
        u.OrderFromWill = false;
        u.Order = kind;
        u.OrderPos = pos;
        u.OrderTarget = target;
        u.OrderRes = res;
        u.Station = null;
        u.Command = CommandType.None;
        if (kind != OrderKind.None) u.FreeWill = false;
    }

    // Station subjects at a camp: it becomes their home, so they gather what's around it and drop it off there.
    public void Station(IEnumerable<Unit> units, Building? camp)
    {
        foreach (var u in units)
        {
            if (u.IsRuler) continue;
            u.Station = camp;
            if (camp != null) { u.Command = CommandType.None; }
        }
    }

    public Building? CampNear(Kingdom k, Vector2 p, float r = 5f) =>
        BuildingsOf(k).Where(b => b.Type == BuildingType.Camp && Vector2.Distance(b.Pos, p) < r)
            .OrderBy(b => Vector2.Distance(b.Pos, p)).FirstOrDefault();

    public ResourceNode? NearestNode(ResourceType type, Vector2 from) =>
        W.Nodes.Where(n => n.Type == type && n.Amount > 0f).OrderBy(n => Vector2.DistanceSquared(n.Pos, from)).FirstOrDefault();

    public void SetStance(IEnumerable<Unit> units, float stance)
    {
        foreach (var u in units) u.Stance = stance;
    }

    public void MoveRuler(Kingdom k, Vector2 to)
    {
        if (k.Ruler.Controller is MoveOrderController m) m.Target = to;
    }

    // ---------- building ----------

    public string? CanPlace(Kingdom k, BuildingType type, Vector2 pos)
    {
        var def = Defs.Of(type);
        if (k.Age < def.Age) return $"Needs Age {def.Age}";
        if (k.Timber < def.Timber || k.Stone < def.Stone || k.Lumen < def.Lumen) return $"Needs {def.CostText}";
        pos = Snap(type, pos);
        if (pos.X < 3 || pos.Y < 3 || pos.X > W.Width - 3 || pos.Y > W.Height - 3) return "Off the map";
        // Buildings need open ground: not on roads, fields, woods, water, bog or rock.
        float r = type == BuildingType.Farm ? 1.5f : def.Radius;
        for (int ty = (int)MathF.Floor(pos.Y - r + 0.01f); ty <= (int)MathF.Floor(pos.Y + r - 0.01f); ty++)
        for (int tx = (int)MathF.Floor(pos.X - r + 0.01f); tx <= (int)MathF.Floor(pos.X + r - 0.01f); tx++)
        {
            var t = W.TerrainAt(new Vector2(tx + 0.5f, ty + 0.5f));
            if (t is Terrain.Grass || (t is Terrain.Hills && type != BuildingType.Farm) || (t is Terrain.Road && type == BuildingType.Gate)) continue;
            return t switch
            {
                Terrain.Road => "Keep the road clear",
                Terrain.Farmland => "That's a field",
                Terrain.Forest => "Too wooded: clear ground only",
                Terrain.Hills => "Farms need flat ground",
                Terrain.Marsh => "Too boggy",
                _ => "Blocked ground",
            };
        }
        foreach (var b in W.Buildings)
        {
            // Fields may touch each other, walls join up; everything else keeps a walkable gap.
            float gap = b.Type == BuildingType.Farm && type == BuildingType.Farm ? -0.05f
                : (IsWall(b.Type) || IsWall(b.SiteFor) && b.Type == BuildingType.BuildSite) && IsWall(type) ? -0.05f
                : IsWall(type) || IsWall(b.Type) ? 0.3f
                : b.Type == BuildingType.Farm || type == BuildingType.Farm ? 0.5f : 0.8f;
            if (Overlaps(b.Pos, b.Type == BuildingType.Farm ? 1.5f : b.Radius, pos, r, gap)) return "Too close to another building";
        }
        foreach (var n in W.Nodes)
            if (n.Amount > 0 && Vector2.Distance(n.Pos, pos) < def.Radius + 0.6f) return "On top of a resource";
        if (type == BuildingType.LumenExtractor && !W.Nodes.Any(n => n.Type == ResourceType.Lumen && Vector2.Distance(n.Pos, pos) < def.Radius + 2.5f))
            return "Must be right beside a Lumen Well";
        if (type == BuildingType.LumenExtractor && W.Buildings.Any(b => (b.Type == BuildingType.LumenExtractor || b.SiteFor == BuildingType.LumenExtractor && b.Type == BuildingType.BuildSite)
                                                                       && Vector2.Distance(b.Pos, pos) < 5f))
            return "That well already has an Extractor";
        // Lumen Extractors are outposts: they only need a well, not a nearby town.
        if (type != BuildingType.LumenExtractor && !BuildingsOf(k).Any(b => Vector2.Distance(b.Pos, pos) < 22f)) return "Too far from your buildings";
        return null;
    }

    // Farms sit exactly on a 3x3 block of tiles; everything else snaps to half tiles.
    public static bool IsWall(BuildingType t) => t is BuildingType.Wall or BuildingType.Gate;

    public static Vector2 Snap(BuildingType type, Vector2 p) => type is BuildingType.Farm or BuildingType.Wall or BuildingType.Gate
        ? new Vector2(MathF.Floor(p.X) + 0.5f, MathF.Floor(p.Y) + 0.5f)
        : new Vector2(MathF.Round(p.X * 2f) / 2f, MathF.Round(p.Y * 2f) / 2f);

    // Square footprints for farms, round for the rest; close enough for spacing.
    static bool Overlaps(Vector2 a, float ra, Vector2 b, float rb, float gap) =>
        MathF.Abs(a.X - b.X) < ra + rb + gap && MathF.Abs(a.Y - b.Y) < ra + rb + gap
        && Vector2.Distance(a, b) < (ra + rb) * 1.42f + gap;

    public Building? PlaceSite(Kingdom k, BuildingType type, Vector2 pos)
    {
        pos = Snap(type, pos);
        if (CanPlace(k, type, pos) != null) return null;
        var def = Defs.Of(type);
        k.Spend(timber: def.Timber, stone: def.Stone, lumen: def.Lumen);
        var b = W.AddBuilding(BuildingType.BuildSite, k.Team, pos);
        b.SiteFor = type;
        b.Radius = def.Radius;
        return b;
    }

    // Rushing pays the missing work from storage at a premium: timber first, stone for the rest.
    public float RushCost(Building site) =>
        MathF.Ceiling(MathF.Max(0f, Defs.Of(site.SiteFor).Work - site.BuildProgress) * Defs.RushPremium);

    public bool Rush(Kingdom k, Building site)
    {
        if (site.Type != BuildingType.BuildSite || site.Team != k.Team) return false;
        float cost = RushCost(site);
        if (k.Timber + k.Stone < cost) return false;
        float t = MathF.Min(k.Timber, cost);
        k.Spend(timber: t, stone: cost - t);
        site.BuildProgress = Defs.Of(site.SiteFor).Work;
        return true;
    }

    public bool AgeUp(Kingdom k)
    {
        if (k.Age >= 3) return false;
        var (s, t, l, g) = Defs.AgeCost(k.Age + 1);
        if (!k.Spend(timber: t, stone: s, lumen: l, gold: g)) return false;
        k.Age++;
        Alert($"{k.Name} entered Age {k.Age}: {Defs.AgeName(k.Age)}", -1, k.Home);
        return true;
    }

    public bool FeedSpire(Kingdom k)
    {
        var spire = Working(k, BuildingType.LumenSpire);
        if (spire == null || k.Lumen <= 0f) return false;
        // Fed Lumen goes into the Spire's reservoir; the Spire charges from it slowly, and only while it works.
        spire.Stored[(int)ResourceType.Lumen] += k.Lumen;
        k.Stored[(int)ResourceType.Lumen] = 0f;
        return true;
    }

    // ---------- Cradle ----------

    public string? Produce(Kingdom k, Mind m)
    {
        if (Working(k, BuildingType.Cradle) == null) return "Build a Cradle first";
        if (!k.Library.Contains(m)) return "That mind is not in your Library";
        if (!k.Spend(grain: Defs.GrainCost(m), lumen: Defs.LumenCost(m))) return "Not enough grain or Lumen";
        k.Queue.Add(new ProductionOrder { Mind = m });
        return null;
    }

    // ---------- Fusion Altar ----------

    public bool CanUseAsDonor(Kingdom k, Unit u) => u.Team == k.Team && u.Active && !u.IsRuler && u.CanAct && u.Mind != null;

    // The Ancient Altar at a Ruin belongs to whoever has more subjects around it; it halves fusion costs.
    public int AltarHolder(Building altar)
    {
        int a = 0, b = 0;
        foreach (var u in W.Units)
        {
            if (!u.CanAct || Vector2.Distance(u.Pos, altar.Pos) > 7f) continue;
            if (u.Team == 0) a++;
            else if (u.Team == 1) b++;
        }
        return a > b ? 0 : b > a ? 1 : -1;
    }

    public bool HoldsAncientAltar(Kingdom k) =>
        W.Buildings.Any(b => b.Type == BuildingType.AncientAltar && AltarHolder(b) == k.Team);

    public int Staff(Building b)
    {
        if (!Defs.Staffing.TryGetValue(b.Type, out var s) || b.Team > 1) return 0;
        int n = 0;
        foreach (var u in W.Units)
            if (u.CanAct && u.Team == b.Team && u.Mind != null && Vector2.Distance(u.Pos, b.Pos) < b.Radius + 3f
                && u.Mind.Traits.Any(t => s.traits.Contains(t.Name))) n++;
        return n;
    }

    public float SightOf(Building b) =>
        Defs.SightOf(b.Type) + (b.Type == BuildingType.Watchtower && Staff(b) > 0 ? 6f : 0f) + (b.Type == BuildingType.Watchtower && b.Level >= 2 ? 6f : 0f);

    public float FusionCost(Kingdom k, Mind a, Mind b) => Defs.FusionCost(a, b) * (HoldsAncientAltar(k) ? 0.5f : 1f);

    public Mind? Foresee(Kingdom k, Unit a, Unit b, float ratioA)
    {
        if (Working(k, BuildingType.FusionAltar) == null || a == b || !CanUseAsDonor(k, a) || !CanUseAsDonor(k, b)) return null;
        if (!HoldsAncientAltar(k) && !k.Spend(lumen: Defs.ForeseeCost)) return null;
        return Factory.Fuse(a.Mind!, b.Mind!, ratioA);
    }

    public Unit? Fuse(Kingdom k, Unit a, Unit b, float ratioA, bool vesselFromA, Mind? foreseen = null)
    {
        var altar = Working(k, BuildingType.FusionAltar);
        if (altar == null || a == b || !CanUseAsDonor(k, a) || !CanUseAsDonor(k, b)) return null;
        if (!k.Spend(lumen: FusionCost(k, a.Mind!, b.Mind!))) return null;
        var mind = foreseen ?? Factory.Fuse(a.Mind!, b.Mind!, ratioA);
        mind.Vessel = vesselFromA ? a.Species : b.Species;
        a.Active = false;
        b.Active = false;
        Learn(k, mind);
        Alert($"{k.Name} fused {a.Mind!.Display} and {b.Mind!.Display} into a {mind.Display}", k.Team, altar.Pos);
        return Spawn(k, mind, altar.Pos);
    }

    // Add a mind to a kingdom's Library under a name no other mind there uses (II, III, ...).
    void Learn(Kingdom k, Mind m)
    {
        if (k.Library.Contains(m)) return;
        m.Name = UniqueName(k.Library, m.Name);
        k.Library.Add(m);
        Discover(k, m);
    }

    public static string UniqueName(IEnumerable<Mind> library, string name)
    {
        var taken = library.Select(x => x.Name).ToHashSet();
        if (!taken.Contains(name)) return name;
        string[] numerals = ["II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X"];
        foreach (var n in numerals) if (!taken.Contains($"{name} {n}")) return $"{name} {n}";
        int i = 11;
        while (taken.Contains($"{name} {i}")) i++;
        return $"{name} {i}";
    }

    void Discover(Kingdom k, Mind m)
    {
        foreach (var t in m.Traits)
            if (k.Discovered.Add(t.Name) && t.Tier >= Tier.Rare)
                Alert($"{k.Name} discovered the {t.Tier} trait {t.Name}!", k.Team);
    }

    // ---------- Training Grounds ----------

    // Bulk simulations (AI self-play tuning) switch drills off: they train real networks on background threads.
    public static bool NoDrills;

    public string? StartDrill(Kingdom k, Unit u, MindKind drill, string drillName)
    {
        if (NoDrills) return "Drills are off";
        if (Working(k, BuildingType.TrainingGrounds) == null) return "Build Training Grounds first";
        if (!CanUseAsDonor(k, u)) return "That subject can't drill now";
        if (k.Training.Count >= 4) return "The Grounds hold 4 subjects at most";
        if (!k.Spend(lumen: Defs.DrillCost)) return "Not enough Lumen";
        u.Active = false;
        k.Training.Add(new TrainingJob
        {
            Unit = u, Drill = drillName,
            Trainer = drillName.StartsWith("Brawl") ? new DrillTrainer(u.Mind!.Brain, MindKind.Guard, () => new BrawlScenario(), Rng.Next())
                                           : new DrillTrainer(u.Mind!.Brain, drill, Rng.Next()),
        });
        return null;
    }

    public const int MirrorMinTapes = 3;
    public static string MirrorName(TapeMode mode) => mode == TapeMode.Raider ? "Mirror: their raiders" : "Mirror: their guards";

    // Mirror Drill: train against ghosts replaying the real enemy's recorded movements.
    public string? StartMirrorDrill(Kingdom k, Unit u, TapeMode mode)
    {
        if (NoDrills) return "Drills are off";
        if (k.Age < 2) return "The Mirror Drill needs Age 2";
        var tapes = k.Mirror.Tapes.Where(t => t.Mode == mode).ToList();
        if (tapes.Count < MirrorMinTapes)
            return mode == TapeMode.Raider ? "Not enough recordings: watch enemy raiders near your Keep first"
                                           : "Not enough recordings: send someone to watch the enemy Keep first";
        if (Working(k, BuildingType.TrainingGrounds) == null) return "Build Training Grounds first";
        if (!CanUseAsDonor(k, u)) return "That subject can't drill now";
        if (k.Training.Count >= 4) return "The Grounds hold 4 subjects at most";
        if (!k.Spend(lumen: Defs.DrillCost)) return "Not enough Lumen";
        u.Active = false;
        var kind = mode == TapeMode.Raider ? MindKind.Guard : MindKind.Thief;
        k.Training.Add(new TrainingJob
        {
            Unit = u, Drill = mode == TapeMode.Raider ? "Mirror-Guard" : "Mirror-Raid",
            Trainer = new DrillTrainer(u.Mind!.Brain, kind, () => new MirrorScenario(mode, tapes), Rng.Next()),
        });
        return null;
    }

    public Mind FinishDrill(Kingdom k, TrainingJob job)
    {
        job.Stop = true;
        job.Running?.Wait();
        k.Training.Remove(job);
        var old = job.Unit.Mind!;
        var trainer = job.Trainer!;
        var mind = trainer.Iteration == 0 ? old : Factory.Make(
            $"{old.Name.Split('·')[0]}·{job.Drill}", trainer.Brain, [old], 1f, [.. old.Drills, $"{job.Drill} x{trainer.Iteration}"]);
        mind.Vessel = old.Vessel;
        if (mind != old) Learn(k, mind);
        var u = job.Unit;
        SetMind(u, mind);
        u.Active = true;
        u.Pos = FreeNear((Working(k, BuildingType.TrainingGrounds) ?? k.Keep).Pos, 1.5f);
        return mind;
    }

    void StepTraining(Kingdom k)
    {
        foreach (var job in k.Training)
        {
            if (job.Stop || job.Trainer == null) continue;
            if (job.Running == null || job.Running.IsCompleted)
            {
                var t = job.Trainer;
                job.Running = Task.Run(() => t.Step());
            }
        }
    }

    // ---------- Mutation Pit ----------

    public Mind[]? Mutate(Kingdom k, Unit u, int level)
    {
        var pit = Working(k, BuildingType.MutationPit);
        if (pit == null || !CanUseAsDonor(k, u) || k.Age < 2 || (level >= 3 && k.Age < 3)) return null;
        if (!k.Spend(lumen: Defs.MutationCost(level))) return null;
        var src = u.Mind!;
        var choices = new Mind[Mutation.Mutants];
        Parallel.For(0, choices.Length, i =>
        {
            var rng = new Random(HashCode.Combine(src.Id, k.MutationHeat, i, W.StepCount));
            choices[i] = Factory.Make($"{src.Name.Split('*')[0]}*{i + 1}", Mutation.Mutate(src.Brain, level, rng), [src], 1f,
                [.. src.Drills, $"mutated L{level}"]);
            choices[i].Vessel = src.Vessel;
        });
        u.Active = false;
        PitChoices[u.Id] = choices;
        k.MutationHeat += 30f * level;
        if (k.MutationHeat > 100f)
        {
            var ab = Factory.Make($"Aberration of {src.Name}", Mutation.Mutate(src.Brain, 3, Rng), [src]);
            ab.Vessel = Species.Troll;
            var au = W.AddUnit(2, Species.Troll, FreeNear(pit.Pos, 2f), new BrainController(ab.Brain));
            au.Mind = ab;
            k.MutationHeat = 40f;
            Alert($"The Mutation Pit boiled over! An Aberration of {src.Name} escaped.", -1, pit.Pos);
        }
        return choices;
    }

    public void ChooseMutant(Kingdom k, Unit u, Mind chosen)
    {
        if (!PitChoices.Remove(u.Id)) return;
        Learn(k, chosen);
        SetMind(u, chosen);
        u.Active = true;
        u.Pos = FreeNear((Working(k, BuildingType.MutationPit) ?? k.Keep).Pos, 1.5f);
    }

    // ---------- Market and the Wandering Merchant ----------

    public string? Trade(Kingdom k, int trade, int lots)
    {
        if (trade < 0 || trade >= Defs.Trades.Length || lots < 1) return "Unknown trade";
        if (trade < Defs.FirstBarter && Working(k, BuildingType.Market) == null) return "Build a Market first";
        var (give, giveAmt, get, getAmt) = Defs.Trades[trade];
        if (k.Stored[(int)give] < giveAmt * lots) return $"Not enough {give}";
        float bonus = get == ResourceType.Gold && Working(k, BuildingType.Market) is { } mk && Staff(mk) > 0 ? 1.5f : 1f;
        k.Stored[(int)give] -= giveAmt * lots;
        if (get == ResourceType.Gold && k.Has("crowns")) bonus *= 1.25f;
        k.Stored[(int)get] += getAmt * lots * bonus;
        return null;
    }

    public Building? Merchant;
    public Mind? MerchantMind;
    public readonly List<Vector2> MerchantSpots = new();
    float _merchantLeft;

    public bool NearMerchant(Kingdom k) =>
        Merchant != null && MerchantMind != null && W.Units.Any(u => u.CanAct && u.Team == k.Team && Vector2.Distance(u.Pos, Merchant.Pos) < 6f);

    public string? BuyFromMerchant(Kingdom k)
    {
        if (MerchantMind == null) return "The merchant has nothing to sell";
        if (!NearMerchant(k)) return "One of your subjects must stand by the merchant";
        float price = Defs.MerchantPrice * (k.Has("crowns") ? 0.75f : 1f);
        if (!k.Spend(gold: price)) return $"The merchant wants {price:0} gold";
        Learn(k, MerchantMind);
        Alert($"{k.Name} bought a {MerchantMind.Display} from the Wandering Merchant", -1, Merchant!.Pos);
        MerchantMind = null;
        return null;
    }

    // ---------- Dungeon ----------

    public TrialResult? Interrogate(Kingdom k, Captive c)
    {
        if (c.Mind == null) return null;
        c.Interrogated = true;
        k.Intel[c.Mind.Id] = c.Mind.Trial;
        return c.Mind.Trial;
    }

    // Score for a captured mind: a rival kingdom's subject is a real prize, a wild creature only a small one.
    static void Credit(Kingdom k, Captive c)
    {
        if (c.FromTeam < 2) k.MindsCaptured++;
        else k.WildCaptured++;
    }

    public string? Extract(Kingdom k, Captive c)
    {
        if (Working(k, BuildingType.Dungeon) == null) return "Extraction needs a Dungeon of Minds";
        if (c.Mind == null) return "This captive has no mind to take";
        Learn(k, c.Mind);
        k.Captives.Remove(c);
        Credit(k, c);
        Alert($"{k.Name} extracted the mind of a {c.Mind.Display}", k.Team);
        return null;
    }

    public string? Distill(Kingdom k, Captive c)
    {
        if (Working(k, BuildingType.Dungeon) == null) return "Distilling needs a Dungeon of Minds";
        if (c.Mind == null) return "Nothing to distill";
        var e = Factory.Distill(c.Mind);
        k.Essences.Add(e);
        k.Captives.Remove(c);
        Credit(k, c);
        Alert($"{k.Name} distilled {e.Name} from a {c.Mind.Display}", k.Team);
        return null;
    }

    // Essence infusion (Age II) or removal (Age III) at the Fusion Altar. preview = Foresee for essences.
    public string? InfuseCheck(Kingdom k, Unit u, float strength)
    {
        if (Working(k, BuildingType.FusionAltar) == null) return "Needs a working Fusion Altar";
        if (k.Age < 2) return "Essence Infusion needs Age 2";
        if (strength < 0f && k.Age < 3) return "Essence Removal needs Age 3";
        if (!CanUseAsDonor(k, u)) return "That subject can't come to the Altar now";
        return null;
    }

    public Mind? ForeseeInfusion(Kingdom k, Unit u, Essence e, float strength)
    {
        if (InfuseCheck(k, u, strength) != null || !k.Spend(lumen: Defs.ForeseeCost)) return null;
        return Factory.Infuse(u.Mind!, e, strength);
    }

    public Mind? Infuse(Kingdom k, Unit u, Essence e, float strength, Mind? foreseen = null)
    {
        if (InfuseCheck(k, u, strength) != null || !k.Spend(lumen: Defs.InfusionCost)) return null;
        var mind = foreseen ?? Factory.Infuse(u.Mind!, e, strength);
        Learn(k, mind);
        SetMind(u, mind);
        Alert($"{k.Name} {(strength >= 0 ? "infused" : "stripped")} {e.Name} {(strength >= 0 ? "into" : "from")} a subject: now a {mind.Display}", k.Team);
        return mind;
    }

    public string? StartConvert(Kingdom k, Captive c)
    {
        if (Working(k, BuildingType.Dungeon) == null) return "Conversion needs a Dungeon of Minds";
        if (c.Mind == null) return "Nothing to convert";
        c.Convert = 0f;
        return null;
    }

    public void Ransom(Kingdom k, Captive c)
    {
        k.Captives.Remove(c);
        k.Stored[(int)ResourceType.Gold] += 20f;
        var home = c.FromTeam < 2 ? Kingdoms[c.FromTeam].Home : c.Unit.Pos;
        Release(c, home);
        Alert($"{k.Name} ransomed back {c.Mind?.Display ?? "a captive"}", -1);
    }

    void Release(Captive c, Vector2 at)
    {
        var u = c.Unit;
        u.Active = true;
        u.Team = c.FromTeam;
        u.Will = u.Body.Will * Rules.ReviveWillFrac;
        u.SubduedTimer = 0f;
        u.CarriedBy = -1;
        u.Pos = FreeNear(at, 2f);
        u.Order = OrderKind.None;
        u.OrderTarget = -1;
        u.FreeWill = true;
        if (u.Team < 2 && u.Mind != null && u.Controller is not OrderController) SetMind(u, u.Mind);
    }

    void StepCaptives(Kingdom k)
    {
        var dungeon = Working(k, BuildingType.Dungeon);
        var hold = dungeon ?? k.Keep;
        for (int i = k.Captives.Count - 1; i >= 0; i--)
        {
            var c = k.Captives[i];
            c.Held += Rules.Dt;
            if (c.Convert >= 0f && dungeon != null)
            {
                c.Convert += Rules.Dt;
                // A converted captive needs a free place in the kingdom; otherwise it waits in its cell.
                if (c.Convert >= Defs.ConvertTime && Population(k) < PopulationCap(k))
                {
                    k.Captives.RemoveAt(i);
                    c.FromTeam = k.Team;
                    Release(c, hold.Pos);
                    c.Unit.Command = CommandType.None;
                    if (c.Mind != null) Learn(k, c.Mind);
                    Credit(k, c);
                    Alert($"{c.Mind?.Display ?? "A captive"} now serves {k.Name}", k.Team, hold.Pos);
                    continue;
                }
            }
            if (dungeon == null && c.Held > Defs.CaptiveEscapeTime)
            {
                k.Captives.RemoveAt(i);
                Release(c, hold.Pos);
                Alert($"{c.Mind?.Display ?? "A captive"} escaped from {k.Name}'s Keep", -1, hold.Pos);
            }
        }
        // Dungeon break: an enemy rescuing at the walls frees one of its own, unless good jailers stand guard.
        if (dungeon != null && Staff(dungeon) > 0) return;
        foreach (var u in W.Units)
        {
            if (!u.CanAct || u.Team == k.Team || u.LastAction != (int)Act.Rescue) continue;
            if (Vector2.Distance(u.Pos, hold.Pos) > hold.Radius + Rules.InteractRange) continue;
            var freed = k.Captives.FirstOrDefault(c => c.FromTeam == u.Team);
            if (freed == null) continue;
            k.Captives.Remove(freed);
            Release(freed, hold.Pos);
            Alert($"A captive was broken out of {k.Name}'s {(dungeon != null ? "Dungeon" : "Keep")}!", -1, hold.Pos);
        }
    }

    // ---------- Royal Command ----------

    public bool BeginRoyal(Kingdom k, Unit u)
    {
        int t = k.Team;
        if (Royal[t] != null || !CanUseAsDonor(k, u) || !k.Spend(lumen: Defs.RoyalCommandCost)) return false;
        Royal[t] = u;
        _royalSaved[t] = u.Controller;
        _royalTime[t] = Defs.RoyalCommandTime;
        RoyalInput[t].Move = 0;
        RoyalInput[t].Action = 0;
        u.Controller = RoyalInput[t];
        return true;
    }

    public float RoyalTimeLeft(int team) => Royal[team] == null ? 0f : _royalTime[team];

    // Mirrors (LAN clients) only know who is under Royal Command and for how long.
    public void SetRoyal(int team, Unit? u, float time)
    {
        Royal[team] = u;
        _royalTime[team] = time;
    }

    void StepRoyal()
    {
        for (int t = 0; t < 2; t++)
        {
            var u = Royal[t];
            if (u == null) continue;
            _royalTime[t] -= Rules.Dt;
            if (_royalTime[t] > 0f && u.Active) continue;
            u.Controller = _royalSaved[t];
            Royal[t] = null;
        }
    }

    // A finished Farm turns its 3x3 patch into farmland with three crops on it.
    public void Plough(Building farm)
    {
        PloughTiles(W, farm.Pos);
        int cx = (int)farm.Pos.X, cy = (int)farm.Pos.Y;
        foreach (var dx in new[] { -1, 0, 1 }) W.AddNode(ResourceType.Food, new Vector2(cx + dx + 0.5f, cy + 0.5f));
        W.Nav?.Invalidate();
        FarmsChanged = true;
    }

    public bool FarmsChanged;

    void SetWall(Building b, bool on)
    {
        if (W.Walls.Length == 0) W.Walls = new byte[W.Width * W.Height];
        int i = (int)b.Pos.Y * W.Width + (int)b.Pos.X;
        W.Walls[i] = on ? (byte)((b.Team + 1) | (b.Type == BuildingType.Gate ? 0x80 : 0)) : (byte)0;
        W.Nav?.Invalidate();
    }

    // Broken walls and gates vanish (moved off the map and given to nobody, so ids stay stable).
    void Demolish(Building b)
    {
        SetWall(b, false);
        Alert($"A {Defs.NameOf(b.Type)} was broken down!", b.Team, b.Pos);
        b.Team = 3;
        b.Pos = new Vector2(-100f, -100f);
    }

    public static void PloughTiles(World w, Vector2 at)
    {
        int cx = (int)at.X, cy = (int)at.Y;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int x = cx + dx, y = cy + dy;
            if (x < 0 || y < 0 || x >= w.Width || y >= w.Height || TerrainInfo.Blocked(w.Map[y * w.Width + x])) continue;
            w.SetTerrain(x, y, Terrain.Farmland);
        }
    }

    // Buildings that work on their own: extractors draw Lumen, shrines and guard posts help nearby subjects.
    void StepBuildings()
    {
        foreach (var u in W.Units) u.AuraMul = 1f;
        foreach (var b in W.Buildings)
        {
            if (b.Team > 1 || !b.Working) continue;
            switch (b.Type)
            {
                case BuildingType.LumenSpire:
                {
                    float take = MathF.Min(Defs.SpireRate * Rules.Dt, b.Stored[(int)ResourceType.Lumen]);
                    b.Stored[(int)ResourceType.Lumen] -= take;
                    float before = b.Charge;
                    b.Charge += take;
                    foreach (var mark in new[] { 0.5f, 0.75f, 0.9f })
                        if (before < Defs.SpireGoal * mark && b.Charge >= Defs.SpireGoal * mark)
                            Alert($"{Kingdoms[b.Team].Name}'s Lumen Spire is {mark * 100:0}% charged! Sabotage it to drain it.", -1, b.Pos);
                    if (b.Charge >= Defs.SpireGoal) End(b.Team, "Lumen Ascension");
                    break;
                }
                case BuildingType.LumenExtractor:
                    Kingdoms[b.Team].Keep.Stored[(int)ResourceType.Lumen] += 0.6f * Rules.Dt;
                    Kingdoms[b.Team].LumenCollected += 0.6f * Rules.Dt;
                    break;
                case BuildingType.GuardPost:
                    float aura = b.Level >= 2 || Kingdoms[b.Team].Has("drill") ? 1.5f : 1.3f;
                    foreach (var u in W.Units)
                        if (u.Active && u.Team == b.Team && Vector2.Distance(u.Pos, b.Pos) <= 7f) u.AuraMul = MathF.Max(u.AuraMul, aura);
                    break;
                case BuildingType.Shrine:
                    foreach (var u in W.Units)
                    {
                        if (!u.Active || u.Team != b.Team || Vector2.Distance(u.Pos, b.Pos) > 7f) continue;
                        if (u.Subdued && u.CarriedBy < 0) u.SubduedTimer -= Rules.Dt;
                        else if (u.TussleTarget < 0) u.Will = MathF.Min(u.Body.Will, u.Will + 2f * Rules.RegenRate * Rules.Dt);
                    }
                    break;
            }
        }
    }

    // ---------- the tick ----------

    public void Step()
    {
        if (Over) return;
        W.Step();
        foreach (var e in W.Events) Handle(e);

        foreach (var b in W.Buildings)
        {
            if (b.Type != BuildingType.BuildSite || b.BuildProgress < Defs.Of(b.SiteFor).Work * (b.Team < 2 && Kingdoms[b.Team].Has("masonry") ? 0.75f : 1f)) continue;
            b.Type = b.SiteFor;
            if (b.Type == BuildingType.Farm) Plough(b);
            if (IsWall(b.Type)) SetWall(b, true);
            if (b.Team < 2) Alert($"{Kingdoms[b.Team].Name} finished a {Defs.NameOf(b.Type)}", b.Team, b.Pos);
        }

        foreach (var b in W.Buildings)
        {
            if (b.Type is not (BuildingType.Camp or BuildingType.Granary or BuildingType.LumberMill) || b.Team > 1) continue;
            var keep = Kingdoms[b.Team].Keep;
            for (int r = 1; r < b.Stored.Length; r++) { keep.Stored[r] += b.Stored[r]; b.Stored[r] = 0f; }
        }

        StepUpgrades();
        foreach (var k in Kingdoms)
        {
            StepProgress(k);
            StepProduction(k);
            StepUpkeep(k);
            StepCaptives(k);
            StepTraining(k);
            k.MutationHeat = MathF.Max(0f, k.MutationHeat - 1.5f * Rules.Dt);
        }
        foreach (var inf in W.Buildings)
        {
            if (inf.Type != BuildingType.Infirmary || !inf.Working || inf.Team > 1) continue;
            foreach (var u in W.Units)
                if (u.Active && u.Team == inf.Team && u.Subdued && u.CarriedBy < 0 && Vector2.Distance(u.Pos, inf.Pos) < 8f)
                    u.SubduedTimer -= (Staff(inf) > 0 ? 4f : 2f) * Rules.Dt;
        }
        StepBuildings();
        // Skills are measured in the background; refresh working stats once they arrive.
        if (W.StepCount % 10 == 0)
            foreach (var u in W.Units)
                if (u.Active && u.Team < 2 && u.Mind is { } um && u.StatsFor != um && um.Skill.Length == Classes.Roles.Length)
                {
                    ApplyStats(u);
                    u.StatsFor = um;
                }
        StepRoyal();
        StepTribes();
        StepEvents();
        if (W.StepCount % 3 == 0) foreach (var v in Vision) v.Update();
        foreach (var k in Kingdoms)
        {
            k.Mirror.Record(this, k);
            k.MirrorRaiders = k.Mirror.CountOf(TapeMode.Raider);
            k.MirrorGuards = k.Mirror.CountOf(TapeMode.Guard);
        }
        if (W.StepCount % 5 == 0) foreach (var ai in Ai) ai?.Think();

        if (PermanentNight) W.EclipseTimer = MathF.Max(W.EclipseTimer, 1f);
        if (W.Time >= Length)
        {
            float s0 = Score(Kingdoms[0]), s1 = Score(Kingdoms[1]);
            End(s0 == s1 ? -2 : s0 > s1 ? 0 : 1, "Score at the time limit");
        }
    }

    void Handle(SimEvent e)
    {
        switch (e.Kind)
        {
            case EventKind.Captured:
            {
                var carrier = W.Units[e.Unit];
                var victim = W.Units[e.Other];
                if (victim.IsRuler && carrier.Team < 2 && victim.Team < 2)
                {
                    End(carrier.Team, "Crown Capture");
                    return;
                }
                if (carrier.Team < 2)
                {
                    var k = Kingdoms[carrier.Team];
                    k.Captives.Add(new Captive { Unit = victim, Mind = victim.Mind, FromTeam = victim.Team });
                    if (victim.Team < 2) Alert($"{k.Name} captured {victim.Mind?.Display ?? victim.Species.ToString()}", -1, carrier.Pos);
                }
                if (victim.Team < 2)
                {
                    Kingdoms[victim.Team].Lost++;
                    if (carrier.Team == 2) Alert($"Wild creatures dragged off {victim.Mind?.Display ?? "a subject"}", victim.Team, carrier.Pos);
                }
                break;
            }
            case EventKind.Deposited when e.Res == ResourceType.Lumen:
            {
                int team = W.Units[e.Unit].Team;
                if (team < 2) Kingdoms[team].LumenCollected += e.Amount;
                break;
            }
            case EventKind.Sabotaged:
            {
                var b = W.Buildings[e.Other];
                if (IsWall(b.Type)) { Demolish(b); break; }
                // A sabotaged Spire bleeds: it loses part of its charge and spills its reservoir.
                if (b.Type == BuildingType.LumenSpire)
                {
                    b.Charge *= 0.85f;
                    b.Stored[(int)ResourceType.Lumen] *= 0.5f;
                }
                if (b.Team < 2) Alert($"{Defs.NameOf(b.Type)} sabotaged!", b.Team, b.Pos);
                break;
            }
            case EventKind.Subdued:
            {
                var u = W.Units[e.Unit];
                if (u.IsRuler) Alert("Your ruler has been subdued! Rescue them!", u.Team, u.Pos);
                break;
            }
            case EventKind.Stolen:
            {
                var b = W.Buildings[e.Other];
                if (b.Team < 2 && W.Units[e.Unit].Team < 2) Kingdoms[b.Team].TheftsSuffered++;
                if (b.Team < 2 && W.StepCount % 25 == 0) Alert("Thieves are raiding your Keep!", b.Team, b.Pos);
                break;
            }
        }
    }

    void StepProduction(Kingdom k)
    {
        if (k.Queue.Count == 0 && k.Repeat != null && k.Library.Contains(k.Repeat) && Population(k) < PopulationCap(k)
            && k.Grain >= Defs.GrainCost(k.Repeat) + 30f && k.Lumen >= Defs.LumenCost(k.Repeat))
            Produce(k, k.Repeat);
        if (k.Queue.Count == 0) return;
        var cradle = Working(k, BuildingType.Cradle);
        if (cradle == null || W.Starving[k.Team] || Population(k) >= PopulationCap(k)) return;
        var order = k.Queue[0];
        order.Remaining -= Rules.Dt;
        if (order.Remaining > 0f) return;
        k.Queue.RemoveAt(0);
        var u = Spawn(k, order.Mind, cradle.Pos);
        Newborn(k, u);
    }

    // Send a fresh subject where the player asked: the rally point, and/or straight to a job.
    void Newborn(Kingdom k, Unit u)
    {
        if (k.Job == NewbornJob.Guard)
        {
            Give(k, [u], OrderKind.Guard, k.Rally ?? k.Home + new Vector2(3f, 3f), -1, ResourceType.None);
            return;
        }
        var res = k.Job switch
        {
            NewbornJob.Food => ResourceType.Food, NewbornJob.Timber => ResourceType.Timber,
            NewbornJob.Stone => ResourceType.Stone, NewbornJob.Lumen => ResourceType.Lumen, _ => ResourceType.None,
        };
        if (res != ResourceType.None)
        {
            var node = OrderController.NearestNode(W, res, k.Rally ?? k.Home, 999f);
            if (node != null) { Give(k, [u], OrderKind.Gather, node.Pos, W.Nodes.IndexOf(node), res); return; }
        }
        if (k.Rally is { } r)
        {
            Give(k, [u], OrderKind.Move, r, -1, ResourceType.None);
            u.OrderFromWill = true;   // arrive, then use its own judgement
            u.FreeWill = true;
        }
    }

    public bool CancelProduce(Kingdom k, int index)
    {
        if (index < 0 || index >= k.Queue.Count) return false;
        var o = k.Queue[index];
        k.Queue.RemoveAt(index);
        k.Stored[(int)ResourceType.Food] += Defs.GrainCost(o.Mind);
        k.Stored[(int)ResourceType.Lumen] += Defs.LumenCost(o.Mind);
        return true;
    }

    void StepUpkeep(Kingdom k)
    {
        k.UpkeepTimer -= Rules.Dt;
        if (k.UpkeepTimer > 0f) return;
        k.UpkeepTimer = Defs.UpkeepInterval;
        var granary = BuildingsOf(k).Where(b => b.Type == BuildingType.Granary && b.Working).OrderByDescending(b => b.Level).FirstOrDefault();
        float need = Population(k) * (granary == null ? 1f : granary.Level >= 2 ? 0.65f : 0.8f) * (k.Has("cellars") ? 0.85f : 1f);
        bool was = W.Starving[k.Team];
        if (k.Grain >= need) { k.Spend(grain: need); W.Starving[k.Team] = false; }
        else
        {
            k.Stored[(int)ResourceType.Food] = 0f;
            W.Starving[k.Team] = true;
            if (!was) Alert("Your granary is empty! Subjects stop recovering and the Cradle halts.", k.Team, k.Home);
        }
    }

    void StepTribes()
    {
        _tribeTimer -= Rules.Dt;
        if (_tribeTimer <= 0f)
        {
            _tribeTimer = 20f;
            foreach (var t in Tribes)
            {
                int alive = W.Units.Count(u => u.Active && u.Team == 2 && u.Mind == t.Mind);
                if (alive >= t.Target) continue;
                var u = W.AddUnit(2, t.Species, FreeNear(t.Den.Pos, 2f), WildController(t));
                u.Mind = t.Mind;
                if (t.Raids) AimRaid(u);
            }
        }
        if (SealedOne == null && SealedMind != null && W.Time >= SealedWakes)
        {
            SealedOne = W.AddUnit(2, Species.Troll, SealedAt, new BrainController(SealedMind.Brain));
            SealedOne.Mind = SealedMind;
            SealedOne.Will = SealedOne.Body.Will * 1.5f;
            Alert("The Sealed One has awoken in the Ruins! Capture it to claim a Legendary mind.", -1, SealedAt);
        }
    }

    // Raiding tribes get real raid orders (to the nearest Keep), the rest are leashed to their den.
    public IController WildController(Tribe t) =>
        t.Raids ? new OrderController(new BrainController(t.Mind.Brain)) : new LeashController(new BrainController(t.Mind.Brain), t.Den.Pos, 10f);

    public void AimRaid(Unit u)
    {
        var target = Kingdoms.OrderBy(k => Vector2.Distance(k.Home, u.Pos)).First();
        u.Command = CommandType.Raid;
        u.CommandTarget = target.Home;
        if (u.Controller is OrderController)
        {
            u.Order = OrderKind.Raid;
            u.OrderTarget = target.Keep.Id;
            u.OrderPos = target.Home;
        }
    }

    void StepEvents()
    {
        if (MerchantMind != null && (_merchantLeft -= Rules.Dt) <= 0f)
        {
            MerchantMind = null;
            Alert("The Wandering Merchant moved on.", -1, Merchant?.Pos);
        }
        _eventTimer -= Rules.Dt;
        if (_eventTimer > 0f) return;
        _eventTimer = 100f + Rng.NextSingle() * 60f;
        int roll = Rng.Next(4);
        if (roll == 2 && Merchant != null)
        {
            var stock = Tribes.Select(t => t.Mind).Concat(Kingdoms.SelectMany(k => k.Library.Where(m => m.Parents.Length == 0))).ToList();
            var src = stock[Rng.Next(stock.Count)];
            MerchantMind = Factory.Make($"{src.Name.Split(' ')[^1]} of the Road", Mutation.Mutate(src.Brain, 2, Rng), [src], 1f, ["sold by a merchant"]);
            MerchantMind.Vessel = src.Vessel;
            if (MerchantSpots.Count > 0) Merchant.Pos = MerchantSpots[Rng.Next(MerchantSpots.Count)];
            _merchantLeft = 90f;
            Alert($"A Wandering Merchant is at the Ruins selling a {MerchantMind.Display} for {Defs.MerchantPrice:0} gold.", -1, Merchant.Pos);
            return;
        }
        if (roll == 3 && Tribes.FirstOrDefault(t => t.Species == Species.Sprite) is { } sprites)
        {
            bool west = Rng.Next(2) == 0;
            var from = new Vector2(west ? 2f : W.Width - 2f, 8f + Rng.NextSingle() * (W.Height - 16f));
            var to = new Vector2(west ? W.Width - 3f : 3f, W.Height - from.Y);
            for (int i = 0; i < 4; i++)
            {
                var u = W.AddUnit(2, Species.Sprite, FreeNear(from, 1.5f), new BrainController(sprites.Mind.Brain));
                u.Mind = sprites.Mind;
                u.Will = u.Body.Will * 0.4f;
                u.Command = CommandType.Rally;
                u.CommandTarget = to;
            }
            Alert("Migration! A band of tired Grove Sprites is crossing the map. Easy to catch.", -1, from);
            return;
        }
        if (roll == 0)
        {
            var wells = W.Nodes.Where(n => n.Type == ResourceType.Lumen).ToList();
            if (wells.Count == 0) return;
            var w = wells[Rng.Next(wells.Count)];
            w.Amount += 150f;
            Alert("Lumen Surge! One of the wells overflows.", -1, w.Pos);
        }
        else
        {
            W.EclipseTimer = 60f;
            Alert("Eclipse! Night falls for a minute.", -1);
        }
    }

    public float Score(Kingdom k) =>
        BuildingsOf(k).Count(b => b.Type != BuildingType.BuildSite && !IsWall(b.Type)) * 10f + k.LumenCollected + k.MindsCaptured * 25f + k.WildCaptured * 5f
        + k.Discovered.Count * 5f + Population(k) * 2f;

    void End(int winner, string reason)
    {
        if (Over) return;
        Winner = winner;
        WinReason = reason;
        foreach (var k in Kingdoms)
            foreach (var job in k.Training) job.Stop = true;
    }
}

// The king: walks where it is told, but has the sense to fall back toward its guards and its Keep when enemies come
// close, fights back when grabbed, and returns to its spot once the danger has passed.
public sealed class MoveOrderController : IController
{
    public Vector2? Target;
    public bool Fleeing;

    public (int move, int action) Decide(World w, Unit u)
    {
        if (u.TussleTarget >= 0) return (0, (int)Act.Tussle);
        Unit? threat = null;
        float near = 7f;
        foreach (var o in w.Units)
        {
            if (!o.Active || o.Team == u.Team || o.Subdued || o.CarriedBy >= 0 || o.Species is Species.Sprite or Species.MothKin) continue;
            float d = Vector2.Distance(o.Pos, u.Pos);
            if (d < near && w.CanSee(u, o, w.SightOf(u))) { near = d; threat = o; }
        }
        Fleeing = threat != null;
        if (threat != null)
        {
            // Safest place: the friendly fighter-weighted spot nearest the Keep, away from the threat.
            var keep = w.KeepOf(u.Team);
            var safe = keep?.Pos ?? u.Pos;
            Unit? escort = null;
            float best = 12f;
            foreach (var o in w.Units)
            {
                if (!o.Active || o.Team != u.Team || o == u || o.Subdued || o.StrengthMul < 1f) continue;
                float d = Vector2.Distance(o.Pos, u.Pos);
                if (d < best && Vector2.Distance(o.Pos, threat.Pos) > 1.5f) { best = d; escort = o; }
            }
            if (escort != null && Vector2.Distance(escort.Pos, threat.Pos) < Vector2.Distance(safe, threat.Pos)) safe = escort.Pos;
            var away = u.Pos - threat.Pos;
            var dir = Vector2.Distance(u.Pos, safe) > 1.5f ? safe - u.Pos : away;
            if (Vector2.Dot(Vector2.Normalize(dir), Vector2.Normalize(away)) < -0.5f) dir = away;   // never run into the threat
            return (Steer.Toward(w, u, u.Pos + Vector2.Normalize(dir) * 3f), 0);
        }
        if (Target == null || Vector2.Distance(u.Pos, Target.Value) < 0.6f) { Target = null; return (0, 0); }
        return (Steer.Toward(w, u, w.Nav?.Waypoint(u.Pos, Target.Value, 3f) ?? Target.Value), 0);
    }
}

// Fog of war for one kingdom: what it can see now, and what it has ever seen.
public sealed class Visibility
{
    public readonly byte[] Explored;
    public readonly bool[] Visible;
    readonly World _w;
    readonly int _team;
    readonly Func<Building, float> _sight;

    public Visibility(World w, int team, Func<Building, float> sight)
    {
        _w = w;
        _team = team;
        _sight = sight;
        Explored = new byte[w.Width * w.Height];
        Visible = new bool[w.Width * w.Height];
    }

    public void Update()
    {
        Array.Clear(Visible);
        foreach (var u in _w.Units)
            if (u.Active && u.Team == _team && u.CarriedBy < 0) Reveal(u.Pos, _w.SightOf(u));
        float night = _w.IsNight ? Rules.NightSightMul : 1f;
        foreach (var b in _w.Buildings)
            if (b.Team == _team) Reveal(b.Pos, _sight(b) * night);
    }

    void Reveal(Vector2 p, float r)
    {
        int x0 = Math.Max(0, (int)(p.X - r)), x1 = Math.Min(_w.Width - 1, (int)(p.X + r));
        int y0 = Math.Max(0, (int)(p.Y - r)), y1 = Math.Min(_w.Height - 1, (int)(p.Y + r));
        float r2 = r * r;
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        {
            float dx = x + 0.5f - p.X, dy = y + 0.5f - p.Y;
            if (dx * dx + dy * dy > r2) continue;
            int i = y * _w.Width + x;
            Visible[i] = true;
            Explored[i] = 1;
        }
    }

    public bool Sees(Vector2 p)
    {
        int x = (int)p.X, y = (int)p.Y;
        return x >= 0 && y >= 0 && x < _w.Width && y < _w.Height && Visible[y * _w.Width + x];
    }

    public bool Sees(Unit u) => u.Team == _team || (Sees(u.Pos) && !Hidden(u));

    bool Hidden(Unit u) => _w.TerrainAt(u.Pos) == Terrain.Forest && !_w.Units.Any(o =>
        o.Active && o.Team == _team && Vector2.Distance(o.Pos, u.Pos) <= Rules.ForestHideRange);
}
