using FusionKingdoms.Sim;
using Godot;

namespace FusionKingdoms;

// First-match hints: one goal at a time, ticked off when the player actually does it. Progress is remembered in
// user://settings.cfg, so it picks up where it left off and stays quiet once finished (the menu can reset it).
public partial class Tutorial : PanelContainer
{
    const string Settings = "user://settings.cfg";
    readonly MatchScene _s;
    Label _text = null!, _count = null!;
    int _step;
    float _flash;
    int _startHouses, _startPop;

    record Step(string Text, Func<MatchScene, Tutorial, bool> Done);

    static readonly Step[] Steps =
    [
        new("Select some of your subjects: drag a box around them, or press Ctrl+A.", (s, _) => s.Mine().Count >= 2),
        new("Right-click one of your fields to set them farming. They'll keep bringing grain home.",
            (s, _) => s.M.Subjects(s.Me).Any(u => u.Order == OrderKind.Gather && u.OrderRes == ResourceType.Food)),
        new("Now send someone to cut timber: select them and right-click a tree.",
            (s, _) => s.M.Subjects(s.Me).Any(u => u.Order == OrderKind.Gather && u.OrderRes == ResourceType.Timber)),
        new("Press B (or the Build button) and place a House: it raises your population cap.",
            (s, t) => s.M.BuildingsOf(s.Me).Count(b => b.Type == BuildingType.House || b.Type == BuildingType.BuildSite && b.SiteFor == BuildingType.House) > t._startHouses),
        new("Select a few subjects and right-click the building site to build it. Builders are fastest.",
            (s, _) => s.M.Subjects(s.Me).Any(u => u.Order == OrderKind.Build)),
        new("Click your Cradle (the egg) and produce a new subject from your Library of Minds.",
            (s, t) => s.Me.Queue.Count > 0 || s.M.Population(s.Me) > t._startPop),
        new("Double-click a subject to select everyone of its class on screen. Then press Ctrl+1 to save them as group 1.",
            (s, _) => s.Mine().Count >= 2 && s.Mine().Select(u => Classes.Of(u.Mind).Name).Distinct().Count() == 1),
        new("Build a Fusion Altar, then use it to fuse two subjects into a new mind with a new class.",
            (s, _) => s.Me.Library.Any(m => m.Parents.Length == 2)),
        new("Gather stone and Lumen, then advance to Age 2 at your Keep (click the castle).", (s, _) => s.Me.Age >= 2),
    ];

    public Tutorial(MatchScene s)
    {
        _s = s;
        var cfg = new ConfigFile();
        cfg.Load(Settings);
        _step = (int)cfg.GetValue("tutorial", "step", 0);
        bool off = (bool)cfg.GetValue("tutorial", "off", false);
        Visible = !off && _step < Steps.Length;
    }

    public static void Reset()
    {
        var cfg = new ConfigFile();
        cfg.Load(Settings);
        cfg.SetValue("tutorial", "step", 0);
        cfg.SetValue("tutorial", "off", false);
        cfg.Save(Settings);
    }

    public static bool Enabled
    {
        get { var c = new ConfigFile(); c.Load(Settings); return !(bool)c.GetValue("tutorial", "off", false); }
        set { var c = new ConfigFile(); c.Load(Settings); c.SetValue("tutorial", "off", !value); c.Save(Settings); }
    }

    public override void _Ready()
    {
        AddThemeStyleboxOverride("panel", Skin.Box("tooltip", 4, 12, 8));
        var v = Ui.VBox(4);
        var head = Ui.HBox(8);
        _count = Ui.Label("", 11, new Color("6a4a2a"));
        _count.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        head.AddChild(_count);
        var skip = Ui.Button("Hide hints", () => { Enabled = false; Visible = false; });
        skip.AddThemeFontSizeOverride("font_size", 11);
        head.AddChild(skip);
        v.AddChild(head);
        _text = Ui.Label("", 14, new Color("2a2033"), true);
        _text.CustomMinimumSize = new Vector2(440, 0);
        v.AddChild(_text);
        AddChild(v);
        _startHouses = _s.M.BuildingsOf(_s.Me).Count(b => b.Type == BuildingType.House);
        _startPop = _s.M.Population(_s.Me);
        Show();
    }

    void Show()
    {
        if (_step >= Steps.Length) { _text.Text = "You know the basics! Press F1 any time for every control. Good luck, ruler."; _count.Text = "Tutorial complete"; return; }
        _text.Text = Steps[_step].Text;
        _count.Text = $"Tip {_step + 1} of {Steps.Length}";
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        if (_flash > 0f)
        {
            _flash -= (float)delta;
            Modulate = new Color(1, 1, 1, 0.6f + 0.4f * MathF.Abs(MathF.Sin(_flash * 8f)));
            if (_flash <= 0f) { Modulate = Colors.White; if (_step >= Steps.Length) Visible = false; }
            return;
        }
        if (_step >= Steps.Length || !Steps[_step].Done(_s, this)) return;
        _step++;
        var cfg = new ConfigFile();
        cfg.Load(Settings);
        cfg.SetValue("tutorial", "step", _step);
        cfg.Save(Settings);
        Sound.I.Play("built", -6f);
        Show();
        _flash = _step >= Steps.Length ? 6f : 1f;
    }
}
