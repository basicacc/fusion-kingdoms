using FusionKingdoms.Sim;
using Godot;

namespace FusionKingdoms;

public partial class MutationPit : VBoxContainer
{
    const float InstabilityPerUse = 30f;
    const float CoolPerSecond = 1.5f;

    public ArenaView? Arena;

    OptionButton _subject = null!;
    HSlider _level = null!;
    Label _levelLabel = null!, _status = null!;
    ProgressBar _instability = null!;
    GridContainer _grid = null!;
    Mind? _source;
    float _heat;
    readonly Random _rng = new();

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 6);
        _subject = new OptionButton();
        AddChild(_subject);

        var lv = new HBoxContainer();
        _level = new HSlider { MinValue = 1, MaxValue = 3, Step = 1, Value = 1, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _levelLabel = new Label { CustomMinimumSize = new Vector2(64, 0) };
        _level.ValueChanged += _ => _levelLabel.Text = $"Level {_level.Value:0}";
        _levelLabel.Text = "Level 1";
        lv.AddChild(_level);
        lv.AddChild(_levelLabel);
        AddChild(lv);

        var throwIn = new Button { Text = "Throw into the Pit" };
        throwIn.Pressed += Spawn;
        AddChild(throwIn);

        AddChild(new Label { Text = "Instability" });
        _instability = new ProgressBar { MaxValue = 100, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 10) };
        AddChild(_instability);

        _grid = new GridContainer { Columns = 2 };
        _grid.AddThemeConstantOverride("h_separation", 6);
        _grid.AddThemeConstantOverride("v_separation", 6);
        AddChild(_grid);

        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _status.AddThemeFontSizeOverride("font_size", 11);
        AddChild(_status);

        MindLibrary.I.Changed += Refresh;
        Refresh();
    }

    void Refresh()
    {
        int keep = _subject.Selected;
        _subject.Clear();
        foreach (var bp in MindLibrary.I.All) _subject.AddItem(bp.Name, bp.Id);
        if (_subject.ItemCount > 0) _subject.Select(Math.Clamp(keep, 0, _subject.ItemCount - 1));
    }

    public override void _Process(double delta)
    {
        _heat = MathF.Max(0f, _heat - CoolPerSecond * (float)delta);
        _instability.Value = _heat;
        _instability.Modulate = _heat > 70f ? new Color("ff5a4f") : _heat > 40f ? new Color("f0a030") : new Color("7ff0ff");
    }

    void Spawn()
    {
        if (_subject.Selected < 0) return;
        _source = MindLibrary.I.Find(_subject.GetItemId(_subject.Selected))!;
        int level = (int)_level.Value;
        foreach (var c in _grid.GetChildren()) c.QueueFree();

        float risk = _heat + InstabilityPerUse * level;
        _heat = MathF.Min(120f, risk);
        for (int i = 0; i < Mutation.Mutants; i++)
        {
            var bp = MindLibrary.I.Make($"{_source.Name}*{i + 1}", Mutation.Mutate(_source.Brain, level, _rng), [_source], 1f, [.. _source.Drills, $"mutated L{level}"]);
            _grid.AddChild(Card(bp));
        }
        _status.Text = "Pick one mutant to keep. The rest dissolve.";

        if (risk > 100f && Arena != null)
        {
            var aberration = Mutation.Mutate(_source.Brain, 3, _rng);
            var bp = MindLibrary.I.Make($"Aberration of {_source.Name}", aberration, [_source]);
            Arena.SpawnAberration(bp);
            _heat = 40f;
            _status.Text = $"The Pit boiled over! An Aberration of {_source.Name} escaped into the arena.";
        }
    }

    Control Card(Mind bp)
    {
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(128, 0) };
        var radar = new RadarChart { Result = bp.Trial, Compare = _source?.Trial, Fill = bp.AuraColor().Lightened(0.2f), CustomMinimumSize = new Vector2(128, 104) };
        v.AddChild(radar);
        bool legendary = bp.Traits.Any(t => t.Tier == Tier.Legendary);
        var traits = new Label
        {
            Text = bp.Traits.Count == 0 ? "no notable traits" : string.Join(", ", bp.Traits.Select(t => t.Name)),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = legendary ? new Color("f2d24b") : bp.Traits.Any(t => t.Tier == Tier.Flaw) ? new Color("ff8080") : Colors.White,
        };
        traits.AddThemeFontSizeOverride("font_size", 10);
        v.AddChild(traits);
        var keep = new Button { Text = "Keep" };
        keep.Pressed += () =>
        {
            MindLibrary.I.Add(bp);
            foreach (var c in _grid.GetChildren()) c.QueueFree();
            _status.Text = $"Kept {bp.Name}: {string.Join(", ", bp.Traits.Select(t => t.Name))}";
        };
        v.AddChild(keep);
        return v;
    }
}
