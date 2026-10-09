namespace FusionKingdoms.Sim;

// A mind: a brain plus everything known about it (lineage, Trial result, traits). Blueprints in a Library are minds.
public sealed class Mind
{
    static int _nextId;

    public readonly int Id = Interlocked.Increment(ref _nextId);
    public string Name = "";
    public Brain Brain = null!;
    public int Generation;
    public Mind[] Parents = [];
    public float Ratio = 1f;
    public string[] Drills = [];
    public TrialResult Trial = null!;
    public List<TraitDef> Traits = new();
    public (float R, float G, float B) Aura;
    public Species Vessel = Species.Human;
    public string? Title;
    public int RemoteId = -1;
    public bool Stub;
    public int NetId => RemoteId >= 0 ? RemoteId : Id;
    public float[] SkillsRaw = [];
    public float[] Skill = [];
    public Task? SkillTask;

    public IEnumerable<(string name, float pct)> TopSkills(int n = 3) =>
        Skill.Select((v, i) => (Skills.Jobs[i].name, v)).Where(x => x.v >= 25f).OrderByDescending(x => x.v).Take(n);

    public string Lineage
    {
        get
        {
            string origin = Parents.Length switch
            {
                0 => "base mind",
                1 => $"from {Parents[0].Name}",
                _ => $"{Parents[0].Name} {Ratio * 100:0}% + {Parents[1].Name} {(1 - Ratio) * 100:0}%",
            };
            return Drills.Length == 0 ? origin : $"{origin}; {string.Join(", ", Drills)}";
        }
    }

    public bool Has(Tier tier) => Traits.Any(t => t.Tier == tier);

    // What players see: people are known by their class ("Yeoman ★★"), creatures and legends by their name.
    public string Display => Vessel == Species.Human && !Name.StartsWith("The ") ? Classes.Label(this) : Name;
}

public sealed class MindFactory(TraitTable traits, Brain baseBrain)
{
    public readonly TraitTable Traits = traits;
    public readonly Brain BaseBrain = baseBrain;
    TrialResult? _baseTrial;
    float[]? _floor, _reference;

    // Call once the eight base specialists exist, so skills can be expressed relative to them.
    public void SetSkillReference(IReadOnlyDictionary<string, Mind> baseMinds)
    {
        var baseRaw = Skills.Raw(BaseBrain);
        _reference = Skills.Jobs.Select((j, i) => baseMinds.TryGetValue(j.kind.ToString(), out var m) ? m.SkillsRaw[i] : baseRaw[i] + 1f).ToArray();
        // Jobs every subject already does well (farming) are shown as absolute ability, not an edge over the base.
        _floor = baseRaw.Select((b, i) => b < 0.5f * _reference[i] ? b : 0f).ToArray();
        foreach (var m in baseMinds.Values) m.Skill = Skills.Relative(m.SkillsRaw, _floor, _reference);
    }
    public TrialResult BaseTrial => _baseTrial ??= Trial.Run(BaseBrain);

    public Essence Distill(Mind m) => Essence.Distill(m, BaseBrain, BaseTrial);

    // Infusion (strength > 0) or removal (strength < 0) of an Essence.
    public Mind Infuse(Mind m, Essence e, float strength)
    {
        string verb = strength >= 0f ? "+" : "-";
        var made = Make($"{m.Name.Split(' ')[0]}{verb}{e.Axis}", e.ApplyTo(m.Brain, strength), [m], 1f,
            [.. m.Drills, $"{(strength >= 0 ? "infused" : "stripped of")} {e.Name} x{MathF.Abs(strength):0.##}"]);
        made.Vessel = m.Vessel;
        return made;
    }

    public Mind Make(string name, Brain brain, Mind[] parents, float ratio = 1f, string[]? drills = null)
    {
        var m = new Mind
        {
            Name = name,
            Brain = brain,
            Parents = parents,
            Ratio = ratio,
            Drills = drills ?? [],
            Generation = parents.Length switch { 0 => 0, 1 => parents[0].Generation, _ => parents.Max(p => p.Generation) + 1 },
            Trial = Trial.Run(brain),
        };
        if (parents.Length > 0) m.Vessel = parents[0].Vessel;
        m.Traits = Traits.Match(m.Trial);
        m.Title = m.Traits.Count == 0 ? Naming.Title(m.Trial) : null;
        if (_floor != null && _reference != null)
        {
            // Skills take ~50 ms to measure, so they fill in shortly after the mind is made instead of stalling the game.
            var floor = _floor;
            var reference = _reference;
            m.SkillTask = Task.Run(() =>
            {
                m.SkillsRaw = Skills.Raw(brain);
                m.Skill = Skills.Relative(m.SkillsRaw, floor, reference);
            });
        }
        else m.SkillsRaw = Skills.Raw(brain);
        m.Aura = AuraOf(m.Trial);
        return m;
    }

    // Foresee: the blend and its Trial, computed without committing anything.
    public Mind Fuse(Mind a, Mind b, float ratioA) =>
        Make(FusedName(a, b, ratioA), Brain.Fuse(BaseBrain, a.Brain, b.Brain, ratioA), [a, b], ratioA);

    public static string FusedName(Mind a, Mind b, float ratioA)
    {
        var major = ratioA >= 0.5f ? a : b;
        var minor = ratioA >= 0.5f ? b : a;
        string Head(string s) => s.Length <= 4 ? s : s[..(s.Length / 2 + 1)];
        string Tail(string s) => s.Length <= 4 ? s.ToLowerInvariant() : s[(s.Length / 2)..].ToLowerInvariant();
        return Head(major.Name.Split(' ', '*', '·')[0]) + Tail(minor.Name.Split(' ', '*', '·')[0]);
    }

    public static readonly (float R, float G, float B)[] AxisColors =
    [
        (0.95f, 0.95f, 0.95f), // Courage
        (0.88f, 0.28f, 0.28f), // Aggression
        (0.94f, 0.63f, 0.19f), // Greed
        (0.95f, 0.82f, 0.29f), // Loyalty
        (0.31f, 0.50f, 0.91f), // Discipline
        (0.31f, 0.85f, 0.91f), // Curiosity
        (0.60f, 0.64f, 0.71f), // Patience
        (0.25f, 0.75f, 0.63f), // Teamwork
        (0.63f, 0.35f, 0.91f), // Cunning
        (0.35f, 0.84f, 0.35f), // Diligence
        (0.63f, 0.47f, 0.31f), // Territoriality
        (0.94f, 0.48f, 0.78f), // Compassion
    ];

    // Each mind glows with the colors of its strongest axes, so an army's personality reads at a glance.
    public static (float R, float G, float B) AuraOf(TrialResult t)
    {
        float r = 0, g = 0, b = 0, wsum = 0;
        for (int i = 0; i < Trial.AxisCount; i++)
        {
            float w = MathF.Max(0f, t.Scores[i] - 55f);
            w *= w;
            r += AxisColors[i].R * w; g += AxisColors[i].G * w; b += AxisColors[i].B * w; wsum += w;
        }
        return wsum <= 0f ? (0.54f, 0.54f, 0.54f) : (r / wsum, g / wsum, b / wsum);
    }
}
