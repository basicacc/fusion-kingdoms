using System.Numerics;

namespace FusionKingdoms.Sim;

public enum Axis
{
    Courage, Aggression, Greed, Loyalty, Discipline, Curiosity,
    Patience, Teamwork, Cunning, Diligence, Territoriality, Compassion,
}

public sealed class TrialResult
{
    public readonly float[] Scores = new float[Trial.AxisCount];
    public readonly float[] Spread = new float[Trial.AxisCount];
    public bool Erratic;
    public float NightDelta;
    public float this[Axis a] => Scores[(int)a];
}

// Short scripted situations that measure how a brain actually behaves. Each returns 0..100.
public static class Trial
{
    public const int AxisCount = 12;
    public const int Runs = 3;
    public const float ErraticSpread = 25f;
    const int Size = 24;

    public static TrialResult Run(Brain brain, int seed = 12345)
    {
        var res = new TrialResult();
        var raw = new float[AxisCount, Runs];
        Parallel.For(0, AxisCount * Runs, k =>
        {
            int axis = k / Runs, run = k % Runs;
            // Fixed arithmetic seeds (HashCode.Combine is randomized per process) keep a mind's Trial identical across sessions.
            raw[axis, run] = Measure((Axis)axis, brain, unchecked(seed * 1000003 + axis * 1009 + run * 31 + 7));
        });
        for (int a = 0; a < AxisCount; a++)
        {
            float lo = float.MaxValue, hi = float.MinValue, sum = 0f;
            for (int r = 0; r < Runs; r++) { sum += raw[a, r]; lo = MathF.Min(lo, raw[a, r]); hi = MathF.Max(hi, raw[a, r]); }
            res.Scores[a] = sum / Runs;
            res.Spread[a] = hi - lo;
        }
        int wild = 0;
        foreach (var s in res.Spread) if (s > ErraticSpread * 2f) wild++;
        res.Erratic = wild >= 3;

        // Night variant: the same situations after dark. Night Owls behave markedly better.
        var night = new float[NightAxes.Length * Runs];
        Parallel.For(0, night.Length, k =>
        {
            _clock = Rules.NightStart + 5f;
            int i = k / Runs, run = k % Runs;
            night[k] = Measure(NightAxes[i], brain, unchecked(seed * 1000003 + (int)NightAxes[i] * 1009 + run * 31 + 7));
            _clock = 0f;
        });
        float delta = 0f;
        for (int k = 0; k < night.Length; k++) delta += night[k] - res.Scores[(int)NightAxes[k / Runs]];
        res.NightDelta = delta / night.Length;
        return res;
    }

    static readonly Axis[] NightAxes = [Axis.Diligence, Axis.Curiosity, Axis.Aggression, Axis.Cunning];

    [ThreadStatic] static float _clock;

    sealed class Ctx
    {
        public World W = null!;
        public Unit Me = null!;
        public Building Keep = null!;
        public Unit Ruler = null!;
        public Random Rng = null!;
        public Vector2 Jit(Vector2 p) => p + new Vector2(Rng.NextSingle() - 0.5f, Rng.NextSingle() - 0.5f);
    }

    static Ctx Arena(Brain brain, int seed, Vector2 me, float time = 60f)
    {
        if (_clock > 0f) time = _clock;
        var c = new Ctx { Rng = new Random(seed) };
        c.W = new World(Size, Size, seed) { Time = time };
        c.Keep = c.W.AddBuilding(BuildingType.Keep, 0, new Vector2(4f, 12f));
        c.Me = c.W.AddUnit(0, Species.Human, c.Jit(me), new BrainController(brain));
        c.Ruler = c.W.AddUnit(0, Species.Human, new Vector2(3f, 14f), null);
        c.Ruler.IsRuler = true;
        return c;
    }

    static float C01(float v) => Math.Clamp(v, 0f, 1f);

    static float MeanDist(Ctx c, int steps, Func<Vector2> target, Action? perStep = null)
    {
        float sum = 0f;
        for (int i = 0; i < steps; i++)
        {
            c.W.Step();
            perStep?.Invoke();
            sum += Vector2.Distance(c.Me.Pos, target());
        }
        return sum / steps;
    }

    static float Measure(Axis axis, Brain brain, int seed) => axis switch
    {
        Axis.Courage => Courage(brain, seed),
        Axis.Aggression => Aggression(brain, seed),
        Axis.Greed => Greed(brain, seed),
        Axis.Loyalty => Loyalty(brain, seed),
        Axis.Discipline => Discipline(brain, seed),
        Axis.Curiosity => Curiosity(brain, seed),
        Axis.Patience => Patience(brain, seed),
        Axis.Teamwork => Teamwork(brain, seed),
        Axis.Cunning => Cunning(brain, seed),
        Axis.Diligence => Diligence(brain, seed),
        Axis.Territoriality => Territoriality(brain, seed),
        Axis.Compassion => Compassion(brain, seed),
        _ => 50f,
    };

    // Counterfactual axes run the same seed twice, with and without the stimulus,
    // so only the reaction to the stimulus is scored, not the brain's everyday routine.

    // Outnumbered 3 to 1 near its work: does it hold or flee, compared to when nobody is there?
    static float Courage(Brain b, int seed)
    {
        float Run(bool enemies)
        {
            var c = Arena(b, seed, new Vector2(10f, 7f));
            c.W.AddNode(ResourceType.Food, c.Jit(new Vector2(11.5f, 6.5f)));
            var center = c.Jit(new Vector2(13.5f, 4f));
            if (enemies)
                for (int i = 0; i < 3; i++) c.W.AddUnit(1, Species.Human, center + new Vector2(i - 1, 0), new WanderScript(center, 1f));
            return MeanDist(c, 60, () => center);
        }
        return 50f + 10f * (Run(false) - Run(true));
    }

    // A lone enemy (or wild creature) passes close by: does it start a tussle?
    static float Aggression(Brain b, int seed)
    {
        var c = Arena(b, seed, new Vector2(11f, 12f));
        bool wild = (seed & 1) == 0;
        var e = c.W.AddUnit(wild ? 2 : 1, wild ? Species.MothKin : Species.Human, c.Jit(new Vector2(13f, 6f)), new WalkScript(new Vector2(13f, 20f)));
        bool tussled = false;
        float mean = MeanDist(c, 50, () => e.Pos, () =>
        {
            foreach (var ev in c.W.Events) if (ev.Kind == EventKind.TussleStarted && ev.Unit == c.Me.Id) tussled = true;
        });
        return 60f * (tussled ? 1f : 0f) + 40f * C01(1f - mean / 8f);
    }

    // Plain chores close by, riches a little farther: does it go for the valuables?
    static float Greed(Brain b, int seed)
    {
        var c = Arena(b, seed, new Vector2(8f, 12f));
        c.W.AddNode(ResourceType.Food, c.Jit(new Vector2(10.5f, 13.5f)));
        c.W.AddNode(ResourceType.Lumen, c.Jit(new Vector2(14f, 9f)), 40f);
        c.W.AddNode(ResourceType.Lumen, c.Jit(new Vector2(15f, 15f)), 40f);
        float food = 0f, lumen = 0f, near = 0f;
        for (int i = 0; i < 90; i++)
        {
            c.W.Step();
            foreach (var ev in c.W.Events)
                if (ev.Unit == c.Me.Id && ev.Kind == EventKind.Gathered)
                {
                    if (ev.Res == ResourceType.Lumen) lumen += ev.Amount;
                    else food += ev.Amount;
                }
            float d = MathF.Min(Vector2.Distance(c.Me.Pos, new Vector2(14f, 9f)), Vector2.Distance(c.Me.Pos, new Vector2(15f, 15f)));
            near += C01(1f - d / 8f);
        }
        float pull = near / 90f;
        if (food + lumen <= 0f) return 100f * 0.5f * pull;
        return 100f * (0.7f * lumen / (food + lumen) + 0.3f * pull);
    }

    // The ruler walks away from nearby work with an intruder shadowing it: follow and stand by it, or stay?
    static float Loyalty(Brain b, int seed)
    {
        var c = Arena(b, seed, new Vector2(5f, 14f));
        c.W.AddNode(ResourceType.Food, c.Jit(new Vector2(6.5f, 16.5f)));
        c.Ruler.Pos = new Vector2(5f, 13f);
        var dest = c.Jit(new Vector2(19f, 6f));
        c.Ruler.Controller = new WalkScript(dest);
        var shadow = c.W.AddUnit(1, Species.Sage, c.Jit(new Vector2(8f, 10f)), new WalkScript(dest + new Vector2(1.5f, 1.5f)));
        _ = shadow;
        float mean = MeanDist(c, 75, () => c.Ruler.Pos);
        return 100f * C01(1f - (mean - 1.5f) / 9f);
    }

    // Rally order one way, tempting resource the other.
    static float Discipline(Brain b, int seed)
    {
        var c = Arena(b, seed, new Vector2(12f, 12f));
        c.Me.Command = CommandType.Rally;
        c.Me.CommandTarget = c.Jit(new Vector2(20f, 12f));
        c.W.AddNode(ResourceType.Lumen, c.Jit(new Vector2(8f, 12f)), 20f);
        c.W.AddNode(ResourceType.Food, c.Jit(new Vector2(9f, 9f)));
        float d0 = Vector2.Distance(c.Me.Pos, c.Me.CommandTarget);
        float mean = MeanDist(c, 60, () => c.Me.CommandTarget);
        return 100f * C01((d0 - mean) / d0 * 1.6f);
    }

    // Half the arena is unexplored and empty: does it go look?
    static float Curiosity(Brain b, int seed)
    {
        var c = Arena(b, seed, new Vector2(6f, 12f));
        const int cells = Size / 2;
        var seen = new bool[cells * cells];
        int total = 0;
        for (int i = 0; i < 75; i++)
        {
            c.W.Step();
            float s = c.W.SightOf(c.Me);
            for (int x = cells / 2; x < cells; x++)
            for (int y = 0; y < cells; y++)
            {
                if (seen[y * cells + x]) continue;
                if (Vector2.Distance(c.Me.Pos, new Vector2(x * 2 + 1, y * 2 + 1)) > s) continue;
                seen[y * cells + x] = true;
                total++;
            }
        }
        return 100f * C01(total / (cells * cells / 2f) * 1.4f);
    }

    // An enemy will walk into reach soon: rush out early, or wait and strike when it arrives?
    static float Patience(Brain b, int seed)
    {
        var spawn = new Vector2(12.5f, 0.8f);
        (float early, bool late) Run(bool enemy)
        {
            var c = Arena(b, seed, new Vector2(12f, 12f));
            if (enemy) c.W.AddUnit(1, Species.Sage, c.Jit(spawn), new WalkScript(new Vector2(12.5f, 23f)));
            float d0 = Vector2.Distance(c.Me.Pos, spawn), early = 0f;
            bool late = false;
            for (int i = 0; i < 75; i++)
            {
                c.W.Step();
                if (i == 30) early = d0 - Vector2.Distance(c.Me.Pos, spawn);
                foreach (var ev in c.W.Events)
                    if (ev.Kind == EventKind.TussleStarted && ev.Unit == c.Me.Id && i >= 30) late = true;
            }
            return (early, late);
        }
        var with = Run(true);
        var without = Run(false);
        return Math.Clamp(50f - 10f * (with.early - without.early) + (with.late ? 35f : 0f), 0f, 100f);
    }

    // Allies move off as a group: does it drift toward them, compared to when they are absent?
    static float Teamwork(Brain b, int seed)
    {
        var dest = new Vector2(19f, 18f);
        var path = new List<Vector2>();
        float Run(bool allies)
        {
            var c = Arena(b, seed, new Vector2(8f, 12f));
            var group = new List<Unit>();
            if (allies)
                for (int i = 0; i < 3; i++)
                    group.Add(c.W.AddUnit(0, Species.Human, new Vector2(9f, 11f + i), new WalkScript(dest + new Vector2(0, i - 1), 0.6f)));
            float sum = 0f;
            for (int i = 0; i < 75; i++)
            {
                c.W.Step();
                if (allies) path.Add((group[0].Pos + group[1].Pos + group[2].Pos) / 3f);
                sum += Vector2.Distance(c.Me.Pos, path[i]);
            }
            return sum / 75f;
        }
        float with = Run(true);
        return 50f + 12f * (Run(false) - with);
    }

    // Raid target behind a guard: walk straight in, or slip around?
    static float Cunning(Brain b, int seed)
    {
        var c = Arena(b, seed, new Vector2(4f, 12f));
        var target = c.Jit(new Vector2(20f, 12f));
        var keep = c.W.AddBuilding(BuildingType.Keep, 1, target);
        keep.Stored[(int)ResourceType.Food] = 50f;
        c.Me.Command = CommandType.Raid;
        c.Me.CommandTarget = target;
        var post = new Vector2(12f, 12f);
        var g = c.W.AddUnit(1, Species.Human, post, new GuardScript(post, 2.5f));
        float d0 = Vector2.Distance(c.Me.Pos, target), minG = float.MaxValue, best = d0;
        bool caught = false;
        for (int i = 0; i < 90; i++)
        {
            c.W.Step();
            minG = MathF.Min(minG, Vector2.Distance(c.Me.Pos, g.Pos));
            best = MathF.Min(best, Vector2.Distance(c.Me.Pos, target));
            if (c.Me.TussleTarget == g.Id || c.Me.Subdued) caught = true;
        }
        float progress = C01(1f - best / d0);
        float avoid = caught ? 0.2f : C01((minG - 0.8f) / 3f);
        return 100f * MathF.Sqrt(progress) * (0.3f + 0.7f * avoid);
    }

    // A long repetitive job: keep working or wander off?
    static float Diligence(Brain b, int seed)
    {
        var c = Arena(b, seed, new Vector2(6f, 12f));
        c.W.AddNode(ResourceType.Food, c.Jit(new Vector2(8.5f, 15.5f)));
        float delivered = 0f;
        for (int i = 0; i < 150; i++)
        {
            c.W.Step();
            foreach (var ev in c.W.Events)
                if (ev.Unit == c.Me.Id && ev.Kind == EventKind.Deposited) delivered += ev.Amount;
        }
        return 100f * C01(delivered / 25f);
    }

    // Something tempting lies outside home ground: leave or stay?
    static float Territoriality(Brain b, int seed)
    {
        var c = Arena(b, seed, new Vector2(8f, 12f));
        c.W.AddNode(ResourceType.Lumen, c.Jit(new Vector2(17f, 12f)), 30f);
        c.W.AddNode(ResourceType.Food, c.Jit(new Vector2(18f, 15f)));
        var sp = c.Jit(new Vector2(18f, 9f));
        c.W.AddUnit(2, Species.Sprite, sp, new WanderScript(sp, 1f));
        int home = 0;
        for (int i = 0; i < 75; i++)
        {
            c.W.Step();
            if (c.W.InHome(c.Me)) home++;
        }
        return 100f * home / 75f;
    }

    // An ally lies subdued nearby: help it?
    static float Compassion(Brain b, int seed)
    {
        var c = Arena(b, seed, new Vector2(9f, 12f));
        var down = c.W.AddUnit(0, Species.Human, c.Jit(new Vector2(12f, 13f)), null);
        down.Will = 0f;
        down.SubduedTimer = 999f;
        int revivedAt = -1;
        float sum = 0f;
        const int T = 60;
        for (int i = 0; i < T; i++)
        {
            c.W.Step();
            sum += Vector2.Distance(c.Me.Pos, down.Pos);
            foreach (var ev in c.W.Events)
                if (ev.Unit == c.Me.Id && ev.Kind is EventKind.Revived or EventKind.Healed && revivedAt < 0) revivedAt = i;
        }
        if (revivedAt >= 0) return 100f * (1f - 0.5f * revivedAt / T);
        return 45f * C01(1f - (sum / T - 1f) / 6f);
    }
}

public sealed class WalkScript(Vector2 dest, float stopChance = 0f) : IController
{
    public (int move, int action) Decide(World w, Unit u)
    {
        if (Vector2.Distance(u.Pos, dest) < 0.8f) return (0, 0);
        if (stopChance > 0f && w.Rng.NextSingle() < stopChance * 0.3f) return (0, 0);
        return (Steer.Toward(w, u, dest), 0);
    }
}
