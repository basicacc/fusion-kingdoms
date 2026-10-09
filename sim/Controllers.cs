using System.Numerics;

namespace FusionKingdoms.Sim;

public interface IController
{
    (int move, int action) Decide(World w, Unit u);
}

public sealed class BrainController : IController
{
    public Brain Brain;
    public bool Greedy;
    readonly float[] _obs = new float[BrainSpec.In.Count];

    public BrainController(Brain brain, bool greedy = false) { Brain = brain; Greedy = greedy; }

    public (int move, int action) Decide(World w, Unit u)
    {
        Observer.Build(w, u, _obs);
        return Brain.Decide(_obs, w.Rng, Greedy);
    }
}

// Actions are written in from outside (Python training) before each World.Step.
public sealed class ExternalController : IController
{
    public int Move, Action;
    public (int move, int action) Decide(World w, Unit u) => (Move, Action);
}

// Hand-written behaviors used only as training partners and opponents, never as player subjects.
public static class Steer
{
    public static int Toward(World w, Unit u, Vector2 target) => Best(w, u, target - u.Pos);
    public static int Away(World w, Unit u, Vector2 from) => Best(w, u, u.Pos - from);

    static int Best(World w, Unit u, Vector2 want)
    {
        if (want.LengthSquared() < 1e-6f) return 0;
        want = Vector2.Normalize(want);
        int best = 0;
        float bestScore = -2f;
        for (int m = 1; m <= 8; m++)
        {
            var dir = World.MoveDir(m);
            if (w.Blocked(u, u.Pos + dir * 0.8f)) continue;
            float s = Vector2.Dot(dir, want);
            if (s > bestScore) { bestScore = s; best = m; }
        }
        return best;
    }

    public static Unit? NearestHostile(World w, Unit u, float range, bool wantSubdued)
    {
        Unit? best = null;
        float bestD = range, sight = w.SightOf(u);
        foreach (var o in w.Units)
        {
            if (!o.Active || !World.Hostile(u.Team, o.Team) || o.CarriedBy >= 0) continue;
            if (o.Subdued != wantSubdued || !w.CanSee(u, o, sight)) continue;
            float d = Vector2.Distance(u.Pos, o.Pos);
            if (d <= bestD) { best = o; bestD = d; }
        }
        return best;
    }

    public static ResourceNode? NearestNode(World w, Vector2 p, ResourceType type)
    {
        ResourceNode? best = null;
        float bestD = float.MaxValue;
        foreach (var n in w.Nodes)
        {
            if (n.Type != type || n.Amount <= 0f) continue;
            float d = Vector2.Distance(p, n.Pos);
            if (d < bestD) { best = n; bestD = d; }
        }
        return best;
    }

    public static bool Near(Unit u, Vector2 p, float r) => Vector2.Distance(u.Pos, p) <= r;
}

public sealed class WanderScript(Vector2 anchor, float radius) : IController
{
    int _dir;
    float _timer;

    public (int move, int action) Decide(World w, Unit u)
    {
        if (u.TussleTarget >= 0) return (0, (int)Act.Tussle);
        if (Vector2.Distance(u.Pos, anchor) > radius) return (Steer.Toward(w, u, anchor), 0);
        _timer -= Rules.Dt;
        if (_timer <= 0f)
        {
            _timer = 1f + w.Rng.NextSingle() * 2f;
            _dir = w.Rng.Next(3) == 0 ? 0 : w.Rng.Next(1, 9);
        }
        return (_dir, 0);
    }
}

public sealed class WorkerScript(ResourceType type) : IController
{
    public (int move, int action) Decide(World w, Unit u)
    {
        var keep = w.KeepOf(u.Team);
        if (keep == null) return (0, 0);
        if (u.CarryAmount >= u.Carry - 0.01f)
        {
            if (Vector2.Distance(u.Pos, keep.Pos) <= keep.Radius + Rules.InteractRange) return (0, (int)Act.Deposit);
            return (Steer.Toward(w, u, keep.Pos), 0);
        }
        var n = Steer.NearestNode(w, u.Pos, type);
        if (n == null) return (0, 0);
        if (Steer.Near(u, n.Pos, Rules.InteractRange)) return (0, (int)Act.Gather);
        return (Steer.Toward(w, u, n.Pos), 0);
    }
}

public sealed class GuardScript(Vector2 post, float aggro) : IController
{
    public (int move, int action) Decide(World w, Unit u)
    {
        if (u.CarryingUnit >= 0)
        {
            var keep = w.KeepOf(u.Team)!;
            if (Vector2.Distance(u.Pos, keep.Pos) <= keep.Radius + Rules.InteractRange) return (0, (int)Act.Deposit);
            return (Steer.Toward(w, u, keep.Pos), 0);
        }
        if (u.TussleTarget >= 0) return (0, (int)Act.Tussle);
        var down = Steer.NearestHostile(w, u, 4f, true);
        if (down != null && Vector2.Distance(down.Pos, post) < aggro + 2f)
            return Steer.Near(u, down.Pos, Rules.InteractRange) ? (0, (int)Act.Carry) : (Steer.Toward(w, u, down.Pos), 0);
        var t = Steer.NearestHostile(w, u, aggro + 2f, false);
        if (t != null && Vector2.Distance(t.Pos, post) < aggro)
            return Steer.Near(u, t.Pos, Rules.InteractRange) ? (0, (int)Act.Tussle) : (Steer.Toward(w, u, t.Pos), 0);
        return Steer.Near(u, post, 1f) ? (0, 0) : (Steer.Toward(w, u, post), 0);
    }
}

public sealed class RaiderScript(int targetTeam, float boldness) : IController
{
    public (int move, int action) Decide(World w, Unit u)
    {
        var home = w.KeepOf(u.Team);
        var target = w.KeepOf(targetTeam);
        if (home == null || target == null) return (0, 0);
        if (u.TussleTarget >= 0) return w.Rng.NextSingle() < boldness ? (0, (int)Act.Tussle) : (Steer.Toward(w, u, home.Pos), 0);
        if (boldness > 0.5f)
        {
            var t = Steer.NearestHostile(w, u, Rules.InteractRange, false);
            if (t != null) return (0, (int)Act.Tussle);
        }
        if (u.CarryAmount >= u.Carry - 0.01f)
        {
            if (Vector2.Distance(u.Pos, home.Pos) <= home.Radius + Rules.InteractRange) return (0, (int)Act.Deposit);
            return (Steer.Toward(w, u, home.Pos), 0);
        }
        if (Vector2.Distance(u.Pos, target.Pos) <= target.Radius + Rules.InteractRange) return (0, (int)Act.Steal);
        return (Steer.Toward(w, u, target.Pos), 0);
    }
}

public sealed class HunterScript(float chase) : IController
{
    readonly WanderScript _idle = new(Vector2.Zero, float.MaxValue);

    public (int move, int action) Decide(World w, Unit u)
    {
        if (u.CarryingUnit >= 0)
        {
            var keep = w.KeepOf(u.Team);
            if (keep == null) return (0, 0);
            if (Vector2.Distance(u.Pos, keep.Pos) <= keep.Radius + Rules.InteractRange) return (0, (int)Act.Deposit);
            return (Steer.Toward(w, u, keep.Pos), 0);
        }
        if (u.TussleTarget >= 0) return (0, (int)Act.Tussle);
        var down = Steer.NearestHostile(w, u, chase, true);
        if (down != null && w.KeepOf(u.Team) != null)
            return Steer.Near(u, down.Pos, Rules.InteractRange) ? (0, (int)Act.Carry) : (Steer.Toward(w, u, down.Pos), 0);
        var t = Steer.NearestHostile(w, u, chase, false);
        if (t != null)
            return Steer.Near(u, t.Pos, Rules.InteractRange) ? (0, (int)Act.Tussle) : (Steer.Toward(w, u, t.Pos), 0);
        return _idle.Decide(w, u);
    }
}

public sealed class SpriteScript(Vector2 anchor) : IController
{
    readonly WanderScript _wander = new(anchor, 6f);

    public (int move, int action) Decide(World w, Unit u)
    {
        var t = Steer.NearestHostile(w, u, 4f, false);
        if (t != null) return (Steer.Away(w, u, t.Pos), 0);
        return _wander.Decide(w, u);
    }
}

public sealed class TrollScript(Vector2 anchor) : IController
{
    readonly WanderScript _wander = new(anchor, 3f);

    public (int move, int action) Decide(World w, Unit u)
    {
        if (u.TussleTarget >= 0) return (0, (int)Act.Tussle);
        var t = Steer.NearestHostile(w, u, 4f, false);
        if (t != null && Vector2.Distance(t.Pos, anchor) < 5f)
            return Steer.Near(u, t.Pos, Rules.InteractRange) ? (0, (int)Act.Tussle) : (Steer.Toward(w, u, t.Pos), 0);
        return _wander.Decide(w, u);
    }
}
