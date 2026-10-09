using System.Numerics;
using I = FusionKingdoms.Sim.BrainSpec.In;

namespace FusionKingdoms.Sim;

public static class Observer
{
    public static void Build(World w, Unit u, Span<float> obs)
    {
        obs.Clear();
        float sight = w.SightOf(u);
        var p = u.Pos;

        Span<float> nodeD = stackalloc float[5];
        Span<int> nodeI = stackalloc int[5];
        nodeD.Fill(float.MaxValue);
        nodeI.Fill(-1);
        for (int i = 0; i < w.Nodes.Count; i++)
        {
            var n = w.Nodes[i];
            if (n.Amount <= 0f) continue;
            float d = Vector2.Distance(p, n.Pos);
            int t = (int)n.Type;
            if (d <= sight && d < nodeD[t]) { nodeD[t] = d; nodeI[t] = i; }
        }
        PutNode(w, obs, I.Food, p, nodeI[(int)ResourceType.Food], sight);
        PutNode(w, obs, I.Timber, p, nodeI[(int)ResourceType.Timber], sight);
        PutNode(w, obs, I.Stone, p, nodeI[(int)ResourceType.Stone], sight);
        PutNode(w, obs, I.Lumen, p, nodeI[(int)ResourceType.Lumen], sight);

        Unit? ally = null, enemy = null, wild = null, downAlly = null, downHostile = null;
        float dA = float.MaxValue, dE = float.MaxValue, dW = float.MaxValue, dDA = float.MaxValue, dDH = float.MaxValue;
        int nA = 0, nE = 0, nW = 0;
        foreach (var o in w.Units)
        {
            if (o == u || !o.Active || !w.CanSee(u, o, sight)) continue;
            float d = Vector2.Distance(p, o.Pos);
            if (o.Team == u.Team)
            {
                if (o.Subdued) { if (d < dDA) { dDA = d; downAlly = o; } continue; }
                nA++;
                if (d < dA) { dA = d; ally = o; }
            }
            else
            {
                if (o.Subdued)
                {
                    if (o.CarriedBy < 0 && d < dDH) { dDH = d; downHostile = o; }
                    continue;
                }
                if (o.Team == 2) { nW++; if (d < dW) { dW = d; wild = o; } }
                else { nE++; if (d < dE) { dE = d; enemy = o; } }
            }
        }
        if (ally != null) PutProx(obs, I.Ally, ally.Pos - p, sight);
        if (enemy != null) PutProx(obs, I.Enemy, enemy.Pos - p, sight);
        if (wild != null) PutProx(obs, I.Wild, wild.Pos - p, sight);
        if (downAlly != null) PutProx(obs, I.SubduedAlly, downAlly.Pos - p, sight);
        if (downHostile != null) PutProx(obs, I.SubduedHostile, downHostile.Pos - p, sight);
        obs[I.AllyCount] = MathF.Min(1f, nA / 5f);
        obs[I.EnemyCount] = MathF.Min(1f, nE / 5f);
        obs[I.WildCount] = MathF.Min(1f, nW / 5f);

        Building? eb = null, site = null;
        float dEB = float.MaxValue, dS = float.MaxValue;
        foreach (var b in w.Buildings)
        {
            float d = Vector2.Distance(p, b.Pos);
            // "Enemy building" means an enemy treasury (Keep or den), as in training where it was the only one.
            if (b.Team != u.Team) { if ((b.Type is BuildingType.Keep or BuildingType.Den) && d <= sight && d < dEB) { dEB = d; eb = b; } }
            else if (b.Type == BuildingType.BuildSite && d < dS) { dS = d; site = b; }
        }
        if (eb != null) PutProx(obs, I.EnemyBuilding, eb.Pos - p, sight);
        if (site != null) PutProx(obs, I.BuildSite, site.Pos - p, Rules.OwnBuildingRange);

        var keep = w.HomeOf(u);
        if (keep != null) PutRoute(w, obs, I.HomeDir, I.HomeDist, p, keep.Pos);
        var ruler = w.RulerOf(u.Team);
        if (ruler != null && ruler != u) PutRoute(w, obs, I.RulerDir, I.RulerDist, p, ruler.Pos);
        if (u.Command != CommandType.None)
        {
            PutRoute(w, obs, I.CommandDir, I.CommandDist, p, u.CommandTarget);
            obs[u.Command == CommandType.Raid ? I.CmdRaid : I.CmdGuide] = 1f;
        }

        obs[I.Will] = u.Will / u.Body.Will;
        obs[I.CarryLoad] = u.CarryAmount / u.Carry;
        obs[I.CarryingUnit] = u.CarryingUnit >= 0 ? 1f : 0f;
        obs[I.InHome] = w.InHome(u) ? 1f : 0f;
        obs[I.InTussle] = u.TussleTarget >= 0 ? 1f : 0f;
        float phase = w.Time % Rules.DayCycle / Rules.DayCycle * MathF.Tau;
        obs[I.TimeSin] = MathF.Sin(phase);
        obs[I.TimeCos] = MathF.Cos(phase);
        obs[I.Stance] = u.Stance;
        if (u.CarryAmount > 0f)
        {
            if (u.CarryType == ResourceType.Food) obs[I.CarryFood] = 1f;
            else if (u.CarryType == ResourceType.Lumen) obs[I.CarryLumen] = 1f;
            else obs[I.CarryMaterial] = 1f;
        }
    }

    // On big maps (with a navigator) the direction follows the walkable path and the distance is capped at what
    // brains met in training, so a far target reads like a far-but-reachable one, not like an unreachable void.
    const float RouteDistCap = 24f;

    static void PutRoute(World w, Span<float> obs, int dirIdx, int distIdx, Vector2 from, Vector2 goal)
    {
        if (w.Nav == null) { PutFar(obs, dirIdx, distIdx, goal - from); return; }
        var dir = w.Nav.Waypoint(from, goal) - from;
        float len = dir.Length();
        if (len > 1e-4f) { obs[dirIdx] = dir.X / len; obs[dirIdx + 1] = dir.Y / len; }
        obs[distIdx] = MathF.Min(Vector2.Distance(from, goal), RouteDistCap) / Rules.FarRange;
    }

    static void PutNode(World w, Span<float> obs, int idx, Vector2 p, int node, float range)
    {
        if (node >= 0) PutProx(obs, idx, w.Nodes[node].Pos - p, range);
    }

    // Direction scaled by closeness: length ~1 when adjacent, 0 at the edge of range.
    static void PutProx(Span<float> obs, int idx, Vector2 delta, float range)
    {
        float d = delta.Length();
        if (d > range) return;
        if (d < 1e-4f) { obs[idx] = 0f; obs[idx + 1] = 0f; return; }
        var v = delta / d * (1f - d / range * 0.9f);
        obs[idx] = v.X;
        obs[idx + 1] = v.Y;
    }

    static void PutFar(Span<float> obs, int dirIdx, int distIdx, Vector2 delta)
    {
        float d = delta.Length();
        if (d > 1e-4f) { obs[dirIdx] = delta.X / d; obs[dirIdx + 1] = delta.Y / d; }
        obs[distIdx] = MathF.Min(1f, d / Rules.FarRange);
    }
}
