using System.Numerics;

namespace FusionKingdoms.Sim;

public static class Rules
{
    public const float Dt = 0.2f;
    public const float InteractRange = 1.3f;
    public const float TussleRange = 1.8f;
    public const float HomeRadius = 10f;
    public const float SubdueTime = 30f;
    public const float ReviveWillFrac = 0.3f;
    public const float GatherRate = 2f;
    public const float BuildRate = 1f;        // build points per second for an average builder
    public const float MillBonus = 0.2f;      // extra timber/stone when dropped at a Lumber Mill
    public const float StealRate = 2f;
    public const float HealRate = 10f;
    public const float RegenRate = 2f;
    public const float OutnumberBonus = 0.25f;
    public const float CarryUnitSpeedMul = 0.5f;
    public const float SabotageTime = 10f;
    public const float SabotageDisable = 30f;
    public const float DayCycle = 240f;
    public const float NightStart = 180f;
    public const float NightSightMul = 0.6f;
    public const float ForestHideRange = 2.5f;
    public const float FarRange = 64f;
    public const float OwnBuildingRange = 32f;
}

public enum EventKind : byte
{
    Gathered, Deposited, Delivered, TussleStarted, Subdued, PickedUp, Captured,
    Stolen, Sabotaged, Revived, Healed, Freed,
    Worked,   // build work put into a site (Amount = build points)
}

public readonly record struct SimEvent(EventKind Kind, int Unit, int Other, ResourceType Res, float Amount);

public sealed class World
{
    public readonly int Width, Height;
    public readonly Terrain[] Map;
    public readonly List<Unit> Units = new();
    public readonly List<Building> Buildings = new();
    public readonly List<ResourceNode> Nodes = new();
    public readonly List<SimEvent> Events = new();
    public Random Rng;
    public float Time;
    public int StepCount;

    float[] _dmg = new float[64];
    int[] _attackers = new int[64];
    bool[] _sabotaged = new bool[16];

    public World(int width, int height, int seed)
    {
        Width = width;
        Height = height;
        Map = new Terrain[width * height];
        Rng = new Random(seed);
    }

    public bool IsNight => EclipseTimer > 0f || Time % Rules.DayCycle >= Rules.NightStart;
    public float EclipseTimer;
    public Navigator? Nav;
    public readonly bool[] Starving = new bool[3];

    // Walls and gates, per tile: 0 = open, else owner team + 1, with 0x80 set for a gate (its owner walks through).
    public byte[] Walls = [];

    public bool WallBlocks(int team, int tile)
    {
        if (Walls.Length == 0) return false;
        byte v = Walls[tile];
        if (v == 0) return false;
        bool gate = (v & 0x80) != 0;
        return !gate || (v & 0x7f) - 1 != team;
    }

    // Can this unit stand at p? Terrain first, then walls (its own gates let it through).
    public bool Blocked(Unit u, Vector2 p)
    {
        if (TerrainInfo.Blocked(TerrainAt(p))) return true;
        int x = (int)p.X, y = (int)p.Y;
        return Walls.Length > 0 && WallBlocks(u.Team, y * Width + x);
    }

    public Terrain TerrainAt(Vector2 p)
    {
        int x = (int)p.X, y = (int)p.Y;
        if (x < 0 || y < 0 || x >= Width || y >= Height) return Terrain.Rock;
        return Map[y * Width + x];
    }

    public void SetTerrain(int x, int y, Terrain t) => Map[y * Width + x] = t;

    public Unit AddUnit(int team, Species species, Vector2 pos, IController? controller)
    {
        var body = Body.Of(species);
        var u = new Unit { Id = Units.Count, Team = team, Species = species, Body = body, Pos = pos, Will = body.Will, Controller = controller };
        Units.Add(u);
        return u;
    }

    public Building AddBuilding(BuildingType type, int team, Vector2 pos)
    {
        var b = new Building { Id = Buildings.Count, Type = type, Team = team, Pos = pos };
        Buildings.Add(b);
        return b;
    }

    public ResourceNode AddNode(ResourceType type, Vector2 pos, float amount = float.PositiveInfinity)
    {
        var n = new ResourceNode { Type = type, Pos = pos, Amount = amount };
        Nodes.Add(n);
        return n;
    }

    public Building? KeepOf(int team)
    {
        foreach (var b in Buildings) if (b.Team == team && b.Type == BuildingType.Keep) return b;
        return null;
    }

    // A unit's home: its kingdom's Keep, or for wild tribes the nearest den.
    public Building? HomeOf(Unit u)
    {
        if (u.Station is { } st && st.Team == u.Team && st.IsStore) return st;
        Building? best = null;
        float bestD = float.MaxValue;
        foreach (var b in Buildings)
        {
            if (b.Team != u.Team || !b.IsStore || b.Type is BuildingType.Camp or BuildingType.Granary or BuildingType.LumberMill) continue;
            float d = Vector2.DistanceSquared(u.Pos, b.Pos);
            if (d < bestD) { bestD = d; best = b; }
        }
        return best;
    }

    public Unit? RulerOf(int team)
    {
        foreach (var u in Units) if (u.Active && u.IsRuler && u.Team == team) return u;
        return null;
    }

    public static bool Hostile(int a, int b) => a != b;

    public float SightOf(Unit u) => u.Body.Sight * u.SightMul * (IsNight ? Rules.NightSightMul : 1f);

    public static float GatherMul(Unit u, ResourceType t) => t switch
    {
        ResourceType.Food => u.FarmMul, ResourceType.Lumen => u.LumenMul, _ => u.MaterialMul,
    };

    // Drop-off points: Keep and Camps take everything, a Granary takes grain, a Lumber Mill takes timber and stone.
    public static bool Accepts(Building b, ResourceType t) => b.Type switch
    {
        BuildingType.Keep or BuildingType.Camp or BuildingType.Den => true,
        BuildingType.Granary => t == ResourceType.Food,
        BuildingType.LumberMill => t is ResourceType.Timber or ResourceType.Stone,
        _ => false,
    };

    public bool CanSee(Unit observer, Unit target, float sight)
    {
        float d = Vector2.Distance(observer.Pos, target.Pos);
        if (d > sight) return false;
        if (target.Team != observer.Team && TerrainAt(target.Pos) == Terrain.Forest && d > Rules.ForestHideRange) return false;
        return true;
    }

    public bool InHome(Unit u)
    {
        var k = HomeOf(u);
        return k != null && Vector2.Distance(u.Pos, k.Pos) < Rules.HomeRadius;
    }

    static readonly Vector2[] MoveDirs =
    {
        Vector2.Zero, new(0, -1), Vector2.Normalize(new(1, -1)), new(1, 0), Vector2.Normalize(new(1, 1)),
        new(0, 1), Vector2.Normalize(new(-1, 1)), new(-1, 0), Vector2.Normalize(new(-1, -1)),
    };

    public static Vector2 MoveDir(int move) => MoveDirs[move];

    public void Step()
    {
        Events.Clear();
        var units = Units;
        if (_dmg.Length < units.Count) { _dmg = new float[units.Count * 2]; _attackers = new int[units.Count * 2]; }
        if (_sabotaged.Length < Buildings.Count) _sabotaged = new bool[Buildings.Count * 2];

        foreach (var u in units)
        {
            if (u.CanAct && u.Controller != null)
                (u.LastMove, u.LastAction) = u.Controller.Decide(this, u);
            else
                (u.LastMove, u.LastAction) = (0, (int)Act.Idle);
        }

        Array.Clear(_sabotaged);
        foreach (var u in units)
            if (u.CanAct) ApplyAction(u, (Act)u.LastAction);

        foreach (var u in units)
            if (u.CanAct && u.LastMove != 0) ApplyMove(u);

        ResolveTussles();

        foreach (var u in units)
        {
            if (!u.Active) continue;
            if (u.CarriedBy >= 0) { u.Pos = units[u.CarriedBy].Pos; continue; }
            if (u.Subdued)
            {
                u.SubduedTimer -= Rules.Dt;
                if (u.SubduedTimer <= 0f) u.Will = u.Body.Will * Rules.ReviveWillFrac;
            }
            else if (u.TussleTarget < 0 && _attackers[u.Id] == 0 && !Starving[u.Team])
            {
                u.Will = MathF.Min(u.Body.Will, u.Will + Rules.RegenRate * Rules.Dt);
            }
        }

        foreach (var b in Buildings)
        {
            if (b.DisabledTimer > 0f) b.DisabledTimer -= Rules.Dt;
            if (!_sabotaged[b.Id]) b.SabotageProgress = MathF.Max(0f, b.SabotageProgress - Rules.Dt * 0.5f);
        }

        if (EclipseTimer > 0f) EclipseTimer -= Rules.Dt;
        Time += Rules.Dt;
        StepCount++;
    }

    void ApplyAction(Unit u, Act act)
    {
        switch (act)
        {
            case Act.Gather: Gather(u); break;
            case Act.Deposit: Deposit(u); break;
            case Act.Build: Build(u); break;
            case Act.Tussle: StartTussle(u); break;
            case Act.Carry: PickUp(u); break;
            case Act.Steal: Steal(u); break;
            case Act.Sabotage: Sabotage(u); break;
            case Act.Rescue: Rescue(u); break;
        }
    }

    void Gather(Unit u)
    {
        if (u.CarryingUnit >= 0) return;
        ResourceNode? best = null;
        float bestD = Rules.InteractRange;
        foreach (var n in Nodes)
        {
            if (n.Amount <= 0f) continue;
            float d = Vector2.Distance(u.Pos, n.Pos);
            if (d <= bestD && (u.CarryAmount <= 0f || u.CarryType == n.Type)) { best = n; bestD = d; }
        }
        if (best == null) return;
        // Rich crops (irrigated farms) fill bigger loads, faster.
        float amt = MathF.Min(MathF.Min(Rules.GatherRate * GatherMul(u, best.Type) * best.Yield * Rules.Dt, u.Carry * best.Yield - u.CarryAmount), best.Amount);
        if (amt <= 0f) return;
        if (u.CarryAmount <= 0f) u.CarryStolen = false;
        best.Amount -= amt;
        u.CarryType = best.Type;
        u.CarryAmount += amt;
        Events.Add(new(EventKind.Gathered, u.Id, -1, best.Type, amt));
    }

    Building? NearestBuilding(Unit u, Func<Building, bool> pred)
    {
        Building? best = null;
        float bestD = float.MaxValue;
        foreach (var b in Buildings)
        {
            if (!pred(b)) continue;
            float d = Vector2.Distance(u.Pos, b.Pos) - b.Radius;
            if (d <= Rules.InteractRange && d < bestD) { best = b; bestD = d; }
        }
        return best;
    }

    void Deposit(Unit u)
    {
        var home = NearestBuilding(u, b => b.Team == u.Team && b.DisabledTimer <= 0f &&
            (u.CarryingUnit >= 0 ? b.Type is BuildingType.Keep or BuildingType.Dungeon or BuildingType.Den or BuildingType.Camp
                                 : b.IsStore && Accepts(b, u.CarryType)));
        if (home == null) return;
        if (u.CarryingUnit >= 0)
        {
            var victim = Units[u.CarryingUnit];
            victim.Active = false;
            victim.CarriedBy = -1;
            u.CarryingUnit = -1;
            Events.Add(new(EventKind.Captured, u.Id, victim.Id, ResourceType.None, 0f));
            return;
        }
        if (u.CarryAmount <= 0f || !home.IsStore) return;
        if (home.Type == BuildingType.LumberMill) u.CarryAmount *= 1f + Rules.MillBonus;
        home.Stored[(int)u.CarryType] += u.CarryAmount;
        Events.Add(new(EventKind.Deposited, u.Id, u.CarryStolen ? 1 : 0, u.CarryType, u.CarryAmount));
        u.CarryAmount = 0f;
        u.CarryType = ResourceType.None;
        u.CarryStolen = false;
    }

    void Build(Unit u)
    {
        var site = NearestBuilding(u, b => b.Team == u.Team && b.Type == BuildingType.BuildSite);
        if (site == null) return;
        // Materials were paid when the site was placed; builders add work. Carried timber or stone still helps.
        if (u.CarryAmount <= 0f || u.CarryType is not (ResourceType.Timber or ResourceType.Stone))
        {
            float work = Rules.BuildRate * u.BuildMul * Rules.Dt;
            site.BuildProgress += work;
            Events.Add(new(EventKind.Worked, u.Id, site.Id, ResourceType.None, work));
            return;
        }
        site.BuildProgress += u.CarryAmount;
        Events.Add(new(EventKind.Delivered, u.Id, site.Id, u.CarryType, u.CarryAmount));
        u.CarryAmount = 0f;
        u.CarryType = ResourceType.None;
        u.CarryStolen = false;
    }

    Unit? NearestUnit(Unit u, float range, Func<Unit, bool> pred)
    {
        Unit? best = null;
        float bestD = range;
        foreach (var o in Units)
        {
            if (o == u || !o.Active || !pred(o)) continue;
            float d = Vector2.Distance(u.Pos, o.Pos);
            if (d <= bestD) { best = o; bestD = d; }
        }
        return best;
    }

    void StartTussle(Unit u)
    {
        if (u.TussleTarget >= 0) return;
        var t = NearestUnit(u, Rules.InteractRange, o => Hostile(u.Team, o.Team) && o.CanAct);
        if (t == null) return;
        u.TussleTarget = t.Id;
        if (t.TussleTarget < 0) t.TussleTarget = u.Id;
        Events.Add(new(EventKind.TussleStarted, u.Id, t.Id, ResourceType.None, 0f));
    }

    void PickUp(Unit u)
    {
        if (u.CarryingUnit >= 0 || u.TussleTarget >= 0) return;
        var t = NearestUnit(u, Rules.InteractRange, o => Hostile(u.Team, o.Team) && o.Subdued && o.CarriedBy < 0);
        if (t == null) return;
        u.CarryAmount = 0f;
        u.CarryType = ResourceType.None;
        u.CarryingUnit = t.Id;
        t.CarriedBy = u.Id;
        Events.Add(new(EventKind.PickedUp, u.Id, t.Id, ResourceType.None, 0f));
    }

    void Steal(Unit u)
    {
        if (u.CarryingUnit >= 0) return;
        var b = NearestBuilding(u, b => Hostile(u.Team, b.Team) && b.IsStore);
        if (b == null) return;
        int type = (int)u.CarryType;
        if (u.CarryAmount <= 0f)
        {
            type = 0;
            for (int i = 1; i < b.Stored.Length; i++) if (b.Stored[i] > b.Stored[type]) type = i;
        }
        if (type == 0 || b.Stored[type] <= 0f) return;
        float amt = MathF.Min(MathF.Min(Rules.StealRate * u.StealMul * Rules.Dt, u.Carry - u.CarryAmount), b.Stored[type]);
        if (amt <= 0f) return;
        b.Stored[type] -= amt;
        u.CarryType = (ResourceType)type;
        u.CarryAmount += amt;
        u.CarryStolen = true;
        Events.Add(new(EventKind.Stolen, u.Id, b.Id, u.CarryType, amt));
    }

    void Sabotage(Unit u)
    {
        var b = NearestBuilding(u, b => Hostile(u.Team, b.Team) && b.DisabledTimer <= 0f);
        if (b == null || _sabotaged[b.Id]) return;
        _sabotaged[b.Id] = true;
        b.SabotageProgress += Rules.Dt;
        if (b.SabotageProgress >= Rules.SabotageTime)
        {
            b.SabotageProgress = 0f;
            b.DisabledTimer = Rules.SabotageDisable;
            Events.Add(new(EventKind.Sabotaged, u.Id, b.Id, ResourceType.None, 0f));
        }
    }

    void Rescue(Unit u)
    {
        var down = NearestUnit(u, Rules.InteractRange, o => o.Team == u.Team && o.Subdued);
        if (down != null)
        {
            if (down.CarriedBy >= 0)
            {
                var carrier = Units[down.CarriedBy];
                carrier.CarryingUnit = -1;
                down.CarriedBy = -1;
                if (u.TussleTarget < 0) u.TussleTarget = carrier.Id;
                if (carrier.TussleTarget < 0) carrier.TussleTarget = u.Id;
                Events.Add(new(EventKind.Freed, u.Id, down.Id, ResourceType.None, 0f));
            }
            else
            {
                down.Will = down.Body.Will * Rules.ReviveWillFrac;
                down.SubduedTimer = 0f;
                Events.Add(new(EventKind.Revived, u.Id, down.Id, ResourceType.None, 0f));
            }
            return;
        }
        var hurt = NearestUnit(u, Rules.InteractRange, o => o.Team == u.Team && o.CanAct && o.Will < o.Body.Will);
        if (hurt == null) return;
        float amt = MathF.Min(Rules.HealRate * u.HealMul * Rules.Dt, hurt.Body.Will - hurt.Will);
        hurt.Will += amt;
        Events.Add(new(EventKind.Healed, u.Id, hurt.Id, ResourceType.None, amt));
    }

    void ApplyMove(Unit u)
    {
        if (u.TussleTarget >= 0)
        {
            var opp = Units[u.TussleTarget];
            // Breaking free takes speed: about 10% a step against an equal, much better if faster, hopeless if slower.
            float r = u.Body.Speed * u.SpeedMul / (opp.Body.Speed * opp.SpeedMul);
            float p = Math.Clamp(0.1f * r * r * r, 0.02f, 0.6f);
            if (Rng.NextSingle() > p) return;
            BreakTussles(u);
        }
        float mul = TerrainInfo.SpeedMul(TerrainAt(u.Pos));
        if (u.CarryingUnit >= 0) mul *= MathF.Min(1f, Rules.CarryUnitSpeedMul * u.HaulMul);
        var step = MoveDirs[u.LastMove] * (u.Body.Speed * u.SpeedMul * mul * Rules.Dt);
        var np = u.Pos + step;
        np.X = Math.Clamp(np.X, 0.5f, Width - 0.5f);
        np.Y = Math.Clamp(np.Y, 0.5f, Height - 0.5f);
        if (!Blocked(u, np)) { u.Pos = np; return; }
        var sx = new Vector2(np.X, u.Pos.Y);
        if (!Blocked(u, sx)) { u.Pos = sx; return; }
        var sy = new Vector2(u.Pos.X, np.Y);
        if (!Blocked(u, sy)) u.Pos = sy;
    }

    void BreakTussles(Unit u)
    {
        u.TussleTarget = -1;
        foreach (var o in Units) if (o.TussleTarget == u.Id) o.TussleTarget = -1;
    }

    void ResolveTussles()
    {
        int n = Units.Count;
        Array.Clear(_dmg, 0, n);
        Array.Clear(_attackers, 0, n);
        foreach (var u in Units)
        {
            if (u.TussleTarget < 0) continue;
            var t = Units[u.TussleTarget];
            if (!u.CanAct || !t.CanAct || Vector2.Distance(u.Pos, t.Pos) > Rules.TussleRange)
            {
                u.TussleTarget = -1;
                continue;
            }
            _dmg[t.Id] += u.Body.Strength * u.StrengthMul * u.AuraMul;
            _attackers[t.Id]++;
        }
        for (int i = 0; i < n; i++)
        {
            if (_attackers[i] == 0) continue;
            var t = Units[i];
            t.Will -= _dmg[i] * (1f + Rules.OutnumberBonus * (_attackers[i] - 1)) * Rules.Dt;
            if (t.Will > 0f) continue;
            int by = -1;
            foreach (var o in Units) if (o.TussleTarget == i) { by = o.Id; break; }
            SubdueUnit(t, by);
        }
    }

    void SubdueUnit(Unit t, int by)
    {
        t.Will = 0f;
        t.SubduedTimer = Rules.SubdueTime;
        BreakTussles(t);
        if (t.CarryingUnit >= 0) { Units[t.CarryingUnit].CarriedBy = -1; t.CarryingUnit = -1; }
        t.CarryAmount = 0f;
        t.CarryType = ResourceType.None;
        t.CarryStolen = false;
        Events.Add(new(EventKind.Subdued, t.Id, by, ResourceType.None, 0f));
    }
}
