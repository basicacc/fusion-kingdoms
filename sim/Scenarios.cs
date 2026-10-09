using System.Numerics;

namespace FusionKingdoms.Sim;

public enum MindKind { Base, Farmer, Builder, Guard, Scout, Thief, Hunter, Healer, Hoarder, All }

// One training episode around a learner (team 0).
// Context gating: every specialist is rewarded for everyday work (the base reward) everywhere, plus its
// specialty only in its own trigger situation, which appears in ContextChance of episodes. That keeps each
// specialist's change from the base confined to its trigger, so blends of two specialists can keep both skills.
public abstract class Scenario
{
    public const int Size = 28;
    public const int MaxSteps = 400;

    public World W = null!;
    public Unit Learner = null!;
    public ExternalController Input = new();
    public Building Keep0 = null!;
    public Building? Keep1;
    public Unit Ruler = null!;
    public bool Done;

    public float ContextChance = 0.6f;
    public bool SpecialOnly;

    protected Random Rng = null!;
    protected bool Ctx;
    public bool InContext => Ctx;
    float _lastCmdDist;
    Vector2 _k1Pos;

    public static Scenario Create(MindKind kind) => kind switch
    {
        MindKind.Base => new BaseScenario(),
        MindKind.Farmer => new FarmerScenario(),
        MindKind.Builder => new BuilderScenario(),
        MindKind.Guard => new GuardScenario(),
        MindKind.Scout => new ScoutScenario(),
        MindKind.Thief => new ThiefScenario(),
        MindKind.Hunter => new HunterScenario(),
        MindKind.Healer => new HealerScenario(),
        MindKind.Hoarder => new HoarderScenario(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    protected abstract MindKind Kind { get; }

    public void Reset(int seed)
    {
        Rng = new Random(seed);
        W = new World(Size, Size, seed);
        W.Time = Rng.NextSingle() * Rules.DayCycle;
        Done = false;
        Keep1 = null;
        Ctx = Rng.NextSingle() < ContextChance;
        BuildMap();
        Learner = W.AddUnit(0, Species.Human, FreePos(Keep0.Pos, 2f, 4f), Input);
        Ruler = W.AddUnit(0, Species.Human, FreePos(Keep0.Pos, 2f, 4f), new WanderScript(Keep0.Pos, 5f));
        Ruler.IsRuler = true;
        if (Rng.NextSingle() < 0.3f) Learner.Stance = Rng.Next(-1, 2);
        Background();
        if (Ctx) AddContext();
        _lastCmdDist = Vector2.Distance(Learner.Pos, Learner.CommandTarget);
    }

    public float Step(int move, int action)
    {
        Input.Move = move;
        Input.Action = action;
        W.Step();
        Tick();
        // With the trigger present the specialty must dominate, or steady farming drowns out exploring it.
        float everyday = BaseReward() * (Ctx && Kind != MindKind.Base ? 0.2f : 1f);
        float r = (SpecialOnly ? 0f : everyday) + (Ctx ? Special() : 0f);
        if (Learner.Subdued || !Learner.Active) { r -= SubduedPenalty; Done = true; }
        if (W.StepCount >= MaxSteps) Done = true;
        return r;
    }

    protected virtual float SubduedPenalty => 1f;
    protected virtual void AddContext() { }
    protected virtual float Special() => 0f;
    protected virtual void Tick() { }

    // Everyday work: gather, bring it home, follow work and rally orders, stay on your feet.
    float BaseReward()
    {
        float r = 0.1f * Sum(EventKind.Deposited) + 0.02f * Sum(EventKind.Gathered);
        if (Learner.Command is CommandType.WorkZone or CommandType.Rally)
        {
            float d = Vector2.Distance(Learner.Pos, Learner.CommandTarget);
            r += 0.05f * (_lastCmdDist - d);
            if (d < 3f) r += 0.01f;
            _lastCmdDist = d;
        }
        return r;
    }

    // Other specialists' trigger situations show up at random with only the base reward,
    // so every mind learns that its default there is to keep working.
    void Background()
    {
        if (Rng.Next(2) == 0) AddEnemyKeep(Rng.Next(2) == 0);
        if (Kind != MindKind.Thief && Keep1 != null && Rng.NextSingle() < 0.15f)
        {
            Learner.Command = CommandType.Raid;
            Learner.CommandTarget = Keep1.Pos;
        }
        else if (Rng.NextSingle() < 0.25f)
        {
            Learner.Command = Rng.Next(2) == 0 ? CommandType.WorkZone : CommandType.Rally;
            Learner.CommandTarget = RandomFree();
        }
        if (Kind != MindKind.Builder && Rng.NextSingle() < 0.25f) W.AddBuilding(BuildingType.BuildSite, 0, FreePos(Keep0.Pos, 3f, 7f));
        if (Kind != MindKind.Guard && Rng.NextSingle() < 0.2f) AddHostile(new HunterScript(6f), FreePos(Keep0.Pos, 9f, 14f));
        if (Kind != MindKind.Hunter && Rng.NextSingle() < 0.3f) { AddMoths(1 + Rng.Next(2)); AddWilds(Rng.Next(2), Rng.Next(4) == 0 ? 1 : 0); }
        if (Kind != MindKind.Healer && Rng.NextSingle() < 0.15f) AddDowned(1);
        if (Kind != MindKind.Hoarder && Rng.NextSingle() < 0.2f) AddLumenNearHome(1 + Rng.Next(2));
        if (Kind != MindKind.Scout && Rng.NextSingle() < 0.1f) MakeScarce();
        AddWorkers(Rng.Next(3));
    }

    protected float Sum(EventKind kind, Func<SimEvent, bool>? pred = null)
    {
        float s = 0f;
        foreach (var e in W.Events)
            if (e.Kind == kind && e.Unit == Learner.Id && (pred == null || pred(e)))
                s += e.Amount;
        return s;
    }

    protected int Count(EventKind kind)
    {
        int c = 0;
        foreach (var e in W.Events) if (e.Kind == kind && e.Unit == Learner.Id) c++;
        return c;
    }

    protected bool IsWild(int id) => W.Units[id].Team == 2;
    protected bool IsHostile(int id) => W.Units[id].Team != 0;

    // Potential-based shaping toward the current objective; ignores the jump when the objective changes.
    protected float Approach(ref float last, float now)
    {
        float r = last >= 0f && MathF.Abs(last - now) < 3f ? 0.02f * (last - now) : 0f;
        last = now;
        return r;
    }

    void BuildMap()
    {
        var k0 = new Vector2(5.5f, Rng.Next(6, 22) + 0.5f);
        var k1 = new Vector2(Size - k0.X, Size - k0.Y);
        for (int i = 0; i < 4; i++) Blob(Terrain.Forest, 1.5f + Rng.NextSingle() * 1.5f, k0, k1, 4f);
        for (int i = 0; i < 2; i++) Blob(Terrain.Rock, 1f + Rng.NextSingle() * 0.6f, k0, k1, 5f);

        Keep0 = W.AddBuilding(BuildingType.Keep, 0, k0);
        for (int i = 0; i < 3; i++) Field(FreePos(k0, 3f, 7f));
        for (int x = 0; x < Size; x++)
        for (int y = 0; y < Size; y++)
        {
            var c = new Vector2(x + 0.5f, y + 0.5f);
            if (W.Map[y * Size + x] == Terrain.Forest && Rng.Next(7) == 0) W.AddNode(ResourceType.Timber, c, 40f);
            if (W.Map[y * Size + x] == Terrain.Rock && Rng.Next(3) == 0)
            {
                var p = FreePos(c, 1f, 1.6f);
                if (W.TerrainAt(p) != Terrain.Rock) W.AddNode(ResourceType.Stone, p, 40f);
            }
        }
        var center = new Vector2(Size / 2f, Size / 2f);
        for (int i = 0; i < 2; i++) W.AddNode(ResourceType.Lumen, FreePos(center, 0f, 4f), 30f);
        _k1Pos = k1;
    }

    protected Building AddEnemyKeep(bool stocked)
    {
        if (Keep1 != null) return Keep1;
        Keep1 = W.AddBuilding(BuildingType.Keep, 1, _k1Pos);
        for (int i = 0; i < 3; i++) Field(FreePos(_k1Pos, 3f, 7f));
        if (stocked)
        {
            Keep1.Stored[(int)ResourceType.Food] = 40f + Rng.Next(40);
            Keep1.Stored[(int)ResourceType.Timber] = Rng.Next(30);
            Keep1.Stored[(int)ResourceType.Lumen] = Rng.Next(15);
        }
        return Keep1;
    }

    void Field(Vector2 p)
    {
        W.SetTerrain((int)p.X, (int)p.Y, Terrain.Farmland);
        W.AddNode(ResourceType.Food, new Vector2((int)p.X + 0.5f, (int)p.Y + 0.5f));
    }

    void Blob(Terrain t, float r, Vector2 avoidA, Vector2 avoidB, float clearance)
    {
        for (int tries = 0; tries < 20; tries++)
        {
            var c = new Vector2(2 + Rng.NextSingle() * (Size - 4), 2 + Rng.NextSingle() * (Size - 4));
            if (Vector2.Distance(c, avoidA) < clearance + r || Vector2.Distance(c, avoidB) < clearance + r) continue;
            for (int x = (int)(c.X - r); x <= (int)(c.X + r); x++)
            for (int y = (int)(c.Y - r); y <= (int)(c.Y + r); y++)
                if (x >= 0 && y >= 0 && x < Size && y < Size && Vector2.Distance(new(x + 0.5f, y + 0.5f), c) <= r)
                    W.SetTerrain(x, y, t);
            return;
        }
    }

    protected Vector2 FreePos(Vector2 center, float rmin, float rmax)
    {
        for (int tries = 0; tries < 40; tries++)
        {
            float a = Rng.NextSingle() * MathF.Tau, d = rmin + Rng.NextSingle() * (rmax - rmin);
            var p = center + new Vector2(MathF.Cos(a), MathF.Sin(a)) * d;
            if (p.X < 1 || p.Y < 1 || p.X > Size - 1 || p.Y > Size - 1) continue;
            if (!TerrainInfo.Blocked(W.TerrainAt(p))) return p;
        }
        return center;
    }

    protected Vector2 RandomFree() => FreePos(new Vector2(Size / 2f, Size / 2f), 0f, Size / 2f - 1f);

    protected void AddWilds(int sprites, int trolls)
    {
        for (int i = 0; i < sprites; i++) { var p = RandomFree(); W.AddUnit(2, Species.Sprite, p, new SpriteScript(p)); }
        for (int i = 0; i < trolls; i++) { var p = RandomFree(); W.AddUnit(2, Species.Troll, p, new TrollScript(p)); }
    }

    protected void AddMoths(int n)
    {
        for (int i = 0; i < n; i++) { var p = RandomFree(); W.AddUnit(2, Species.MothKin, p, new WanderScript(p, 4f)); }
    }

    protected void AddWorkers(int n)
    {
        for (int i = 0; i < n; i++)
            W.AddUnit(0, Species.Human, FreePos(Keep0.Pos, 2f, 5f), new WorkerScript(ResourceType.Food));
    }

    protected void AddDowned(int n)
    {
        for (int i = 0; i < n; i++)
        {
            var u = W.AddUnit(0, Species.Human, FreePos(Keep0.Pos, 3f, 8f), new WorkerScript(ResourceType.Food));
            u.Will = 0f;
            u.SubduedTimer = 30f + Rng.NextSingle() * 30f;
        }
    }

    protected void AddLumenNearHome(int n)
    {
        for (int i = 0; i < n; i++) W.AddNode(ResourceType.Lumen, FreePos(Keep0.Pos, 4f, 9f), 20f);
    }

    protected void MakeScarce()
    {
        foreach (var n in W.Nodes)
            if (n.Type != ResourceType.Lumen && Vector2.Distance(n.Pos, Keep0.Pos) < 12f) n.Amount = 0f;
    }

    protected Unit AddHostile(IController c, Vector2 at) => W.AddUnit(1, Species.Human, at, c);

    protected void Respawn(Unit u, Vector2 at)
    {
        u.Active = true;
        u.Pos = at;
        u.Will = u.Body.Will;
        u.SubduedTimer = 0f;
        u.CarriedBy = -1;
        u.TussleTarget = -1;
    }
}

sealed class BaseScenario : Scenario
{
    protected override MindKind Kind => MindKind.Base;
}

// Farmer: grain over everything else, always.
sealed class FarmerScenario : Scenario
{
    protected override MindKind Kind => MindKind.Farmer;
    protected override float Special() =>
        0.15f * Sum(EventKind.Deposited, e => e.Res == ResourceType.Food) + 0.02f * Sum(EventKind.Gathered, e => e.Res == ResourceType.Food);
}

// Builder trigger: a build site waiting for work, with timber and stone nearby.
// Building is work done at the site (materials come from storage); carrying timber or stone to it helps too.
sealed class BuilderScenario : Scenario
{
    protected override MindKind Kind => MindKind.Builder;
    Building _site = null!;
    float _lastD;

    protected override void AddContext()
    {
        W.AddNode(ResourceType.Timber, FreePos(Keep0.Pos, 3f, 6f), 40f);
        W.AddNode(ResourceType.Stone, FreePos(Keep0.Pos, 3f, 6f), 40f);
        _site = W.AddBuilding(BuildingType.BuildSite, 0, FreePos(Keep0.Pos, 2.5f, 6f));
        _lastD = -1f;
    }

    protected override float Special() =>
        Approach(ref _lastD, Vector2.Distance(Learner.Pos, _site.Pos))
        + 0.4f * Sum(EventKind.Worked)
        + 0.3f * Sum(EventKind.Delivered)
        + 0.03f * Sum(EventKind.Gathered, e => e.Res is ResourceType.Timber or ResourceType.Stone);
}

// Guard trigger: intruders coming at the ruler.
sealed class GuardScenario : Scenario
{
    protected override MindKind Kind => MindKind.Guard;
    float _spawn;

    protected override void AddContext()
    {
        AddEnemyKeep(false);
        _spawn = 2f + Rng.NextSingle() * 4f;
        _lastD = -1f;
    }

    protected override void Tick()
    {
        if (!Ctx) return;
        _spawn -= Rules.Dt;
        if (_spawn > 0f) return;
        _spawn = 12f + Rng.NextSingle() * 10f;
        int active = 0;
        foreach (var u in W.Units) if (u.Active && u.Team == 1) active++;
        if (active >= 3) return;
        IController c = Rng.Next(2) == 0 ? new HunterScript(8f) : new RaiderScript(0, 0.7f);
        AddHostile(c, FreePos(Keep1!.Pos, 1f, 4f));
    }

    protected override float SubduedPenalty => 0.5f;

    float _lastD = -1f;

    protected override float Special()
    {
        Unit? threat = null;
        foreach (var u in W.Units)
            if (u.Active && u.Team == 1 && !u.Subdued && Vector2.Distance(u.Pos, Ruler.Pos) < 8f) { threat = u; break; }
        float r = 0f;
        if (threat != null)
        {
            r += Approach(ref _lastD, Vector2.Distance(Learner.Pos, threat.Pos));
            if (Vector2.Distance(Learner.Pos, Ruler.Pos) < 3.5f) r += 0.02f;
            if (Learner.TussleTarget >= 0 && W.Units[Learner.TussleTarget].Team == 1) r += 0.05f;
        }
        else _lastD = -1f;
        foreach (var e in W.Events)
        {
            if (e.Kind == EventKind.Subdued && e.Other == Learner.Id && IsHostile(e.Unit)
                && Vector2.Distance(W.Units[e.Unit].Pos, Ruler.Pos) < 8f) r += 1.5f;
            if (e.Kind == EventKind.Subdued && e.Unit == Ruler.Id) r -= 2f;
            if (e.Kind == EventKind.Captured && e.Unit == Learner.Id) r += 1f;
            if (e.Kind == EventKind.Captured && e.Other == Ruler.Id) { r -= 3f; Done = true; }
        }
        return r;
    }
}

// Scout trigger: nothing to gather anywhere near home.
sealed class ScoutScenario : Scenario
{
    protected override MindKind Kind => MindKind.Scout;
    readonly bool[] _seen = new bool[(Size / 2) * (Size / 2)];
    bool _first;

    protected override void AddContext()
    {
        MakeScarce();
        for (int i = Rng.Next(2); i > 0; i--) AddHostile(new HunterScript(6f), RandomFree());
        Array.Clear(_seen);
        _first = true;
    }

    protected override float SubduedPenalty => 1.5f;

    protected override float Special()
    {
        int fresh = 0;
        float s = W.SightOf(Learner);
        for (int cx = 0; cx < Size / 2; cx++)
        for (int cy = 0; cy < Size / 2; cy++)
        {
            int i = cy * (Size / 2) + cx;
            if (_seen[i] || Vector2.Distance(Learner.Pos, new Vector2(cx * 2 + 1, cy * 2 + 1)) > s) continue;
            _seen[i] = true;
            fresh++;
        }
        if (_first) { _first = false; return 0f; }
        return 0.1f * fresh;
    }
}

// Thief trigger: a Raid order on a stocked enemy Keep.
sealed class ThiefScenario : Scenario
{
    protected override MindKind Kind => MindKind.Thief;
    float _lastD;

    protected override void AddContext()
    {
        var k1 = AddEnemyKeep(true);
        if (k1.Stored[(int)ResourceType.Food] <= 0f) k1.Stored[(int)ResourceType.Food] = 40f + Rng.Next(40);
        for (int i = Rng.Next(0, 3); i > 0; i--)
        {
            var post = FreePos(k1.Pos, 2f, 4f);
            AddHostile(new GuardScript(post, 3f), post);
        }
        Learner.Command = CommandType.Raid;
        Learner.CommandTarget = k1.Pos;
        _lastD = -1f;
    }

    protected override float SubduedPenalty => 1.5f;

    protected override float Special()
    {
        float d = Vector2.Distance(Learner.Pos, Learner.CarryStolen ? Keep0.Pos : Keep1!.Pos);
        return Approach(ref _lastD, d) + 0.05f * Sum(EventKind.Stolen) + 0.25f * Sum(EventKind.Deposited, e => e.Other == 1);
    }
}

// Hunter trigger: catchable wild creatures in the area.
sealed class HunterScenario : Scenario
{
    protected override MindKind Kind => MindKind.Hunter;
    float _lastD;

    protected override void AddContext()
    {
        AddMoths(2 + Rng.Next(2));
        AddWilds(Rng.Next(2), Rng.Next(4) == 0 ? 1 : 0);
        _lastD = -1f;
    }

    protected override void Tick()
    {
        if (!Ctx) return;
        foreach (var u in W.Units)
            if (!u.Active && u.Team == 2) Respawn(u, RandomFree());
    }

    protected override float Special()
    {
        float d = 30f;
        if (Learner.CarryingUnit >= 0) d = Vector2.Distance(Learner.Pos, Keep0.Pos);
        else
            foreach (var u in W.Units)
                if (u.Active && u.Team == 2 && u.CarriedBy < 0 && u.Species != Species.Troll)
                    d = MathF.Min(d, Vector2.Distance(Learner.Pos, u.Pos));
        float r = Approach(ref _lastD, d);
        if (Learner.TussleTarget >= 0 && IsWild(Learner.TussleTarget)) r += 0.05f;
        foreach (var e in W.Events)
        {
            if (e.Kind == EventKind.Subdued && e.Other == Learner.Id && IsWild(e.Unit)) r += 1f;
            if (e.Kind == EventKind.PickedUp && e.Unit == Learner.Id) r += 0.3f;
            if (e.Kind == EventKind.Captured && e.Unit == Learner.Id) r += 3f;
        }
        return r;
    }
}

// Healer trigger: an ally lying subdued nearby.
sealed class HealerScenario : Scenario
{
    protected override MindKind Kind => MindKind.Healer;
    float _lastD;

    protected override void AddContext()
    {
        AddDowned(1 + Rng.Next(2));
        foreach (var u in W.Units)
            if (u.Team == 0 && u != Learner && !u.Subdued && !u.IsRuler && Rng.Next(2) == 0)
                u.Will = u.Body.Will * (0.2f + Rng.NextSingle() * 0.5f);
        if (Rng.Next(3) == 0) AddWilds(0, 1);
        _lastD = -1f;
    }

    protected override float Special()
    {
        float d = 30f;
        foreach (var u in W.Units)
            if (u.Active && u.Team == 0 && u != Learner && u.Subdued)
                d = MathF.Min(d, Vector2.Distance(Learner.Pos, u.Pos));
        float r = Approach(ref _lastD, d) + 0.01f * Sum(EventKind.Healed);
        r += 1.5f * Count(EventKind.Revived) + 1f * Count(EventKind.Freed);
        return r;
    }
}

// Hoarder trigger: Lumen within reach of home.
sealed class HoarderScenario : Scenario
{
    protected override MindKind Kind => MindKind.Hoarder;

    protected override void AddContext() => AddLumenNearHome(2 + Rng.Next(2));

    protected override float Special() =>
        0.4f * Sum(EventKind.Deposited, e => e.Res == ResourceType.Lumen)
        + 0.1f * Sum(EventKind.Deposited, e => e.Other == 1)
        + 0.04f * Sum(EventKind.Gathered, e => e.Res == ResourceType.Lumen);
}

// Brawl: the learner is out working when one to three attackers of random strength come for it. Winning a fight it
// can win pays; being dragged off is the worst outcome; getting home to friends counts as getting away.
// Used as a Training Grounds drill ("Brawl") to teach a mind when to fight and when to run.
public sealed class BrawlScenario : Scenario
{
    protected override MindKind Kind => MindKind.Guard;
    readonly List<Unit> _foes = new();
    float _odds;

    protected override void AddContext()
    {
        AddEnemyKeep(false);
        _foes.Clear();
        int n = 1 + Rng.Next(3);
        Learner.Pos = FreePos(Keep0.Pos, 7f, 11f);
        for (int i = 0; i < n; i++)
        {
            var f = AddHostile(new HunterScript(12f), FreePos(Learner.Pos, 2.5f, 5f));
            f.StrengthMul = 0.7f + Rng.NextSingle() * 0.9f;
            f.SpeedMul = 0.85f + Rng.NextSingle() * 0.3f;
            _foes.Add(f);
        }
        // Sometimes friends stand guard at home: running to them is the smart move when outmatched.
        for (int i = Rng.Next(3); i > 0; i--)
        {
            var post = FreePos(Keep0.Pos, 1f, 3f);
            W.AddUnit(0, Species.Human, post, new GuardScript(post, 6f));
        }
        _odds = _foes.Sum(f => f.StrengthMul) / MathF.Max(0.1f, Learner.StrengthMul);
    }

    protected override float SubduedPenalty => 2f;

    protected override float Special()
    {
        float r = 0.004f;   // still standing
        foreach (var e in W.Events)
        {
            if (e.Kind == EventKind.Subdued && e.Other == Learner.Id && _foes.Any(f => f.Id == e.Unit)) r += 1.5f;
            if (e.Kind == EventKind.Captured && e.Other == Learner.Id) { r -= 3f; Done = true; }
        }
        // Safe at home among friends while being hunted.
        if (W.InHome(Learner) && _foes.Any(f => f.Active && !f.Subdued && Vector2.Distance(f.Pos, Learner.Pos) < 8f)) r += 0.01f;
        return r;
    }
}

