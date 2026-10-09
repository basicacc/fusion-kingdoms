using FusionKingdoms.Sim;
using Godot;

namespace FusionKingdoms;

public partial class MindLab : Node
{
    const float LeftW = 270f, RightW = 290f;

    ArenaView _arena = null!;
    ItemList _library = null!;
    TrialCard _libCard = null!, _foreCard = null!;
    OptionButton _slotA = null!, _slotB = null!;
    HSlider _ratio = null!;
    Label _ratioLabel = null!, _unitInfo = null!;
    Button _fuse = null!;
    Mind? _foreseen;

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(new Color("0d0d14"));
        Sound.I.Music("night");
        _arena = new ArenaView();
        AddChild(_arena);
        _arena.SelectionChanged += OnUnitSelected;

        var layer = new CanvasLayer();
        AddChild(layer);
        layer.AddChild(BuildLeft());
        layer.AddChild(BuildRight());

        MindLibrary.I.Changed += RefreshLibrary;
        RefreshLibrary();
        GetViewport().SizeChanged += FitArena;
        FitArena();
    }

    void FitArena()
    {
        var vp = GetViewport().GetVisibleRect().Size;
        var avail = new Vector2(vp.X - LeftW - RightW - 16f, vp.Y - 16f);
        float s = MathF.Min(avail.X / _arena.WorldSize.X, avail.Y / _arena.WorldSize.Y);
        _arena.Scale = new Vector2(s, s);
        _arena.Position = new Vector2(LeftW + 8f + (avail.X - _arena.WorldSize.X * s) / 2f, 8f + (avail.Y - _arena.WorldSize.Y * s) / 2f);
    }

    static PanelContainer Panel(float width, bool right)
    {
        var p = new PanelContainer { CustomMinimumSize = new Vector2(width, 0) };
        p.AnchorTop = 0; p.AnchorBottom = 1;
        p.AnchorLeft = right ? 1 : 0; p.AnchorRight = right ? 1 : 0;
        p.OffsetLeft = right ? -width : 0; p.OffsetRight = right ? 0 : width;
        p.AddThemeStyleboxOverride("panel", Skin.Box("panel", 8, 16, 14));
        return p;
    }

    static Label Header(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 15);
        l.Modulate = Ui.Accent;
        return l;
    }

    Control BuildLeft()
    {
        var panel = Panel(LeftW, false);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(scroll);
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(v);

        var title = new Label { Text = "MIND LAB" };
        title.AddThemeFontSizeOverride("font_size", 20);
        v.AddChild(title);

        var speed = new HBoxContainer();
        foreach (var (txt, sp) in new[] { ("||", 0f), ("1x", 1f), ("3x", 3f), ("8x", 8f) })
        {
            var b = new Button { Text = txt };
            b.Pressed += () => _arena.Speed = sp;
            speed.AddChild(b);
        }
        var menu = new Button { Text = "Menu" };
        menu.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/Menu.tscn");
        speed.AddChild(menu);
        var reset = new Button { Text = "Reset" };
        reset.Pressed += () => _arena.Reset((int)GD.Randi());
        speed.AddChild(reset);
        v.AddChild(speed);

        v.AddChild(Header("Library of Minds"));
        _library = new ItemList { CustomMinimumSize = new Vector2(0, 170) };
        _library.ItemSelected += i => _libCard.Show(SelectedBlueprint());
        v.AddChild(_library);

        var spawn = new HBoxContainer();
        var blue = new Button { Text = "Spawn Blue", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        blue.Pressed += () => SpawnSelected(0);
        var red = new Button { Text = "Spawn Red", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        red.Pressed += () => SpawnSelected(1);
        spawn.AddChild(blue);
        spawn.AddChild(red);
        v.AddChild(spawn);

        _libCard = new TrialCard();
        v.AddChild(_libCard);

        v.AddChild(Header("Selected subject"));
        _unitInfo = new Label { Text = "Click a subject in the arena.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _unitInfo.AddThemeFontSizeOverride("font_size", 11);
        v.AddChild(_unitInfo);
        return panel;
    }

    Control BuildRight()
    {
        var panel = Panel(RightW, true);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(scroll);
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(v);

        v.AddChild(Header("Fusion Altar"));
        _slotA = new OptionButton();
        _slotB = new OptionButton();
        _slotA.ItemSelected += _ => ClearForesee();
        _slotB.ItemSelected += _ => ClearForesee();
        v.AddChild(_slotA);
        v.AddChild(_slotB);

        _ratioLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _ratio = new HSlider { MinValue = 10, MaxValue = 90, Step = 10, Value = 50 };
        _ratio.ValueChanged += _ => { UpdateRatioLabel(); ClearForesee(); };
        v.AddChild(_ratio);
        v.AddChild(_ratioLabel);
        UpdateRatioLabel();

        var row = new HBoxContainer();
        var foresee = new Button { Text = "Foresee", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foresee.Pressed += Foresee;
        _fuse = new Button { Text = "Fuse", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _fuse.Pressed += Fuse;
        row.AddChild(foresee);
        row.AddChild(_fuse);
        v.AddChild(row);

        _foreCard = new TrialCard();
        v.AddChild(_foreCard);

        v.AddChild(Header("Training Grounds"));
        v.AddChild(new TrainingGrounds());

        v.AddChild(Header("Mutation Pit"));
        v.AddChild(new MutationPit { Arena = _arena });
        return panel;
    }

    Mind? Slot(OptionButton o) => o.Selected < 0 ? null : MindLibrary.I.Find(o.GetItemId(o.Selected));
    float RatioA => (float)_ratio.Value / 100f;

    void UpdateRatioLabel() => _ratioLabel.Text = $"{_ratio.Value:0} / {100 - _ratio.Value:0}";

    void ClearForesee()
    {
        _foreseen = null;
        _foreCard.Show(null);
    }

    void Foresee()
    {
        var a = Slot(_slotA);
        var b = Slot(_slotB);
        if (a == null || b == null || a == b) return;
        _foreseen = MindLibrary.I.Foresee(a, b, RatioA);
        _foreCard.Show(_foreseen, a);
    }

    void Fuse()
    {
        if (_foreseen == null) Foresee();
        if (_foreseen == null) return;
        var bp = MindLibrary.I.Add(_foreseen);
        _foreseen = null;
        _foreCard.Show(bp);
        _library.Select(MindLibrary.I.All.IndexOf(bp));
        _libCard.Show(bp);
    }

    Mind? SelectedBlueprint()
    {
        var sel = _library.GetSelectedItems();
        return sel.Length == 0 ? null : MindLibrary.I.All[sel[0]];
    }

    void SpawnSelected(int team)
    {
        var bp = SelectedBlueprint();
        if (bp != null) _arena.Spawn(bp, team);
    }

    void RefreshLibrary()
    {
        int keepA = _slotA.Selected, keepB = _slotB.Selected;
        _library.Clear();
        _slotA.Clear();
        _slotB.Clear();
        foreach (var bp in MindLibrary.I.All)
        {
            var label = bp.Traits.Count > 0 ? $"{bp.Display} — {string.Join(", ", bp.Traits.Take(2).Select(t => t.Name))}" : bp.Display;
            int i = _library.AddItem(label);
            _library.SetItemCustomFgColor(i, bp.AuraColor().Lightened(0.25f));
            _slotA.AddItem(bp.Display, bp.Id);
            _slotB.AddItem(bp.Display, bp.Id);
        }
        int n = MindLibrary.I.All.Count;
        if (n > 0)
        {
            _slotA.Select(keepA >= 0 && keepA < n ? keepA : 0);
            _slotB.Select(keepB >= 0 && keepB < n ? keepB : Math.Min(1, n - 1));
        }
    }

    void OnUnitSelected(int id)
    {
        if (id < 0) { _unitInfo.Text = "Click a subject in the arena."; return; }
        var u = _arena.W.Units[id];
        var bp = u.Mind;
        string who = bp?.Display ?? (u.IsRuler ? "Ruler" : u.Team == 2 ? $"Wild {u.Species}" : u.Species.ToString());
        string team = u.Team switch { 0 => "Blue", 1 => "Red", _ => "Wild" };
        _unitInfo.Text = $"{who} ({team})\n" + (bp != null ? string.Join(", ", bp.Traits.Select(t => t.Name)) : "");
        if (bp != null) _libCard.Show(bp);
    }

    public override void _Process(double delta)
    {
        if (_arena.Selected < 0) return;
        var u = _arena.W.Units[_arena.Selected];
        var bp = u.Mind;
        string state = !u.Active ? "captured" : u.Subdued ? (u.CarriedBy >= 0 ? "being carried off" : "subdued")
            : u.TussleTarget >= 0 ? "tussling" : u.CarryingUnit >= 0 ? "carrying a captive" : ((Act)u.LastAction).ToString().ToLowerInvariant();
        string who = bp?.Display ?? (u.IsRuler ? "Ruler" : u.Team == 2 ? $"Wild {u.Species}" : u.Species.ToString());
        _unitInfo.Text = $"{who}  [{(u.Team == 0 ? "Blue" : u.Team == 1 ? "Red" : "Wild")}]\n" +
                         $"Will {u.Will:0}/{u.Body.Will:0}   Carry {u.CarryAmount:0.0} {(u.CarryAmount > 0 ? u.CarryType : "")}\n" +
                         $"Doing: {state}";
    }
}
