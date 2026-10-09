using FusionKingdoms.Sim;
using Godot;

namespace FusionKingdoms;

public static class Ui
{
    public static readonly Color Bg = new("15151c");
    public static readonly Color BgSoft = new("1d1d27");
    public static readonly Color Accent = new("f6c544");
    public static readonly Color Lumen = new("7ff0ff");
    public static readonly Color Muted = new(1, 1, 1, 0.55f);

    public static StyleBoxFlat Box(Color bg, int pad = 8, int radius = 4) => new()
    {
        BgColor = bg,
        ContentMarginLeft = pad, ContentMarginRight = pad, ContentMarginTop = pad, ContentMarginBottom = pad,
        CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
    };

    // No colour: the framed royal panel. BgSoft: a recessed section inside a panel. Anything else: a flat tinted box.
    public static PanelContainer Panel(Color? bg = null, int pad = 8)
    {
        var p = new PanelContainer();
        StyleBox box = bg == null ? Skin.Box("panel", 8, pad + 6)
            : bg == BgSoft ? Skin.Box("panel_soft", 4, pad)
            : Box(bg.Value, pad);
        p.AddThemeStyleboxOverride("panel", box);
        return p;
    }

    public static TextureRect Icon(string name, int size = 20) => new()
    {
        Texture = Skin.Tex($"icon_{name}"),
        CustomMinimumSize = new Vector2(size, size),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
    };

    public static Label Label(string text, int size = 12, Color? color = null, bool wrap = false)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c) l.Modulate = c;
        if (wrap) l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return l;
    }

    public static Label Header(string text) => Label(text, 15, Accent);

    public static Button Button(string text, Action onPress, bool expand = false, string? tooltip = null)
    {
        var b = new Button { Text = text, TooltipText = tooltip ?? "" };
        if (expand) b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        b.Pressed += onPress;
        return b;
    }

    public static VBoxContainer VBox(int sep = 6)
    {
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", sep);
        return v;
    }

    public static HBoxContainer HBox(int sep = 6)
    {
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", sep);
        return h;
    }

    public static void Clear(Node n)
    {
        foreach (var c in n.GetChildren()) { n.RemoveChild(c); c.QueueFree(); }
    }

    public static Color TierColor(Tier t) => t switch
    {
        Tier.Flaw => new Color("8a2a2a"),
        Tier.Uncommon => new Color("2a6a3a"),
        Tier.Rare => new Color("2a4a8a"),
        Tier.Legendary => new Color("8a6a1a"),
        _ => new Color("3a3a44"),
    };

    public static Control Badge(TraitDef t)
    {
        var panel = new PanelContainer { TooltipText = $"{t.Tier}: {t.Desc}" };
        panel.AddThemeStyleboxOverride("panel", Box(TierColor(t.Tier), 3));
        panel.AddChild(Label(t.Name, 11));
        return panel;
    }

    public static HFlowContainer Badges(Mind m)
    {
        var f = new HFlowContainer();
        f.AddThemeConstantOverride("h_separation", 4);
        f.AddThemeConstantOverride("v_separation", 4);
        foreach (var t in m.Traits) f.AddChild(Badge(t));
        if (m.Traits.Count == 0) f.AddChild(Label("no notable traits", 11, Muted));
        return f;
    }

    public static string Res(float grain = 0, float timber = 0, float stone = 0, float lumen = 0)
    {
        var parts = new List<string>();
        if (grain > 0) parts.Add($"{grain:0} grain");
        if (timber > 0) parts.Add($"{timber:0} timber");
        if (stone > 0) parts.Add($"{stone:0} stone");
        if (lumen > 0) parts.Add($"{lumen:0} Lumen");
        return parts.Count == 0 ? "free" : string.Join(", ", parts);
    }
}
