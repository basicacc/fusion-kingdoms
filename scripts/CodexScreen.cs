using FusionKingdoms.Sim;
using Godot;

namespace FusionKingdoms;

// The Codex: every trait in the table. Discovered ones show the recipe that first produced them.
public partial class CodexScreen : Control
{
    public override void _Ready()
    {
        Sound.I.Music("menu");
        SetAnchorsPreset(LayoutPreset.FullRect);
        var bg = new ColorRect { Color = new Color("0d0d14") };
        bg.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        var lib = MindLibrary.I;
        var all = lib.Factory.Traits.Traits;
        var root = Ui.VBox(10);
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.OffsetLeft = 40; root.OffsetRight = -40; root.OffsetTop = 24; root.OffsetBottom = -24;
        AddChild(root);

        var top = Ui.HBox(16);
        top.AddChild(Ui.Label("THE CODEX", 30, Ui.Accent));
        var count = Ui.Label($"{all.Count(t => lib.Codex.ContainsKey(t.Name))} of {all.Count} traits discovered", 15, Ui.Muted);
        count.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        top.AddChild(count);
        top.AddChild(Ui.Button("Back", () => GetTree().ChangeSceneToFile("res://scenes/Menu.tscn")));
        root.AddChild(top);
        root.AddChild(Ui.Label("Traits are named from how a mind actually behaves in the Trial. Discover new ones by fusing, drilling, mutating and stealing minds; the first recipe that produced each is kept here.", 13, Ui.Muted, true));

        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        var list = Ui.VBox(6);
        list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(list);
        root.AddChild(scroll);

        foreach (var t in all.OrderByDescending(t => lib.Codex.ContainsKey(t.Name)).ThenByDescending(t => t.Tier == Tier.Flaw ? -1 : (int)t.Tier).ThenBy(t => t.Name))
        {
            var box = Ui.Panel(Ui.BgSoft, 8);
            var v = Ui.VBox(3);
            bool found = lib.Codex.TryGetValue(t.Name, out var entry);
            var head = Ui.HBox(10);
            var tier = new PanelContainer();
            tier.AddThemeStyleboxOverride("panel", Ui.Box(Ui.TierColor(t.Tier), 3));
            tier.AddChild(Ui.Label(t.Tier.ToString(), 11));
            head.AddChild(tier);
            head.AddChild(Ui.Label(found ? t.Name : "???", 16, found ? Colors.White : Ui.Muted));
            v.AddChild(head);
            if (found)
            {
                v.AddChild(Ui.Label(t.Desc, 12, null, true));
                string recipe = t.Special != null ? "" : "Pattern: " + string.Join(", ", t.When.Select(c => $"{c.Axis} {c.Op} {c.Level}")) + ".  ";
                v.AddChild(Ui.Label($"{recipe}First seen in {entry!.Mind}: {entry.Recipe}", 11, Ui.Muted, true));
                v.AddChild(Ui.Label($"Discovered {entry.When} by {entry.Who}", 10, Ui.Muted));
            }
            else v.AddChild(Ui.Label("Not yet discovered.", 12, Ui.Muted));
            box.AddChild(v);
            list.AddChild(box);
        }
    }
}
