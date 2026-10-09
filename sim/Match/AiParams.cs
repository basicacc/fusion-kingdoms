using System.Text.Json;

namespace FusionKingdoms.Sim;

// Every number the AI ruler's strategy uses, in one place, so it can adapt them during a match (from scouting)
// and so self-play can tune them between matches (tools/headless "tune"; results in data/ai_params.json).
public sealed class AiParams
{
    public float FoodShare { get; set; } = 0.35f;
    public float TimberShare { get; set; } = 0.30f;
    public float StoneShare { get; set; } = 0.15f;
    public float LumenShare { get; set; } = 0.15f;
    public float ArmyFraction { get; set; } = 0.12f;     // share of the population kept as fighters
    public float AttackStart { get; set; } = 360f;       // earliest campaign, seconds
    public float AttackRatio { get; set; } = 1.5f;       // our fighters needed per enemy fighter seen at their home
    public int AttackMin { get; set; } = 6;
    public float RetreatAt { get; set; } = 0.4f;         // fall back when this share of the army is still standing
    public int Defenders { get; set; } = 2;              // sent per intruder
    public float RaidNightOnly { get; set; } = 1f;       // >0.5: thieves raid only at night unless there are many
    public float ScoutEvery { get; set; } = 300f;        // seconds between scouting runs
    public float FuseEvery { get; set; } = 100f;
    public float SpireAlarm { get; set; } = 250f;        // enemy Spire charge that triggers sabotage

    public AiParams Clone() => (AiParams)MemberwiseClone();

    public static AiParams For(AiPersonality p)
    {
        var d = Tuned.TryGetValue(p, out var t) ? t.Clone() : new AiParams();
        if (Tuned.ContainsKey(p)) return d;
        switch (p)
        {
            case AiPersonality.Warden:
                d.ArmyFraction = 0.18f; d.AttackStart = 600f; d.AttackMin = 10; d.Defenders = 3; break;
            case AiPersonality.Archmage:
                d.LumenShare = 0.25f; d.ArmyFraction = 0.10f; d.FuseEvery = 120f; break;
            default:
                d.FuseEvery = 70f; break;
        }
        return d;
    }

    // Parameters found by self-play, if any have been saved.
    public static readonly Dictionary<AiPersonality, AiParams> Tuned = new();

    public static void Load(string path)
    {
        if (File.Exists(path)) LoadJson(File.ReadAllText(path));
    }

    // From text (the game reads it out of its packed files, where there is no plain file path).
    public static void LoadJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        var all = JsonSerializer.Deserialize<Dictionary<string, AiParams>>(json);
        if (all == null) return;
        foreach (var (k, v) in all)
            if (Enum.TryParse<AiPersonality>(k, out var p)) Tuned[p] = v;
    }

    public static void Save(string path, Dictionary<AiPersonality, AiParams> all) =>
        File.WriteAllText(path, JsonSerializer.Serialize(all.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            new JsonSerializerOptions { WriteIndented = true }));

    // A random nudge of every number, for self-play search.
    public AiParams Mutate(Random rng, float scale)
    {
        var c = Clone();
        float N(float v, float lo, float hi) => Math.Clamp(v * (1f + scale * (float)(rng.NextDouble() * 2 - 1)), lo, hi);
        c.FoodShare = N(FoodShare, 0.15f, 0.6f); c.TimberShare = N(TimberShare, 0.1f, 0.5f);
        c.StoneShare = N(StoneShare, 0.05f, 0.35f); c.LumenShare = N(LumenShare, 0.05f, 0.4f);
        c.ArmyFraction = N(ArmyFraction, 0.05f, 0.45f); c.AttackStart = N(AttackStart, 120f, 900f);
        c.AttackRatio = N(AttackRatio, 0.8f, 3f); c.AttackMin = Math.Clamp((int)MathF.Round(N(AttackMin, 3, 16)), 3, 16);
        c.RetreatAt = N(RetreatAt, 0.15f, 0.7f); c.Defenders = Math.Clamp((int)MathF.Round(N(Defenders, 1, 4)), 1, 4);
        c.RaidNightOnly = rng.NextDouble() < 0.15 ? 1f - RaidNightOnly : RaidNightOnly;
        c.ScoutEvery = N(ScoutEvery, 60f, 400f); c.FuseEvery = N(FuseEvery, 40f, 200f); c.SpireAlarm = N(SpireAlarm, 100f, 600f);
        return c;
    }

    public override string ToString() =>
        $"food {FoodShare:0.00} timber {TimberShare:0.00} stone {StoneShare:0.00} lumen {LumenShare:0.00} army {ArmyFraction:0.00} " +
        $"attack@{AttackStart:0}s x{AttackRatio:0.0} min{AttackMin} retreat {RetreatAt:0.00} def {Defenders} night {RaidNightOnly:0} scout {ScoutEvery:0} fuse {FuseEvery:0} spire {SpireAlarm:0}";
}
