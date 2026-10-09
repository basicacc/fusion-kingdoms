using FusionKingdoms.Sim;
using Godot;

namespace FusionKingdoms;

public partial class RadarChart : Control
{
    public TrialResult? Result;
    public TrialResult? Compare;
    public Color Fill = new("7ff0ff");

    static readonly string[] Short = ["COU", "AGG", "GRD", "LOY", "DIS", "CUR", "PAT", "TEA", "CUN", "DIL", "TER", "CMP"];

    public override void _Draw()
    {
        var c = Size / 2f;
        float R = MathF.Min(Size.X, Size.Y) / 2f - 16f;
        var font = ThemeDB.FallbackFont;
        Vector2 At(int i, float v) => c + Vector2.FromAngle(-Mathf.Pi / 2 + i * Mathf.Tau / Trial.AxisCount) * R * v;

        foreach (float ring in new[] { 0.33f, 0.66f, 1f })
        {
            var pts = new Vector2[Trial.AxisCount + 1];
            for (int i = 0; i <= Trial.AxisCount; i++) pts[i] = At(i % Trial.AxisCount, ring);
            DrawPolyline(pts, new Color(1, 1, 1, 0.15f), 1f);
        }
        for (int i = 0; i < Trial.AxisCount; i++)
        {
            DrawLine(c, At(i, 1f), new Color(1, 1, 1, 0.12f));
            var lp = At(i, 1.13f) - new Vector2(9, -4);
            DrawString(font, lp, Short[i], fontSize: 9, modulate: MindUi.AxisColor(i));
        }
        if (Compare != null) DrawShape(Compare, new Color(1, 1, 1, 0.35f), false);
        if (Result != null) DrawShape(Result, Fill, true);

        void DrawShape(TrialResult r, Color col, bool fill)
        {
            var pts = new Vector2[Trial.AxisCount];
            for (int i = 0; i < Trial.AxisCount; i++) pts[i] = At(i, MathF.Max(0.03f, r.Scores[i] / 100f));
            if (fill) DrawColoredPolygon(pts, col with { A = 0.35f });
            DrawPolyline([.. pts, pts[0]], col, 1.5f);
        }
    }
}

public partial class TrialCard : VBoxContainer
{
    readonly Label _name = new(), _lineage = new(), _summary = new();
    readonly RadarChart _radar = new() { CustomMinimumSize = new Vector2(210, 190) };
    readonly HFlowContainer _badges = new();
    readonly SkillBars _skills = new() { CustomMinimumSize = new Vector2(0, 104) };
    readonly Button _tree = new() { Text = "Family tree" };
    public bool ShowTreeButton = true;
    Mind? _shown;

    static readonly (string hi, string lo)[] Words =
    [
        ("brave", "cowardly"), ("aggressive", "peaceful"), ("greedy", "modest"), ("loyal", "independent"),
        ("obedient", "willful"), ("curious", "stays put"), ("patient", "impulsive"), ("sticks with allies", "goes alone"),
        ("sneaky", "blunt"), ("hardworking", "lazy"), ("guards home", "roams far"), ("protects allies", "ignores the fallen"),
    ];

    public override void _Ready()
    {
        _name.AddThemeFontSizeOverride("font_size", 16);
        _lineage.AddThemeFontSizeOverride("font_size", 10);
        _lineage.Modulate = new Color(1, 1, 1, 0.6f);
        _summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _summary.AddThemeFontSizeOverride("font_size", 11);
        _badges.AddThemeConstantOverride("h_separation", 4);
        _badges.AddThemeConstantOverride("v_separation", 4);
        AddChild(_name);
        AddChild(_lineage);
        AddChild(_radar);
        AddChild(_badges);
        AddChild(_summary);
        AddChild(_skills);
        _tree.Pressed += () => { if (_shown != null) LineageTree.Open(this, _shown); };
        AddChild(_tree);
        Show(null);
    }

    public void Show(Mind? bp, Mind? compare = null)
    {
        Visible = bp != null;
        _shown = bp;
        if (bp == null) return;
        _tree.Visible = ShowTreeButton && bp.Parents.Length > 0;
        _name.Text = bp.Generation > 0 ? $"{bp.Display}  (gen {bp.Generation})" : bp.Display;
        _name.Modulate = bp.AuraColor().Lightened(0.2f);
        _lineage.Text = bp.Lineage;
        _radar.Result = bp.Trial;
        _radar.Compare = compare?.Trial;
        _radar.Fill = bp.AuraColor().Lightened(0.2f);
        _radar.QueueRedraw();
        foreach (var c in _badges.GetChildren()) c.QueueFree();
        foreach (var t in bp.Traits) _badges.AddChild(Badge(t));
        if (bp.Title != null) _badges.AddChild(new Label { Text = $"Unnamed profile: \"{bp.Title}\"", Modulate = new Color(1, 1, 1, 0.7f) });
        _summary.Text = Summary(bp.Trial);
        _skills.Mind = bp;
        _skills.Compare = compare;
        _skills.QueueRedraw();
    }

    static Control Badge(TraitDef t)
    {
        var col = t.Tier switch
        {
            Tier.Flaw => new Color("8a2a2a"),
            Tier.Uncommon => new Color("2a6a3a"),
            Tier.Rare => new Color("2a4a8a"),
            Tier.Legendary => new Color("8a6a1a"),
            _ => new Color("3a3a44"),
        };
        var panel = new PanelContainer { TooltipText = $"{t.Tier}: {t.Desc}" };
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = col, CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 1, ContentMarginBottom = 1,
        });
        var l = new Label { Text = t.Name };
        l.AddThemeFontSizeOverride("font_size", 11);
        panel.AddChild(l);
        return panel;
    }

    public static string Summary(TrialResult r)
    {
        var top = Enumerable.Range(0, Trial.AxisCount)
            .OrderByDescending(i => MathF.Abs(r.Scores[i] - 50f))
            .Take(3)
            .Select(i => r.Scores[i] >= 50f ? Words[i].hi : Words[i].lo);
        var s = string.Join(", ", top);
        return char.ToUpperInvariant(s[0]) + s[1..] + ".";
    }
}

// What the mind can do: one bar per job, 100% = as good as the original specialist.
public partial class SkillBars : Control
{
    public Mind? Mind;
    public Mind? Compare;

    public override void _Process(double delta)
    {
        if (Mind != null && Mind.Skill.Length == 0) QueueRedraw();
    }

    public override void _Draw()
    {
        if (Mind == null) return;
        if (Mind.Skill.Length == 0)
        {
            DrawString(ThemeDB.FallbackFont, new Vector2(0, 14), "Measuring skills...", fontSize: 11, modulate: new Color(1, 1, 1, 0.5f));
            return;
        }
        var font = ThemeDB.FallbackFont;
        float row = Size.Y / Skills.Jobs.Length, labelW = 64f, w = Size.X - labelW - 36f;
        for (int i = 0; i < Skills.Jobs.Length; i++)
        {
            float y = i * row;
            float v = Mind.Skill[i];
            DrawString(font, new Vector2(0, y + row - 3), Skills.Jobs[i].name, fontSize: 10, modulate: new Color(1, 1, 1, 0.7f));
            DrawRect(new Rect2(labelW, y + 2, w, row - 4), new Color(1, 1, 1, 0.06f));
            if (Compare != null && Compare.Skill.Length > i)
                DrawRect(new Rect2(labelW, y + row - 3, w * Math.Min(1f, Compare.Skill[i] / 100f), 1.5f), new Color(1, 1, 1, 0.5f));
            var col = v >= 60f ? new Color("7ff0ff") : v >= 25f ? new Color("5aa0b0") : new Color("4a5560");
            DrawRect(new Rect2(labelW, y + 2, w * Math.Min(1f, v / 100f), row - 4), col);
            DrawString(font, new Vector2(labelW + w + 4, y + row - 3), $"{v:0}%", fontSize: 10, modulate: new Color(1, 1, 1, 0.7f));
        }
    }
}
