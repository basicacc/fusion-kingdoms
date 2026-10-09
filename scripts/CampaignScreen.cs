using System.Text.Json;
using FusionKingdoms.Sim;
using Godot;

namespace FusionKingdoms;

// The run in progress: which region is next and which minds were carried this far.
public static class CampaignRun
{
    const string Path = "user://campaign.json";
    sealed record Saved(int Region, List<string> Legacy);

    public static int Region;
    public static readonly List<string> Legacy = new();
    public static bool Active => Region > 0 || Legacy.Count > 0;

    public static void Load()
    {
        Region = 0;
        Legacy.Clear();
        if (!Godot.FileAccess.FileExists(Path)) return;
        try
        {
            var s = JsonSerializer.Deserialize<Saved>(Godot.FileAccess.GetFileAsString(Path));
            if (s == null) return;
            Region = s.Region;
            Legacy.AddRange(s.Legacy);
        }
        catch (Exception e) { GD.PushWarning($"Campaign save unreadable: {e.Message}"); }
    }

    public static void Save()
    {
        using var f = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Write);
        f?.StoreString(JsonSerializer.Serialize(new Saved(Region, Legacy)));
    }

    public static void Reset()
    {
        Region = 0;
        Legacy.Clear();
        Save();
    }

    public static List<Mind> LegacyMinds() =>
        Legacy.Select(n => MindLibrary.I.All.FirstOrDefault(m => m.Name == n)).Where(m => m != null).Cast<Mind>().ToList();
}

public partial class CampaignScreen : Control
{
    public override void _Ready()
    {
        Sound.I.Music("menu");
        CampaignRun.Load();
        SetAnchorsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = new Color("0d0d14") };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var root = Ui.VBox(10);
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.OffsetLeft = 60; root.OffsetRight = -60; root.OffsetTop = 30; root.OffsetBottom = -30;
        AddChild(root);

        var top = Ui.HBox(16);
        var title = Ui.Label("CAMPAIGN", 30, Ui.Accent);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(title);
        top.AddChild(Ui.Button("Abandon run", () => { CampaignRun.Reset(); GetTree().ReloadCurrentScene(); }));
        top.AddChild(Ui.Button("Back", () => GetTree().ChangeSceneToFile("res://scenes/Menu.tscn")));
        root.AddChild(top);
        root.AddChild(Ui.Label("Ten realms, one run. You start with the eight base minds; after each victory, carry two minds you bred, trained or stole into the next realm. Lose once and the run ends, but your Codex remembers everything.", 13, Ui.Muted, true));

        var h = Ui.HBox(20);
        h.SizeFlagsVertical = SizeFlags.ExpandFill;
        var list = Ui.VBox(6);
        list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        for (int i = 0; i < Campaign.Regions.Length; i++)
        {
            var r = Campaign.Regions[i];
            bool done = i < CampaignRun.Region, current = i == CampaignRun.Region;
            var box = Ui.Panel(current ? new Color("1f2a33") : Ui.BgSoft, 8);
            var v = Ui.VBox(2);
            v.AddChild(Ui.Label($"{(done ? "✓" : current ? "▶" : "·")}  {i + 1}. {r.Name}   ({r.Minutes:0} min, vs The {r.Foe})", 15,
                done ? new Color("7fd08f") : current ? Colors.White : Ui.Muted));
            v.AddChild(Ui.Label(r.Twist, 12, Ui.Muted, true));
            box.AddChild(v);
            list.AddChild(box);
        }
        var scroll = new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        scroll.AddChild(list);
        h.AddChild(scroll);

        var side = Ui.VBox(8);
        side.CustomMinimumSize = new Vector2(320, 0);
        side.AddChild(Ui.Header("Legacy Library"));
        var legacy = CampaignRun.LegacyMinds();
        if (legacy.Count == 0) side.AddChild(Ui.Label("Empty. Win a realm to carry minds forward.", 12, Ui.Muted, true));
        foreach (var m in legacy)
        {
            var l = Ui.Label($"{m.Name}: {string.Join(", ", m.Traits.Take(3).Select(t => t.Name))}", 12, m.AuraColor().Lightened(0.3f), true);
            side.AddChild(l);
        }
        side.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });
        if (CampaignRun.Region < Campaign.Regions.Length)
        {
            var go = Ui.Button($"Enter {Campaign.Regions[CampaignRun.Region].Name}", () =>
            {
                MatchScene.CampaignRegion = CampaignRun.Region;
                MatchScene.Seed = 0;
                MatchScene.Role = NetRole.Local;
                MatchScene.ShowcaseMode = false;
        GetTree().ChangeSceneToFile("res://scenes/Match.tscn");
            });
            go.CustomMinimumSize = new Vector2(0, 48);
            side.AddChild(go);
        }
        else
        {
            side.AddChild(Ui.Label("You conquered every realm. The Mad Archmage is no more.", 16, new Color("f2d24b"), true));
            side.AddChild(Ui.Button("Start a new run", () => { CampaignRun.Reset(); GetTree().ReloadCurrentScene(); }));
        }
        h.AddChild(side);
        root.AddChild(h);
    }
}
