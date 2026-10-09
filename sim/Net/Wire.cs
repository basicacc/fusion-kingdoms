using System.Numerics;

namespace FusionKingdoms.Sim;

// LAN replication. The host simulates; clients keep a mirror Match (same map seed) that only receives state.
public static class Wire
{
    static void W(BinaryWriter w, Vector2 v) { w.Write(v.X); w.Write(v.Y); }
    static Vector2 V(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle());

    // ---------- minds ----------

    public static void WriteMind(BinaryWriter w, Mind m, bool withWeights)
    {
        w.Write(m.NetId);
        w.Write(m.Name);
        w.Write(m.Generation);
        w.Write(m.Ratio);
        w.Write((byte)m.Vessel);
        w.Write(m.Title ?? "");
        w.Write(m.Drills.Length);
        foreach (var d in m.Drills) w.Write(d);
        w.Write(m.Parents.Length);
        foreach (var p in m.Parents) { w.Write(p.NetId); w.Write(p.Name); }
        foreach (var s in m.Trial.Scores) w.Write(s);
        w.Write(m.Trial.Erratic);
        w.Write(m.Trial.NightDelta);
        w.Write(m.Skill.Length);
        foreach (var s in m.Skill) w.Write(s);
        w.Write(withWeights);
        if (withWeights) foreach (var x in m.Brain.Weights) w.Write(x);
    }

    // Builds the client's copy of a host mind. Traits are re-derived from the Trial scores with the shared table.
    public static Mind ReadMind(BinaryReader r, MindFactory f, Func<int, Mind?> known)
    {
        int id = r.ReadInt32();
        var m = new Mind { RemoteId = id, Name = r.ReadString(), Generation = r.ReadInt32(), Ratio = r.ReadSingle(), Vessel = (Species)r.ReadByte() };
        string title = r.ReadString();
        m.Title = title.Length == 0 ? null : title;
        m.Drills = new string[r.ReadInt32()];
        for (int i = 0; i < m.Drills.Length; i++) m.Drills[i] = r.ReadString();
        int np = r.ReadInt32();
        var parents = new Mind[np];
        for (int i = 0; i < np; i++)
        {
            int pid = r.ReadInt32();
            string pname = r.ReadString();
            parents[i] = known(pid) ?? new Mind { RemoteId = pid, Name = pname, Brain = f.BaseBrain, Trial = f.BaseTrial, Stub = true };
        }
        m.Parents = parents;
        var t = new TrialResult();
        for (int i = 0; i < Trial.AxisCount; i++) t.Scores[i] = r.ReadSingle();
        t.Erratic = r.ReadBoolean();
        t.NightDelta = r.ReadSingle();
        m.Trial = t;
        m.Skill = new float[r.ReadInt32()];
        for (int i = 0; i < m.Skill.Length; i++) m.Skill[i] = r.ReadSingle();
        if (r.ReadBoolean())
        {
            var w = new float[BrainSpec.WeightCount];
            for (int i = 0; i < w.Length; i++) w[i] = r.ReadSingle();
            m.Brain = new Brain(w);
        }
        else
        {
            m.Brain = f.BaseBrain;
            m.Stub = true;
        }
        m.Traits = f.Traits.Match(t);
        m.Aura = MindFactory.AuraOf(t);
        return m;
    }

    // A player's own Library, sent when joining a LAN game. Base minds travel by name only.
    public static byte[] WriteLibrary(IEnumerable<Mind> minds, ISet<Mind> baseMinds)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            var custom = minds.Where(m => !baseMinds.Contains(m)).ToList();
            w.Write(custom.Count);
            foreach (var m in custom)
            {
                w.Write(m.Name);
                w.Write(m.Generation);
                w.Write(m.Ratio);
                w.Write((byte)m.Vessel);
                w.Write(m.Drills.Length);
                foreach (var d in m.Drills) w.Write(d);
                w.Write(m.Parents.Length);
                foreach (var p in m.Parents) w.Write(p.Name);
                foreach (var x in m.Brain.Weights) w.Write(x);
            }
        }
        return ms.ToArray();
    }

    public static List<Mind> ReadLibrary(byte[] data, MindFactory f, IReadOnlyDictionary<string, Mind> baseMinds)
    {
        var result = new List<Mind>(baseMinds.Values);
        using var r = new BinaryReader(new MemoryStream(data));
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            string name = r.ReadString();
            int gen = r.ReadInt32();
            float ratio = r.ReadSingle();
            var vessel = (Species)r.ReadByte();
            var drills = new string[r.ReadInt32()];
            for (int d = 0; d < drills.Length; d++) drills[d] = r.ReadString();
            var parentNames = new string[r.ReadInt32()];
            for (int p = 0; p < parentNames.Length; p++) parentNames[p] = r.ReadString();
            var w = new float[BrainSpec.WeightCount];
            for (int k = 0; k < w.Length; k++) w[k] = r.ReadSingle();
            var parents = parentNames.Select(pn => result.FirstOrDefault(x => x.Name == pn)
                ?? new Mind { Name = pn, Brain = f.BaseBrain, Trial = f.BaseTrial, Stub = true }).ToArray();
            var m = f.Make(name, new Brain(w), parents, ratio, drills);
            m.Generation = gen;
            m.Vessel = vessel;
            result.Add(m);
        }
        return result;
    }

    // ---------- snapshots ----------

    public sealed class HostState
    {
        public int AlertsSent;
        public readonly HashSet<int> MindsSent = new();
    }

    // Minds the client needs to know about this tick: everything on the map, in its Library, queue, Dungeon or Pit.
    static IEnumerable<Mind> Referenced(Match m, Kingdom k)
    {
        foreach (var u in m.W.Units) if (u.Mind != null) yield return u.Mind;
        foreach (var x in k.Library) yield return x;
        foreach (var o in k.Queue) yield return o.Mind;
        foreach (var c in k.Captives) if (c.Mind != null) yield return c.Mind;
        foreach (var (id, choices) in m.PitChoices)
            if (m.W.Units[id].Team == k.Team) foreach (var c in choices) yield return c;
        if (m.MerchantMind != null) yield return m.MerchantMind;
    }

    public static byte[]? NewMinds(Match m, Kingdom k, HostState st)
    {
        var fresh = new List<Mind>();
        void Visit(Mind x)
        {
            if (x.Stub || st.MindsSent.Contains(x.NetId)) return;
            foreach (var p in x.Parents) Visit(p);
            if (st.MindsSent.Add(x.NetId)) fresh.Add(x);
        }
        foreach (var x in Referenced(m, k)) Visit(x);
        if (fresh.Count == 0) return null;
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write(fresh.Count);
            foreach (var x in fresh) WriteMind(w, x, k.Library.Contains(x));
        }
        return ms.ToArray();
    }

    public static byte[] Snapshot(Match m, Kingdom k, HostState st)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var world = m.W;
        w.Write(world.Time); w.Write(world.StepCount); w.Write(world.EclipseTimer);
        w.Write(world.Starving[0]); w.Write(world.Starving[1]);
        w.Write(m.Winner); w.Write(m.WinReason);

        w.Write(world.Units.Count);
        foreach (var u in world.Units)
        {
            w.Write((byte)u.Team); w.Write((byte)u.Species); w.Write(u.Active); w.Write(u.IsRuler);
            W(w, u.Pos); w.Write(u.Will); w.Write(u.SubduedTimer);
            w.Write((byte)u.CarryType); w.Write(u.CarryAmount);
            w.Write(u.CarriedBy); w.Write(u.CarryingUnit); w.Write(u.TussleTarget);
            w.Write((byte)u.LastAction); w.Write((byte)u.LastMove);
            w.Write((byte)u.Command); W(w, u.CommandTarget); w.Write(u.Stance);
            w.Write((byte)u.Order); W(w, u.OrderPos); w.Write(u.OrderTarget); w.Write((byte)u.OrderRes); w.Write(u.FreeWill);
            foreach (var f in new[] { u.FarmMul, u.MaterialMul, u.LumenMul, u.BuildMul, u.StrengthMul, u.SpeedMul, u.SightMul, u.StealMul, u.HealMul, u.CarryMul, u.HaulMul })
                w.Write(f);
            w.Write(u.Station?.Id ?? -1); w.Write(u.Mind?.NetId ?? -1);
        }
        w.Write(world.Buildings.Count);
        foreach (var b in world.Buildings)
        {
            w.Write((byte)b.Type); w.Write((byte)b.Team); W(w, b.Pos); w.Write(b.Radius);
            w.Write((byte)b.SiteFor); w.Write(b.BuildProgress); w.Write(b.DisabledTimer); w.Write(b.SabotageProgress); w.Write(b.Charge);
            w.Write(b.Level); w.Write(b.UpgradeTimer);
            foreach (var s in b.Stored) w.Write(s);
        }
        w.Write(world.Nodes.Count);
        foreach (var n in world.Nodes) { w.Write((byte)n.Type); W(w, n.Pos); w.Write(n.Amount); }

        foreach (var kk in m.Kingdoms)
        {
            w.Write(kk.Age); w.Write(kk.LumenCollected); w.Write(kk.MindsCaptured); w.Write(kk.Lost); w.Write(kk.MutationHeat); w.Write(kk.MirrorRaiders); w.Write(kk.MirrorGuards); w.Write(kk.WildCaptured);
            w.Write(kk.Rally.HasValue); W(w, kk.Rally ?? default); w.Write((int)kk.Job); w.Write(kk.Repeat?.NetId ?? -1);
            w.Write(string.Join(",", kk.Techs)); w.Write(kk.Researching?.Id ?? ""); w.Write(kk.Researching?.Remaining ?? 0f);
            w.Write(kk.Discovered.Count);
            foreach (var d in kk.Discovered) w.Write(d);
        }

        // The receiving player's own kingdom in detail.
        w.Write(k.Library.Count);
        foreach (var x in k.Library) w.Write(x.NetId);
        w.Write(k.Queue.Count);
        foreach (var o in k.Queue) { w.Write(o.Mind.NetId); w.Write(o.Remaining); }
        w.Write(k.Captives.Count);
        foreach (var c in k.Captives)
        {
            w.Write(c.Unit.Id); w.Write(c.Mind?.NetId ?? -1); w.Write(c.FromTeam); w.Write(c.Held); w.Write(c.Convert); w.Write(c.Interrogated);
        }
        w.Write(k.Training.Count);
        foreach (var j in k.Training)
        {
            w.Write(j.Unit.Id); w.Write(j.Drill); w.Write(j.Iteration);
            var curve = j.Curve.TakeLast(80).ToList();
            w.Write(curve.Count);
            foreach (var v in curve) w.Write(v);
        }
        var pits = m.PitChoices.Where(kv => world.Units[kv.Key].Team == k.Team).ToList();
        w.Write(pits.Count);
        foreach (var (id, choices) in pits)
        {
            w.Write(id);
            w.Write(choices.Length);
            foreach (var c in choices) w.Write(c.NetId);
        }
        w.Write(k.Essences.Count);
        foreach (var e in k.Essences) { w.Write(e.Name); w.Write(e.Source); w.Write((byte)e.Axis); }
        w.Write(k.Intel.Count);
        foreach (var id in k.Intel.Keys) w.Write(m.Kingdoms.SelectMany(x => x.Library).Concat(m.W.Units.Select(u => u.Mind)).FirstOrDefault(x => x?.Id == id)?.NetId ?? id);

        for (int t = 0; t < 2; t++) { w.Write(m.Royal[t]?.Id ?? -1); w.Write(m.RoyalTimeLeft(t)); }
        w.Write(m.MerchantMind?.NetId ?? -1);

        var alerts = m.Alerts.Skip(st.AlertsSent).ToList();
        st.AlertsSent = m.Alerts.Count;
        w.Write(alerts.Count);
        foreach (var a in alerts)
        {
            w.Write(a.Text); w.Write(a.Team); w.Write(a.At.HasValue);
            if (a.At.HasValue) W(w, a.At.Value);
            w.Write(a.Time);
        }
        w.Flush();
        return ms.ToArray();
    }

    public sealed class Mirror(MindFactory factory)
    {
        public readonly Dictionary<int, Mind> Minds = new();
        public readonly MindFactory Factory = factory;

        public Mind? Find(int id) => id < 0 ? null : Minds.GetValueOrDefault(id);

        public void ReadMinds(byte[] data)
        {
            using var r = new BinaryReader(new MemoryStream(data));
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var mind = ReadMind(r, Factory, Find);
                Minds[mind.RemoteId] = mind;
            }
        }

        public void Apply(Match m, Kingdom k, byte[] data)
        {
            using var r = new BinaryReader(new MemoryStream(data));
            var world = m.W;
            world.Time = r.ReadSingle(); world.StepCount = r.ReadInt32(); world.EclipseTimer = r.ReadSingle();
            world.Starving[0] = r.ReadBoolean(); world.Starving[1] = r.ReadBoolean();
            m.Winner = r.ReadInt32(); m.WinReason = r.ReadString();

            int nu = r.ReadInt32();
            for (int i = 0; i < nu; i++)
            {
                int team = r.ReadByte();
                var species = (Species)r.ReadByte();
                var u = i < world.Units.Count ? world.Units[i] : world.AddUnit(team, species, Vector2.Zero, null);
                u.Team = team;
                if (u.Species != species) { u.Species = species; u.Body = Body.Of(species); }
                u.Active = r.ReadBoolean(); u.IsRuler = r.ReadBoolean();
                u.Pos = V(r); u.Will = r.ReadSingle(); u.SubduedTimer = r.ReadSingle();
                u.CarryType = (ResourceType)r.ReadByte(); u.CarryAmount = r.ReadSingle();
                u.CarriedBy = r.ReadInt32(); u.CarryingUnit = r.ReadInt32(); u.TussleTarget = r.ReadInt32();
                u.LastAction = r.ReadByte(); u.LastMove = r.ReadByte();
                u.Command = (CommandType)r.ReadByte(); u.CommandTarget = V(r); u.Stance = r.ReadSingle();
                u.Order = (OrderKind)r.ReadByte(); u.OrderPos = V(r); u.OrderTarget = r.ReadInt32(); u.OrderRes = (ResourceType)r.ReadByte(); u.FreeWill = r.ReadBoolean();
                u.FarmMul = r.ReadSingle(); u.MaterialMul = r.ReadSingle(); u.LumenMul = r.ReadSingle(); u.BuildMul = r.ReadSingle();
                u.StrengthMul = r.ReadSingle(); u.SpeedMul = r.ReadSingle(); u.SightMul = r.ReadSingle(); u.StealMul = r.ReadSingle();
                u.HealMul = r.ReadSingle(); u.CarryMul = r.ReadSingle(); u.HaulMul = r.ReadSingle();
                int station = r.ReadInt32();
                u.Station = station >= 0 && station < world.Buildings.Count ? world.Buildings[station] : null;
                u.Mind = Find(r.ReadInt32());
                u.Controller = null;
            }
            int nb = r.ReadInt32();
            for (int i = 0; i < nb; i++)
            {
                var type = (BuildingType)r.ReadByte();
                int team = r.ReadByte();
                var pos = V(r);
                var b = i < world.Buildings.Count ? world.Buildings[i] : world.AddBuilding(type, team, pos);
                if (type == BuildingType.Farm && b.Type != BuildingType.Farm) { Match.PloughTiles(world, pos); m.FarmsChanged = true; }
                b.Type = type; b.Team = team; b.Pos = pos; b.Radius = r.ReadSingle();
                b.SiteFor = (BuildingType)r.ReadByte(); b.BuildProgress = r.ReadSingle(); b.DisabledTimer = r.ReadSingle();
                b.SabotageProgress = r.ReadSingle(); b.Charge = r.ReadSingle();
                b.Level = r.ReadInt32(); b.UpgradeTimer = r.ReadSingle();
                for (int s = 0; s < b.Stored.Length; s++) b.Stored[s] = r.ReadSingle();
            }
            // Re-resolve stations now that all buildings exist.
            int nn = r.ReadInt32();
            for (int i = 0; i < nn; i++)
            {
                var nType = (ResourceType)r.ReadByte();
                var nPos = V(r);
                float amt = r.ReadSingle();
                if (i < world.Nodes.Count) world.Nodes[i].Amount = amt;
                else world.AddNode(nType, nPos, amt);
            }

            foreach (var kk in m.Kingdoms)
            {
                kk.Age = r.ReadInt32(); kk.LumenCollected = r.ReadSingle(); kk.MindsCaptured = r.ReadInt32(); kk.Lost = r.ReadInt32();
                kk.MutationHeat = r.ReadSingle();
                kk.MirrorRaiders = r.ReadInt32(); kk.MirrorGuards = r.ReadInt32(); kk.WildCaptured = r.ReadInt32();
                bool hasRally = r.ReadBoolean(); var rally = V(r); kk.Rally = hasRally ? rally : null;
                kk.Job = (NewbornJob)r.ReadInt32();
                int rep = r.ReadInt32(); kk.Repeat = rep < 0 ? null : kk.Library.FirstOrDefault(x => x.NetId == rep);
                kk.Techs.Clear();
                foreach (var t in r.ReadString().Split(',', StringSplitOptions.RemoveEmptyEntries)) kk.Techs.Add(t);
                string rid = r.ReadString(); float rrem = r.ReadSingle();
                kk.Researching = rid.Length > 0 ? new Research { Id = rid, Remaining = rrem } : null;
                kk.Discovered.Clear();
                int nd = r.ReadInt32();
                for (int i = 0; i < nd; i++) kk.Discovered.Add(r.ReadString());
            }

            k.Library.Clear();
            int nl = r.ReadInt32();
            for (int i = 0; i < nl; i++) if (Find(r.ReadInt32()) is { } x) k.Library.Add(x);
            k.Queue.Clear();
            int nq = r.ReadInt32();
            for (int i = 0; i < nq; i++)
            {
                var mind = Find(r.ReadInt32());
                float rem = r.ReadSingle();
                if (mind != null) k.Queue.Add(new ProductionOrder { Mind = mind, Remaining = rem });
            }
            k.Captives.Clear();
            int nc = r.ReadInt32();
            for (int i = 0; i < nc; i++)
            {
                var c = new Captive { Unit = world.Units[r.ReadInt32()], Mind = Find(r.ReadInt32()), FromTeam = r.ReadInt32() };
                c.Held = r.ReadSingle(); c.Convert = r.ReadSingle(); c.Interrogated = r.ReadBoolean();
                k.Captives.Add(c);
            }
            k.Training.Clear();
            int nt = r.ReadInt32();
            for (int i = 0; i < nt; i++)
            {
                var job = new TrainingJob { Unit = world.Units[r.ReadInt32()], Drill = r.ReadString(), MirrorIteration = r.ReadInt32() };
                int nv = r.ReadInt32();
                for (int v = 0; v < nv; v++) job.MirrorCurve.Add(r.ReadSingle());
                k.Training.Add(job);
            }
            m.PitChoices.Clear();
            int np = r.ReadInt32();
            for (int i = 0; i < np; i++)
            {
                int id = r.ReadInt32();
                var choices = new Mind[r.ReadInt32()];
                for (int c = 0; c < choices.Length; c++) choices[c] = Find(r.ReadInt32()) ?? k.Library.First();
                m.PitChoices[id] = choices;
            }
            k.Essences.Clear();
            int ne = r.ReadInt32();
            for (int i = 0; i < ne; i++) k.Essences.Add(new Essence { Name = r.ReadString(), Source = r.ReadString(), Axis = (Axis)r.ReadByte() });
            k.Intel.Clear();
            int ni = r.ReadInt32();
            for (int i = 0; i < ni; i++)
            {
                var mind = Find(r.ReadInt32());
                if (mind != null) k.Intel[mind.Id] = mind.Trial;
            }

            for (int t = 0; t < 2; t++)
            {
                int ru = r.ReadInt32();
                float left = r.ReadSingle();
                m.SetRoyal(t, ru >= 0 ? world.Units[ru] : null, left);
            }
            m.MerchantMind = Find(r.ReadInt32());

            int na = r.ReadInt32();
            for (int i = 0; i < na; i++)
            {
                string text = r.ReadString();
                int team = r.ReadInt32();
                Vector2? at = r.ReadBoolean() ? V(r) : null;
                m.Alerts.Add(new Alert(text, team, at, r.ReadSingle()));
            }
        }
    }
}
