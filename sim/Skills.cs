namespace FusionKingdoms.Sim;

// What a mind can actually do: its score on each specialist job with that job's trigger present,
// relative to the base specialist for that job (100 = as good as the original).
public static class Skills
{
    public static readonly (string name, MindKind kind)[] Jobs =
    [
        ("Farming", MindKind.Farmer), ("Building", MindKind.Builder), ("Guarding", MindKind.Guard), ("Scouting", MindKind.Scout),
        ("Raiding", MindKind.Thief), ("Hunting", MindKind.Hunter), ("Healing", MindKind.Healer), ("Hoarding", MindKind.Hoarder),
    ];

    const int Episodes = 32;   // fewer makes weak skills (hoarding, healing) mostly noise

    public static float[] Raw(Brain b)
    {
        var s = new float[Jobs.Length];
        for (int i = 0; i < Jobs.Length; i++) s[i] = (float)VecEnv.Evaluate(b, Jobs[i].kind, Episodes, 4242 + i, false, 1, true);
        return s;
    }

    // Floors are what the plain base brain scores, so 0% means "no better than an untrained subject".
    public static float[] Relative(float[] raw, float[] floor, float[] reference)
    {
        var r = new float[raw.Length];
        for (int i = 0; i < raw.Length; i++)
        {
            float span = MathF.Max(0.5f, reference[i] - floor[i]);
            r[i] = Math.Clamp((raw[i] - floor[i]) / span * 100f, 0f, 150f);
        }
        return r;
    }
}
