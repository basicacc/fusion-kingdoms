using System.Runtime.InteropServices;
using System.Text.Json;
using FusionKingdoms.Sim;
using Godot;

namespace FusionKingdoms;

public static class MindUi
{
    public static Color AuraColor(this Mind m) => new(m.Aura.R, m.Aura.G, m.Aura.B);

    public static Color AxisColor(int axis)
    {
        var c = MindFactory.AxisColors[axis];
        return new Color(c.R, c.G, c.B);
    }

    // The full recipe of a mind: who it was fused from, at what ratio, and what happened to it since.
    public static string Recipe(this Mind m, int depth = 0)
    {
        string core = m.Parents.Length switch
        {
            0 => m.Name,
            1 => depth > 3 ? m.Parents[0].Name : m.Parents[0].Recipe(depth + 1),
            _ => depth > 3 ? $"{m.Parents[0].Name} + {m.Parents[1].Name}"
                : $"({m.Parents[0].Recipe(depth + 1)} {m.Ratio * 100:0}% + {m.Parents[1].Recipe(depth + 1)} {(1 - m.Ratio) * 100:0}%)",
        };
        var own = m.Parents.Length == 1 ? m.Drills.Except(m.Parents[0].Drills).ToArray() : m.Parents.Length == 0 ? [] : m.Drills;
        return own.Length == 0 ? core : $"{core} → {string.Join(" → ", own)}";
    }
}

public sealed record CodexEntry(string Trait, string Tier, string Mind, string Recipe, string When, string Who);

// Autoload "Minds": the player's Library of Minds and Codex, shared by Mind Lab and matches, saved to user://.
public partial class MindLibrary : Node
{
    const string SavePath = "user://library.json";

    public static MindLibrary I { get; private set; } = null!;

    public static readonly string[] BaseKinds = ["Farmer", "Builder", "Guard", "Scout", "Thief", "Hunter", "Healer", "Hoarder"];

    public readonly List<Mind> All = new();
    public readonly Dictionary<string, Mind> BaseMinds = new();
    public readonly Dictionary<string, CodexEntry> Codex = new();
    public MindFactory Factory = null!;
    bool _saveSoon;
    bool _loading;

    [Signal] public delegate void ChangedEventHandler();

    public override void _EnterTree() => I = this;

    public override void _Ready()
    {
        // AI ruler strategy found by self-play (tools/headless tune), if present.
        if (Godot.FileAccess.FileExists("res://data/ai_params.json")) AiParams.LoadJson(Godot.FileAccess.GetFileAsString("res://data/ai_params.json"));
        var traits = TraitTable.Parse(Godot.FileAccess.GetFileAsString("res://data/traits.json"));
        var baseBrain = LoadBrain("base") ?? throw new InvalidOperationException("res://brains/base.bin is missing");
        Factory = new MindFactory(traits, baseBrain);
        _loading = true;
        foreach (var kind in BaseKinds)
        {
            var brain = LoadBrain(kind.ToLowerInvariant());
            if (brain == null) { GD.PushWarning($"Missing brain for {kind}"); continue; }
            var mind = Factory.Make(kind, brain, []);
            BaseMinds[kind] = mind;
            Add(mind);
        }
        Factory.SetSkillReference(BaseMinds);
        Load();
        _loading = false;
    }

    public static Brain? LoadBrain(string name)
    {
        var path = $"res://brains/{name}.best.bin";
        if (!Godot.FileAccess.FileExists(path)) path = $"res://brains/{name}.bin";
        if (!Godot.FileAccess.FileExists(path)) return null;
        return new Brain(MemoryMarshal.Cast<byte, float>(Godot.FileAccess.GetFileAsBytes(path)).ToArray());
    }

    public Mind Make(string name, Brain brain, Mind[] parents, float ratio = 1f, string[]? drills = null) =>
        Factory.Make(name, brain, parents, ratio, drills);

    public Mind Add(Mind m)
    {
        if (!All.Contains(m))
        {
            m.Name = Match.UniqueName(All, m.Name);
            All.Add(m);
        }
        Record(m);
        EmitSignal(SignalName.Changed);
        if (!_loading) _saveSoon = true;
        return m;
    }

    // The first time a trait shows up, the Codex keeps the recipe that produced it.
    void Record(Mind m)
    {
        foreach (var t in m.Traits)
        {
            if (Codex.ContainsKey(t.Name)) continue;
            Codex[t.Name] = new CodexEntry(t.Name, t.Tier.ToString(), m.Name, m.Recipe(), DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                System.Environment.UserName);
        }
    }

    public Mind? Find(int id) => All.FirstOrDefault(m => m.Id == id);

    public Mind Foresee(Mind a, Mind b, float ratioA) => Factory.Fuse(a, b, ratioA);

    public override void _Process(double delta)
    {
        if (!_saveSoon) return;
        _saveSoon = false;
        Save();
    }

    // ---------- saving ----------

    sealed record SavedMind(int Key, string Name, string Weights, int Generation, int[] Parents, string[] ParentNames,
        float Ratio, string[] Drills, string Vessel);

    sealed record SaveFile(List<SavedMind> Minds, List<CodexEntry> Codex);

    public void Save()
    {
        var keys = new Dictionary<Mind, int>();
        foreach (var (kind, m) in BaseMinds) keys[m] = -1 - Array.IndexOf(BaseKinds, kind);
        var saved = new List<SavedMind>();
        int next = 1;
        foreach (var m in All.Where(m => !BaseMinds.ContainsValue(m))) keys[m] = next++;
        foreach (var m in All.Where(m => !BaseMinds.ContainsValue(m)))
        {
            var bytes = MemoryMarshal.AsBytes(m.Brain.Weights.AsSpan()).ToArray();
            saved.Add(new SavedMind(keys[m], m.Name, Convert.ToBase64String(bytes), m.Generation,
                m.Parents.Select(p => keys.GetValueOrDefault(p, 0)).ToArray(), m.Parents.Select(p => p.Name).ToArray(),
                m.Ratio, m.Drills, m.Vessel.ToString()));
        }
        var json = JsonSerializer.Serialize(new SaveFile(saved, Codex.Values.ToList()), new JsonSerializerOptions { WriteIndented = true });
        using var f = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
        f?.StoreString(json);
    }

    void Load()
    {
        if (!Godot.FileAccess.FileExists(SavePath)) return;
        SaveFile? file;
        try { file = JsonSerializer.Deserialize<SaveFile>(Godot.FileAccess.GetFileAsString(SavePath)); }
        catch (Exception e) { GD.PushWarning($"Could not read the saved Library: {e.Message}"); return; }
        if (file == null) return;
        foreach (var c in file.Codex) Codex[c.Trait] = c;

        var byKey = new Dictionary<int, Mind>();
        foreach (var (kind, m) in BaseMinds) byKey[-1 - Array.IndexOf(BaseKinds, kind)] = m;
        foreach (var s in file.Minds)
        {
            var w = MemoryMarshal.Cast<byte, float>(Convert.FromBase64String(s.Weights)).ToArray();
            if (w.Length != BrainSpec.WeightCount) continue;
            var parents = s.Parents.Select((k, i) => byKey.GetValueOrDefault(k) ?? Stub(s.ParentNames[i])).ToArray();
            var m = Factory.Make(s.Name, new Brain(w), parents, s.Ratio, s.Drills);
            m.Generation = s.Generation;
            if (Enum.TryParse<Species>(s.Vessel, out var v)) m.Vessel = v;
            byKey[s.Key] = m;
            Add(m);
        }
    }

    Mind Stub(string name) => new() { Name = name, Brain = Factory.BaseBrain, Trial = BaseMinds.Values.First().Trial };
}
