using System.Numerics;

namespace FusionKingdoms.Sim;

public enum CmdType : byte
{
    Order, Station, Stance, MoveRuler, PlaceSite, Rush, AgeUp, FeedSpire, Produce,
    Foresee, Fuse, StartDrill, FinishDrill, Mutate, ChooseMutant,
    Interrogate, Extract, Distill, Convert, Ransom, ForeseeInfusion, Infuse, BeginRoyal, Trade, BuyMerchant,
    Give, FreeWill, SetRally, CancelProduce, SetRepeat, Research, Upgrade,
}

// Every player action is a command, so the same code path serves single-player and the LAN host.
// Minds are referenced by their id on the host; units and buildings by their id in the world.
public sealed class Command
{
    public CmdType Type;
    public int[] Units = [];
    public int Target = -1;
    public float X, Y, F;
    public int I;
    public bool B;
    public string S = "";

    public void Write(BinaryWriter w)
    {
        w.Write((byte)Type);
        w.Write(Units.Length);
        foreach (var u in Units) w.Write(u);
        w.Write(Target); w.Write(X); w.Write(Y); w.Write(F); w.Write(I); w.Write(B); w.Write(S);
    }

    public static Command Read(BinaryReader r)
    {
        var c = new Command { Type = (CmdType)r.ReadByte() };
        int n = r.ReadInt32();
        c.Units = new int[n];
        for (int i = 0; i < n; i++) c.Units[i] = r.ReadInt32();
        c.Target = r.ReadInt32(); c.X = r.ReadSingle(); c.Y = r.ReadSingle(); c.F = r.ReadSingle();
        c.I = r.ReadInt32(); c.B = r.ReadBoolean(); c.S = r.ReadString();
        return c;
    }

    public byte[] ToBytes()
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms)) Write(w);
        return ms.ToArray();
    }

    public static Command FromBytes(byte[] data)
    {
        using var r = new BinaryReader(new MemoryStream(data));
        return Read(r);
    }
}

public sealed record CommandResult(string? Error = null, Mind? Mind = null, string? Info = null);

public static class Executor
{
    // StartDrill with I >= this is a Mirror Drill; I - MirrorDrillBase is the TapeMode.
    public const int MirrorDrillBase = 100;

    // Minds a kingdom has foreseen but not yet committed, so the commit uses exactly what was previewed.
    static readonly Dictionary<int, Mind> Foreseen = new();

    public static Mind? FindMind(Match m, Kingdom k, int id)
    {
        lock (Foreseen) if (Foreseen.TryGetValue(id, out var f)) return f;
        return k.Library.FirstOrDefault(x => x.Id == id);
    }

    static void Remember(Mind? m)
    {
        if (m == null) return;
        lock (Foreseen)
        {
            Foreseen[m.Id] = m;
            if (Foreseen.Count > 64) Foreseen.Remove(Foreseen.Keys.First());
        }
    }

    public static CommandResult Run(Match m, Kingdom k, Command c)
    {
        Unit? U(int i) => i >= 0 && i < m.W.Units.Count ? m.W.Units[i] : null;
        Building? B(int i) => i >= 0 && i < m.W.Buildings.Count ? m.W.Buildings[i] : null;
        var units = c.Units.Select(U).Where(u => u != null && u.Team == k.Team).Cast<Unit>().ToList();
        var first = units.FirstOrDefault();
        var p = new Vector2(c.X, c.Y);
        Captive? Cap() => k.Captives.FirstOrDefault(x => x.Unit.Id == c.Target);

        switch (c.Type)
        {
            case CmdType.Order: m.Order(units, (CommandType)c.I, p); return new();
            case CmdType.Station:
                var camp = B(c.Target);
                m.Station(units, camp != null && camp.Team == k.Team && camp.Type == BuildingType.Camp ? camp : null);
                return new();
            case CmdType.Stance: m.SetStance(units, c.F); return new();
            case CmdType.Give: return new(m.Give(k, units, (OrderKind)c.I, p, c.Target, (ResourceType)(int)c.F));
            case CmdType.FreeWill: foreach (var u in units) u.FreeWill = c.B; return new();
            case CmdType.MoveRuler: m.MoveRuler(k, p); return new();
            case CmdType.PlaceSite:
                var type = (BuildingType)c.I;
                var err = m.CanPlace(k, type, p);
                if (err != null) return new(err);
                m.PlaceSite(k, type, p);
                return new();
            case CmdType.Rush: return B(c.Target) is { } site && m.Rush(k, site) ? new() : new("Not enough timber and stone");
            case CmdType.AgeUp: return m.AgeUp(k) ? new() : new("Not enough resources");
            case CmdType.FeedSpire: return m.FeedSpire(k) ? new() : new("No working Spire or no Lumen");
            case CmdType.SetRally:
                k.Rally = c.B ? p : null;
                if (c.I >= 0) k.Job = (NewbornJob)c.I;
                return new();
            case CmdType.Research: return new(m.StartResearch(k, c.S));
            case CmdType.Upgrade: return B(c.Target) is { } ub ? new(m.Upgrade(k, ub)) : new("Pick a building");
            case CmdType.CancelProduce: return m.CancelProduce(k, c.I) ? new() : new("Nothing to cancel");
            case CmdType.SetRepeat:
                k.Repeat = c.I < 0 ? null : FindMind(m, k, c.I);
                return new();
            case CmdType.Produce:
                return FindMind(m, k, c.I) is { } pm ? new(m.Produce(k, pm)) : new("Unknown mind");
            case CmdType.Foresee:
            {
                if (first == null || U(c.Target) is not { } b) return new("Pick two subjects");
                var seen = m.Foresee(k, first, b, c.F);
                Remember(seen);
                return seen == null ? new("Can't foresee: pick two different subjects and have enough Lumen") : new(null, seen);
            }
            case CmdType.Fuse:
            {
                if (first == null || U(c.Target) is not { } b) return new("Pick two subjects");
                Mind? seen = null;
                if (c.I >= 0) lock (Foreseen) Foreseen.TryGetValue(c.I, out seen);
                var cost = first.Mind != null && b.Mind != null ? m.FusionCost(k, first.Mind, b.Mind) : 0f;
                var made = m.Fuse(k, first, b, c.F, c.B, seen);
                return made == null ? new($"Fusion needs two different subjects and {cost:0} Lumen") : new(null, made.Mind, $"{made.Mind!.Name} walks out of the Altar!");
            }
            case CmdType.StartDrill:
                if (first == null) return new("Pick a subject");
                if (c.I >= MirrorDrillBase) return new(m.StartMirrorDrill(k, first, (TapeMode)(c.I - MirrorDrillBase)));
                return new(m.StartDrill(k, first, (MindKind)c.I, c.S));
            case CmdType.FinishDrill:
            {
                var job = k.Training.FirstOrDefault(j => j.Unit.Id == c.Target);
                if (job == null) return new("No such drill");
                var made = m.FinishDrill(k, job);
                return new(null, made, $"{made.Name}: {string.Join(", ", made.Traits.Select(t => t.Name))}");
            }
            case CmdType.Mutate:
                return first == null ? new("Pick a subject") : m.Mutate(k, first, c.I) == null ? new("Not enough Lumen, or the Pit isn't ready") : new();
            case CmdType.ChooseMutant:
            {
                if (U(c.Target) is not { } u || u.Team != k.Team || !m.PitChoices.TryGetValue(u.Id, out var choices)) return new("Nothing to choose");
                var pick = choices.FirstOrDefault(x => x.Id == c.I);
                if (pick == null) return new("Unknown mutant");
                m.ChooseMutant(k, u, pick);
                return new(null, pick);
            }
            case CmdType.Interrogate: return Cap() is { } c1 ? new(null, m.Interrogate(k, c1) != null ? c1.Mind : null) : new("No such captive");
            case CmdType.Extract: return Cap() is { } c2 ? new(m.Extract(k, c2), null, $"{c2.Mind?.Name} added to your Library") : new("No such captive");
            case CmdType.Distill:
                return Cap() is { } c3 ? (m.Distill(k, c3) is { } de ? new(de) : new(null, null, $"Distilled {k.Essences[^1].Name}")) : new("No such captive");
            case CmdType.Convert: return Cap() is { } c4 ? new(m.StartConvert(k, c4)) : new("No such captive");
            case CmdType.Ransom:
                if (Cap() is not { } c5) return new("No such captive");
                m.Ransom(k, c5);
                return new();
            case CmdType.ForeseeInfusion:
            {
                if (first == null || c.I < 0 || c.I >= k.Essences.Count) return new("Pick a subject and an Essence");
                var seen = m.ForeseeInfusion(k, first, k.Essences[c.I], c.F);
                Remember(seen);
                return seen == null ? new(m.InfuseCheck(k, first, c.F) ?? "Not enough Lumen") : new(null, seen);
            }
            case CmdType.Infuse:
            {
                if (first == null || c.I < 0 || c.I >= k.Essences.Count) return new("Pick a subject and an Essence");
                Mind? seen = null;
                if (c.Target >= 0) lock (Foreseen) Foreseen.TryGetValue(c.Target, out seen);
                var made = m.Infuse(k, first, k.Essences[c.I], c.F, seen);
                return made == null ? new(m.InfuseCheck(k, first, c.F) ?? "Not enough Lumen")
                    : new(null, made, $"{made.Name}: {string.Join(", ", made.Traits.Select(t => t.Name))}");
            }
            case CmdType.Trade: return new(m.Trade(k, c.I, Math.Max(1, (int)c.F)));
            case CmdType.BuyMerchant: return new(m.BuyFromMerchant(k));
            case CmdType.BeginRoyal:
                return first != null && m.BeginRoyal(k, first) ? new() : new($"Royal Command needs {Defs.RoyalCommandCost:0} Lumen and a free subject");
        }
        return new("Unknown command");
    }
}
