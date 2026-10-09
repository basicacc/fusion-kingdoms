using System.Numerics;

namespace FusionKingdoms.Sim;

// Carries out a subject's order exactly, RTS-style: walk the real path, gather and haul in a loop, build, capture,
// raid, guard. The subject's own brain still matters: it sets how well the work goes (through its class stats),
// decides fight-or-flee when a worker is jumped, and runs the subject whenever it has no order and Free Will is on.
public sealed class OrderController(IController brain) : IController
{
    public IController Brain = brain;
    const float Reach = 1.0f;
    int _stuck, _noProgress;
    Vector2 _lastPos, _goal;
    float _bestD = float.MaxValue;

    public (int move, int action) Decide(World w, Unit u)
    {
        if (u.Order == OrderKind.None)
        {
            if (!u.FreeWill) return (0, u.TussleTarget >= 0 ? (int)Act.Tussle : 0);
            // Free Will: every few seconds an idle subject looks for work that suits its class.
            if (u.TussleTarget < 0 && (w.StepCount + u.Id) % 20 == 0) Initiative(w, u);
            if (u.Order == OrderKind.None) return Brain.Decide(w, u);
        }

        // Jumped while working: the mind decides whether to fight back or slip away.
        if (u.TussleTarget >= 0 && u.Order is OrderKind.Gather or OrderKind.Build or OrderKind.Move or OrderKind.Hold or OrderKind.Sabotage or OrderKind.Raid)
            return Brain.Decide(w, u);

        var r = Execute(w, u);
        // Stuck against an enemy wall or gate: break it down.
        if (r.Item2 == 0 && (_stuck > 8 || _noProgress > 25) && w.Walls.Length > 0)
            foreach (var b in w.Buildings)
                if (b.Team != u.Team && b.Team < 3 && Match.IsWall(b.Type) && Vector2.Distance(b.Pos, u.Pos) < 1.6f)
                    return (0, (int)Act.Sabotage);
        return r;
    }

    (int, int) Execute(World w, Unit u)
    {
        return u.Order switch
        {
            OrderKind.Move => MoveTo(w, u),
            OrderKind.Hold => (0, u.TussleTarget >= 0 ? (int)Act.Tussle : HitNearby(w, u, 1.5f)),
            OrderKind.Gather => Gather(w, u),
            OrderKind.Build => Build(w, u),
            OrderKind.Capture => Capture(w, u),
            OrderKind.Raid => Raid(w, u),
            OrderKind.Sabotage => Sabotage(w, u),
            OrderKind.Guard => Guard(w, u),
            OrderKind.Heal => Heal(w, u),
            _ => (0, 0),
        };
    }

    // ---------- initiative ----------

    // What a free-willed subject does with itself: the job its class is best at, where it's needed most.
    static void Initiative(World w, Unit u)
    {
        if (u.Team > 1 || u.IsRuler || u.CarryingUnit >= 0) return;
        var home = w.KeepOf(u.Team);
        if (home == null) return;
        var c = Classes.Of(u.Mind);
        int[] roles = c.Pure ? [c.A] : [c.A, c.B];
        foreach (var role in roles)
        {
            switch (role)
            {
                case 0: if (TakeCrop(w, u, home.Pos)) return; break;                                 // Farmer
                case 1:                                                                              // Builder
                {
                    var site = w.Buildings.Where(b => b.Team == u.Team && b.Type == BuildingType.BuildSite)
                        .OrderBy(b => Vector2.DistanceSquared(b.Pos, u.Pos)).FirstOrDefault();
                    if (site != null && Vector2.Distance(site.Pos, u.Pos) < 30f) { Give(u, OrderKind.Build, site.Pos, site.Id, ResourceType.None); return; }
                    if (TakeNode(w, u, u.Id % 2 == 0 ? ResourceType.Timber : ResourceType.Stone, home.Pos)) return;
                    break;
                }
                case 2: case 5:                                                                      // Guard, Hunter
                {
                    // Chase off anything hostile near home, else stand watch between home and the enemy.
                    var foe = w.Units.Where(o => o.Active && o.Team != u.Team && !o.Subdued && o.CarriedBy < 0
                                                 && Vector2.Distance(o.Pos, home.Pos) < 14f).OrderBy(o => Vector2.Distance(o.Pos, u.Pos)).FirstOrDefault();
                    if (foe != null) { Give(u, OrderKind.Capture, foe.Pos, foe.Id, ResourceType.None); return; }
                    var enemy = w.KeepOf(1 - u.Team);
                    var post = enemy == null ? home.Pos : home.Pos + Vector2.Normalize(enemy.Pos - home.Pos) * 6f;
                    Give(u, OrderKind.Guard, post + new Vector2((u.Id % 5) - 2, (u.Id / 5 % 3) - 1), -1, ResourceType.None);
                    return;
                }
                case 7: if (TakeNode(w, u, ResourceType.Lumen, home.Pos, 999f)) return; break;     // Hoarder
                case 6:                                                                              // Healer
                {
                    var patient = Patient(w, u, 18f);
                    if (patient != null) { Give(u, OrderKind.Heal, patient.Pos, patient.Id, ResourceType.None); return; }
                    break;
                }
                case 3:                                                                              // Scout: explore
                {
                    for (int tries = 0; tries < 12; tries++)
                    {
                        float a = (float)(w.Rng.NextDouble() * MathF.Tau), d = 15f + (float)w.Rng.NextDouble() * 20f;
                        var to = u.Pos + new Vector2(MathF.Cos(a), MathF.Sin(a)) * d;
                        if (to.X < 2 || to.Y < 2 || to.X > w.Width - 2 || to.Y > w.Height - 2 || TerrainInfo.Blocked(w.TerrainAt(to))) continue;
                        Give(u, OrderKind.Move, to, -1, ResourceType.None);
                        return;
                    }
                    break;
                }
                case 4: break;                                                                       // Thief: falls back to farming below
            }
        }
        // Nothing specific: farming is honest work for anyone.
        if (!c.Pure || c.A != 3 && c.A != 6) TakeCrop(w, u, home.Pos);
    }

    // An order is finished: after a player's order the subject waits for the next one; its own ideas just end.
    static void Done(Unit u)
    {
        if (!u.OrderFromWill) u.FreeWill = false;
        u.Order = OrderKind.None;
    }

    static void Give(Unit u, OrderKind kind, Vector2 pos, int target, ResourceType res)
    {
        u.OrderFromWill = true;
        u.Order = kind;
        u.OrderPos = pos;
        u.OrderTarget = target;
        u.OrderRes = res;
    }

    // The least crowded crop near home.
    static bool TakeCrop(World w, Unit u, Vector2 home)
    {
        ResourceNode? best = null;
        int bestIdx = -1;
        float bestScore = float.MaxValue;
        for (int i = 0; i < w.Nodes.Count; i++)
        {
            var n = w.Nodes[i];
            if (n.Type != ResourceType.Food || n.Amount <= 0f || Vector2.Distance(n.Pos, home) > 22f) continue;
            int busy = 0;
            foreach (var o in w.Units) if (o.Active && o.Team == u.Team && o.Order == OrderKind.Gather && o.OrderTarget == i) busy++;
            float score = busy * 12f + Vector2.Distance(n.Pos, u.Pos);
            if (busy < 2 && score < bestScore) { bestScore = score; best = n; bestIdx = i; }
        }
        if (best == null) return false;
        Give(u, OrderKind.Gather, best.Pos, bestIdx, ResourceType.Food);
        return true;
    }

    static bool TakeNode(World w, Unit u, ResourceType t, Vector2 home, float range = 40f)
    {
        var n = NearestNode(w, t, home, range);
        if (n == null) return false;
        Give(u, OrderKind.Gather, n.Pos, w.Nodes.IndexOf(n), t);
        return true;
    }

    // The friend nearby who most needs help: the subdued first, then the most worn out (below 70% Will).
    public static Unit? Patient(World w, Unit u, float range)
    {
        Unit? best = null;
        float bestScore = float.MaxValue;
        foreach (var o in w.Units)
        {
            if (o == u || !o.Active || o.Team != u.Team || o.CarriedBy >= 0 && w.Units[o.CarriedBy].Team == u.Team) continue;
            float d = Vector2.Distance(o.Pos, u.Pos);
            if (d > range) continue;
            float need = o.Subdued ? 0f : o.Will < o.Body.Will * 0.7f ? o.Will / o.Body.Will : 99f;
            if (need >= 99f) continue;
            float score = need * 20f + d;
            if (score < bestScore) { bestScore = score; best = o; }
        }
        return best;
    }

    // Go to a hurt or subdued friend and patch them up; then look for the next one close by.
    (int, int) Heal(World w, Unit u)
    {
        var t = u.OrderTarget >= 0 && u.OrderTarget < w.Units.Count ? w.Units[u.OrderTarget] : null;
        bool done = t == null || !t.Active || t.Team != u.Team || (!t.Subdued && t.Will >= t.Body.Will * 0.98f);
        if (done)
        {
            t = Patient(w, u, 10f);
            if (t == null) { u.Order = OrderKind.None; return (0, 0); }
            u.OrderTarget = t.Id;
        }
        if (Vector2.Distance(u.Pos, t!.Pos) <= Rules.InteractRange * 0.9f) return (0, (int)Act.Rescue);
        return (Go(w, u, t.Pos), 0);
    }

    // ---------- moving ----------

    int Go(World w, Unit u, Vector2 goal)
    {
        var wp = w.Nav?.Waypoint(u.Pos, goal, 3f) ?? goal;
        // Progress check: sliding along an obstacle without getting closer counts as stuck too.
        if (Vector2.Distance(goal, _goal) > 1f) { _goal = goal; _bestD = float.MaxValue; _noProgress = 0; }
        float dg = Vector2.Distance(u.Pos, goal);
        if (dg < _bestD - 0.3f) { _bestD = dg; _noProgress = 0; } else _noProgress++;
        // Unstick: if we've barely moved for a while, try the raw direction.
        if (Vector2.DistanceSquared(u.Pos, _lastPos) < 0.0004f) _stuck++;
        else _stuck = 0;
        _lastPos = u.Pos;
        return Steer.Toward(w, u, _stuck > 6 && _stuck % 4 < 2 ? goal : wp);
    }

    (int, int) MoveTo(World w, Unit u)
    {
        float d = Vector2.Distance(u.Pos, u.OrderPos);
        // Arrived, or as close as the ground allows.
        if (d < 0.6f + u.Body.Speed * u.SpeedMul * Rules.Dt * 0.6f || (d < 3f && _stuck > 15)) { Done(u); return (0, 0); }
        return (Go(w, u, u.OrderPos), 0);
    }

    static int HitNearby(World w, Unit u, float range)
    {
        foreach (var o in w.Units)
            if (o.Active && o.Team != u.Team && o.CanAct && Vector2.Distance(o.Pos, u.Pos) <= MathF.Min(range, Rules.InteractRange))
                return (int)Act.Tussle;
        return 0;
    }

    // ---------- working ----------

    public static Building? DropOff(World w, Unit u, ResourceType t)
    {
        Building? best = null;
        float bestD = float.MaxValue;
        foreach (var b in w.Buildings)
        {
            if (b.Team != u.Team || b.DisabledTimer > 0f || !b.IsStore || !World.Accepts(b, t)) continue;
            float d = Vector2.DistanceSquared(u.Pos, b.Pos);
            if (d < bestD) { bestD = d; best = b; }
        }
        return best;
    }

    static bool Near(Unit u, Building b) => Vector2.Distance(u.Pos, b.Pos) <= b.Radius + Rules.InteractRange * 0.9f;

    (int, int) Haul(World w, Unit u)
    {
        var drop = DropOff(w, u, u.CarryType);
        if (drop == null) { u.Order = OrderKind.None; return (0, 0); }
        if (Near(u, drop)) return (0, (int)Act.Deposit);
        return (Go(w, u, drop.Pos), 0);
    }

    (int, int) Gather(World w, Unit u)
    {
        // Full, or holding something else: take it home first.
        bool wrongLoad = u.CarryAmount > 0f && u.CarryType != u.OrderRes;
        var node = u.OrderTarget >= 0 && u.OrderTarget < w.Nodes.Count ? w.Nodes[u.OrderTarget] : null;
        float capacity = u.Carry * (node?.Yield ?? 1f);
        if (u.CarryAmount >= capacity - 0.01f || wrongLoad || u.CarryingUnit >= 0)
            return u.CarryingUnit >= 0 ? (0, 0) : Haul(w, u);

        if (node == null || node.Amount <= 0f || node.Type != u.OrderRes)
        {
            // This one ran dry: find the nearest of the same kind around where we were sent.
            node = NearestNode(w, u.OrderRes, u.OrderPos, 14f);
            if (node == null)
            {
                if (u.CarryAmount > 0f) return Haul(w, u);
                u.Order = OrderKind.None;
                return (0, 0);
            }
            u.OrderTarget = w.Nodes.IndexOf(node);
        }
        if (Vector2.Distance(u.Pos, node.Pos) <= Reach) return (0, (int)Act.Gather);
        return (Go(w, u, node.Pos), 0);
    }

    public static ResourceNode? NearestNode(World w, ResourceType t, Vector2 around, float range)
    {
        ResourceNode? best = null;
        float bestD = range * range;
        foreach (var n in w.Nodes)
        {
            if (n.Type != t || n.Amount <= 0f) continue;
            float d = Vector2.DistanceSquared(n.Pos, around);
            if (d < bestD) { bestD = d; best = n; }
        }
        return best;
    }

    (int, int) Build(World w, Unit u)
    {
        var site = u.OrderTarget >= 0 && u.OrderTarget < w.Buildings.Count ? w.Buildings[u.OrderTarget] : null;
        if (site == null || site.Type != BuildingType.BuildSite || site.Team != u.Team)
        {
            // Done: help with the next unfinished site close by, if any.
            site = w.Buildings.Where(b => b.Type == BuildingType.BuildSite && b.Team == u.Team && Vector2.Distance(b.Pos, u.Pos) < 16f)
                .OrderBy(b => Vector2.DistanceSquared(b.Pos, u.Pos)).FirstOrDefault();
            if (site == null) { Done(u); return (0, 0); }
            u.OrderTarget = site.Id;
        }
        if (Near(u, site)) return (0, (int)Act.Build);
        return (Go(w, u, site.Pos), 0);
    }

    // ---------- conflict ----------

    (int, int) Capture(World w, Unit u)
    {
        if (u.CarryingUnit >= 0)
        {
            var hold = w.Buildings.Where(b => b.Team == u.Team && b.Type is BuildingType.Keep or BuildingType.Dungeon && b.DisabledTimer <= 0f)
                .OrderBy(b => Vector2.DistanceSquared(b.Pos, u.Pos)).FirstOrDefault();
            if (hold == null) return (0, 0);
            if (Near(u, hold)) return (0, (int)Act.Deposit);
            return (Go(w, u, hold.Pos), 0);
        }
        if (u.TussleTarget >= 0) return (0, (int)Act.Tussle);
        var t = u.OrderTarget >= 0 && u.OrderTarget < w.Units.Count ? w.Units[u.OrderTarget] : null;
        // A teammate is already carrying the prize: escort them home and fight off anyone who tries to stop it.
        if (t != null && t.Active && t.CarriedBy >= 0 && w.Units[t.CarriedBy].Team == u.Team)
        {
            int hit = HitNearby(w, u, Rules.InteractRange);
            if (hit != 0) return (0, hit);
            var carrier = w.Units[t.CarriedBy];
            return Vector2.Distance(u.Pos, carrier.Pos) > 2f ? (Go(w, u, carrier.Pos), 0) : (0, 0);
        }
        if (t == null || !t.Active || t.Team == u.Team || (t.CarriedBy >= 0 && t.CarriedBy != u.Id))
        {
            u.Order = OrderKind.None;
            return (0, 0);
        }
        float d = Vector2.Distance(u.Pos, t.Pos);
        if (t.Subdued) return d <= Rules.InteractRange * 0.9f ? (0, (int)Act.Carry) : (Go(w, u, t.Pos), 0);
        return d <= Rules.InteractRange * 0.9f ? (0, (int)Act.Tussle) : (Go(w, u, t.Pos), 0);
    }

    (int, int) Raid(World w, Unit u)
    {
        if (u.CarryAmount >= u.Carry - 0.01f || (u.CarryStolen && u.CarryAmount > 0f && u.LastAction != (int)Act.Steal))
            return Haul(w, u);
        var b = u.OrderTarget >= 0 && u.OrderTarget < w.Buildings.Count ? w.Buildings[u.OrderTarget] : null;
        if (b == null || b.Team == u.Team || !b.IsStore) { u.Order = OrderKind.None; return (0, 0); }
        if (Near(u, b))
        {
            bool empty = true;
            for (int i = 1; i < b.Stored.Length; i++) if (b.Stored[i] >= 0.5f) empty = false;
            if (!empty) return (0, (int)Act.Steal);
            if (u.CarryAmount > 0f) return Haul(w, u);
            u.Order = OrderKind.None;
            return (0, 0);
        }
        return (Go(w, u, b.Pos), 0);
    }

    (int, int) Sabotage(World w, Unit u)
    {
        var b = u.OrderTarget >= 0 && u.OrderTarget < w.Buildings.Count ? w.Buildings[u.OrderTarget] : null;
        if (b == null || b.Team == u.Team) { u.Order = OrderKind.None; return (0, 0); }
        // While it's out of action, wait beside it and hit it again the moment it comes back.
        if (b.DisabledTimer > 0f) return Vector2.Distance(u.Pos, b.Pos) > b.Radius + 2.5f ? (Go(w, u, b.Pos), 0) : (0, HitNearby(w, u, 1.3f));
        if (Near(u, b)) return (0, (int)Act.Sabotage);
        return (Go(w, u, b.Pos), 0);
    }

    // Hold a spot: chase off and catch any enemy that comes within 7 tiles, bring captives home, then return.
    (int, int) Guard(World w, Unit u)
    {
        if (u.CarryingUnit >= 0 || u.TussleTarget >= 0)
        {
            var save = (u.Order, u.OrderTarget);
            if (u.CarryingUnit >= 0)
            {
                var r = Capture(w, u);
                u.Order = save.Order;
                return r;
            }
            return (0, (int)Act.Tussle);
        }
        Unit? foe = null;
        float best = 7f;
        foreach (var o in w.Units)
        {
            if (!o.Active || o.Team == u.Team || o.CarriedBy >= 0) continue;
            float d = Vector2.Distance(o.Pos, u.OrderPos);
            if (d > 7f || !w.CanSee(u, o, w.SightOf(u) + 4f)) continue;
            // Wild creatures only matter if they come close; subdued enemies are worth carrying home.
            if (o.Team == 2 && d > 4f) continue;
            if (d < best) { best = d; foe = o; }
        }
        if (foe != null)
        {
            float d = Vector2.Distance(u.Pos, foe.Pos);
            if (foe.Subdued) return d <= Rules.InteractRange * 0.9f ? (0, (int)Act.Carry) : (Go(w, u, foe.Pos), 0);
            return d <= Rules.InteractRange * 0.9f ? (0, (int)Act.Tussle) : (Go(w, u, foe.Pos), 0);
        }
        if (Vector2.Distance(u.Pos, u.OrderPos) > 1.5f) return (Go(w, u, u.OrderPos), 0);
        return (0, 0);
    }
}

// Wild creatures that live around a den: their own minds run them, but they never stray far from home.
public sealed class LeashController(IController brain, Vector2 den, float radius) : IController
{
    public (int move, int action) Decide(World w, Unit u)
    {
        if (u.TussleTarget < 0 && u.CarryingUnit < 0 && Vector2.Distance(u.Pos, den) > radius)
            return (Steer.Toward(w, u, w.Nav?.Waypoint(u.Pos, den, 3f) ?? den), 0);
        return brain.Decide(w, u);
    }
}
