namespace FusionKingdoms.Sim;

// Every mind belongs to a class decided by what it is measurably good at: its best skill alone (a pure class)
// or its two best skills together (one of 28 hybrids). Stars rank how good it is within that class.
// Skills also set the subject's working stats, so breeding a better Farmer really means more grain.
public sealed record ClassDef(string Name, int A, int B, string Blurb)
{
    public bool Pure => A == B;
}

public static class Classes
{
    // Index order follows Skills.Jobs: Farming, Building, Guarding, Scouting, Raiding, Hunting, Healing, Hoarding.
    public static readonly string[] Roles = ["Farmer", "Builder", "Guard", "Scout", "Thief", "Hunter", "Healer", "Hoarder"];

    static readonly string[] RolePerk =
    [
        "harvests grain fast", "builds and gathers timber and stone fast", "hits hard in tussles", "runs fast and sees far",
        "steals fast", "drags captives home fast", "heals and revives fast", "gathers Lumen fast and carries more",
    ];

    static readonly Dictionary<(int, int), string> Hybrids = new()
    {
        [(0, 1)] = "Homesteader", [(0, 2)] = "Yeoman", [(0, 3)] = "Drover", [(0, 4)] = "Poacher", [(0, 5)] = "Trapper",
        [(0, 6)] = "Herbalist", [(0, 7)] = "Steward",
        [(1, 2)] = "Bulwark", [(1, 3)] = "Surveyor", [(1, 4)] = "Sapper", [(1, 5)] = "Woodwright", [(1, 6)] = "Bonesetter",
        [(1, 7)] = "Artificer",
        [(2, 3)] = "Outrider", [(2, 4)] = "Enforcer", [(2, 5)] = "Ranger", [(2, 6)] = "Templar", [(2, 7)] = "Vaultguard",
        [(3, 4)] = "Infiltrator", [(3, 5)] = "Tracker", [(3, 6)] = "Pilgrim", [(3, 7)] = "Prospector",
        [(4, 5)] = "Bandit", [(4, 6)] = "Charlatan", [(4, 7)] = "Magpie",
        [(5, 6)] = "Druid", [(5, 7)] = "Stalker",
        [(6, 7)] = "Mystic",
    };

    public static readonly ClassDef[] All = BuildAll();

    static ClassDef[] BuildAll()
    {
        var list = new List<ClassDef>();
        for (int i = 0; i < Roles.Length; i++) list.Add(new ClassDef(Roles[i], i, i, $"Pure {Roles[i]}: {RolePerk[i]}, even better than a hybrid."));
        foreach (var ((a, b), name) in Hybrids)
            list.Add(new ClassDef(name, a, b, $"{Roles[a]} + {Roles[b]}: {RolePerk[a]} and {RolePerk[b]}."));
        return list.ToArray();
    }

    public static ClassDef Of(int a, int b)
    {
        if (a == b) return All[a];
        var key = a < b ? (a, b) : (b, a);
        return All.First(c => c.A == key.Item1 && c.B == key.Item2);
    }

    // Every mind farms reasonably well, so farming only defines a class when the mind is truly farm-first:
    // excellent at it (100+) and only middling at everything else. Otherwise the class comes from the best other
    // skill, plus the second best when that is at least 60% as strong (and 30+ on its own) to make a hybrid.
    public static ClassDef Of(Mind? m)
    {
        if (m == null) return All[0];
        if (m.Skill.Length != Roles.Length) return FromLineage(m);
        var others = Enumerable.Range(1, Roles.Length - 1).OrderByDescending(i => m.Skill[i]).ToArray();
        int a = others[0], b = others[1];
        bool farmFirst = m.Skill[0] >= 100f && m.Skill[a] < 70f;
        if (farmFirst) return m.Skill[a] >= 40f ? Of(0, a) : All[0];
        if (m.Skill[a] < 30f) return All[0];
        bool hybrid = m.Skill[b] >= 40f && m.Skill[b] >= 0.6f * m.Skill[a];
        return hybrid ? Of(a, b) : All[a];
    }

    // Before skills are measured: guess from the base minds it came from.
    static ClassDef FromLineage(Mind m)
    {
        int Root(Mind x)
        {
            while (x.Parents.Length > 0) x = x.Parents[0];
            int i = Array.IndexOf(Roles, x.Name.Split(' ', '·', '+', '-')[0]);
            return i < 0 ? 0 : i;
        }
        int a = Root(m);
        int b = m.Parents.Length == 2 ? Root(m.Parents[1]) : a;
        return Of(a, b);
    }

    // 1 to 3 stars: how strong the class's defining skill(s) are.
    public static int Stars(Mind? m)
    {
        if (m == null || m.Skill.Length != Roles.Length) return 1;
        var c = Of(m);
        float v = c.Pure ? m.Skill[c.A] : (m.Skill[c.A] + m.Skill[c.B]) / 2f;
        return v >= 110f ? 3 : v >= 75f ? 2 : 1;
    }

    public static string Label(Mind? m)
    {
        var c = Of(m);
        return $"{c.Name} {new string('★', Stars(m))}";
    }

    // Measured skills (100 = as good as the original specialist) become working stats.
    public static void ApplyStats(Unit u)
    {
        var m = u.Mind;
        if (m == null || m.Skill.Length != Roles.Length) return;
        float S(int i) => Math.Clamp(m.Skill[i], 0f, 150f) / 100f;
        var c = Of(m);
        // A pure class gets a further 15% on its own speciality.
        float P(int i) => c.Pure && c.A == i ? 1.15f : 1f;
        u.FarmMul = (0.7f + 0.6f * S(0)) * P(0);
        u.MaterialMul = (0.7f + 0.6f * S(1)) * P(1);
        u.BuildMul = (0.6f + 0.8f * S(1)) * P(1);
        u.StrengthMul = (0.8f + 0.5f * S(2)) * P(2);
        u.SpeedMul = (0.92f + 0.2f * S(3)) * P(3);
        u.SightMul = (0.9f + 0.4f * S(3)) * P(3);
        u.StealMul = (0.6f + 0.8f * S(4)) * P(4);
        u.HaulMul = (0.8f + 0.6f * S(5)) * P(5);
        u.HealMul = (0.6f + 0.8f * S(6)) * P(6);
        u.LumenMul = (0.7f + 0.8f * S(7)) * P(7);
        u.CarryMul = (1f + 0.3f * S(7)) * P(7);
    }

    // Plain-language strengths for the UI, e.g. "Grain x1.4, Tussle x1.2".
    public static string StatLine(Unit u)
    {
        var parts = new List<(string, float)>
        {
            ("Grain", u.FarmMul), ("Timber/stone", u.MaterialMul), ("Build", u.BuildMul), ("Tussle", u.StrengthMul),
            ("Speed", u.SpeedMul), ("Steal", u.StealMul), ("Haul", u.HaulMul), ("Heal", u.HealMul), ("Lumen", u.LumenMul),
        };
        return string.Join("  ", parts.OrderByDescending(p => p.Item2).Take(4).Select(p => $"{p.Item1} x{p.Item2:0.0}"));
    }
}
