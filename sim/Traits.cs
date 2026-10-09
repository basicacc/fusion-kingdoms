using System.Text.Json;

namespace FusionKingdoms.Sim;

public enum Tier { Flaw, Common, Uncommon, Rare, Legendary }
public enum Level { ExtremeLow, Low, Mid, High, ExtremeHigh }

public sealed record TraitDef(string Name, Tier Tier, string Desc, Cond[] When, string[] Replaces, string? Special);

public readonly record struct Cond(Axis Axis, string Op, Level Level)
{
    public bool Test(Level actual) => Op switch
    {
        ">=" => actual >= Level,
        "<=" => actual <= Level,
        _ => actual == Level,
    };
}

public sealed class TraitTable
{
    public const int MaxShown = 5;
    public readonly List<TraitDef> Traits = new();

    public static Level LevelOf(float score) => score switch
    {
        <= 5f => Level.ExtremeLow,
        <= 33f => Level.Low,
        <= 66f => Level.Mid,
        < 95f => Level.High,
        _ => Level.ExtremeHigh,
    };

    public static TraitTable Parse(string json)
    {
        var table = new TraitTable();
        using var doc = JsonDocument.Parse(json);
        foreach (var t in doc.RootElement.GetProperty("traits").EnumerateArray())
        {
            var when = new List<Cond>();
            if (t.TryGetProperty("when", out var w))
                foreach (var c in w.EnumerateArray()) when.Add(ParseCond(c.GetString()!));
            var replaces = new List<string>();
            if (t.TryGetProperty("replaces", out var r))
                foreach (var x in r.EnumerateArray()) replaces.Add(x.GetString()!);
            table.Traits.Add(new TraitDef(
                t.GetProperty("name").GetString()!,
                Enum.Parse<Tier>(t.GetProperty("tier").GetString()!),
                t.GetProperty("desc").GetString()!,
                when.ToArray(), replaces.ToArray(),
                t.TryGetProperty("special", out var s) ? s.GetString() : null));
        }
        return table;
    }

    static Cond ParseCond(string s)
    {
        foreach (var op in new[] { ">=", "<=", "==" })
        {
            int i = s.IndexOf(op, StringComparison.Ordinal);
            if (i < 0) continue;
            return new Cond(Enum.Parse<Axis>(s[..i].Trim()), op, Enum.Parse<Level>(s[(i + 2)..].Trim()));
        }
        throw new FormatException($"Bad trait condition '{s}'");
    }

    public List<TraitDef> Match(TrialResult r)
    {
        var levels = new Level[Trial.AxisCount];
        for (int i = 0; i < levels.Length; i++) levels[i] = LevelOf(r.Scores[i]);

        var hits = new List<TraitDef>();
        foreach (var t in Traits)
        {
            bool ok = t.Special switch
            {
                "hollow" => levels.Count(l => l == Level.Mid) >= 8 && !levels.Any(l => l >= Level.High),
                "erratic" => r.Erratic,
                "nightowl" => r.NightDelta >= 12f,
                "rigid" => levels.Count(l => l == Level.ExtremeHigh) >= 2 && levels.Count(l => l <= Level.Low) >= 8,
                null => t.When.Length > 0 && t.When.All(c => c.Test(levels[(int)c.Axis])),
                _ => false,
            };
            if (ok) hits.Add(t);
        }
        var replaced = hits.SelectMany(t => t.Replaces).ToHashSet();
        return hits
            .Where(t => !replaced.Contains(t.Name))
            .OrderBy(t => t.Tier == Tier.Flaw ? 0 : 1)
            .ThenByDescending(t => t.Tier)
            .ThenByDescending(t => t.When.Sum(c => MathF.Abs(r.Scores[(int)c.Axis] - 50f)))
            .Take(MaxShown)
            .ToList();
    }
}
