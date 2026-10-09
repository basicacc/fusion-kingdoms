using FusionKingdoms.Sim;
using Godot;

namespace FusionKingdoms;

// Runs evolution strategies on a background thread and shows the learning curve plus a live drill.
public partial class TrainingGrounds : VBoxContainer
{
    const float GenerationsPerSecond = 4f;

    static readonly (string name, MindKind kind)[] Drills =
    [
        ("Harvest", MindKind.Farmer), ("Raid", MindKind.Thief), ("Hold the Line", MindKind.Guard),
        ("Hunt", MindKind.Hunter), ("Explore", MindKind.Scout), ("Build", MindKind.Builder), ("Mend", MindKind.Healer),
    ];

    OptionButton _subject = null!, _drill = null!;
    Button _start = null!, _keep = null!;
    Label _status = null!;
    LearningCurve _curve = null!;
    DrillView _view = null!;

    Mind? _source;
    DrillTrainer? _trainer;
    Task? _running;
    volatile bool _stop;
    float _sinceGen;
    float _baseline;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 6);
        _subject = new OptionButton();
        _drill = new OptionButton();
        foreach (var (name, _) in Drills) _drill.AddItem(name);
        AddChild(_subject);
        AddChild(_drill);

        var row = new HBoxContainer();
        _start = new Button { Text = "Start drill", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _start.Pressed += Toggle;
        _keep = new Button { Text = "Keep result", SizeFlagsHorizontal = SizeFlags.ExpandFill, Disabled = true };
        _keep.Pressed += Keep;
        row.AddChild(_start);
        row.AddChild(_keep);
        AddChild(row);

        _curve = new LearningCurve { CustomMinimumSize = new Vector2(0, 90) };
        AddChild(_curve);
        _view = new DrillView { CustomMinimumSize = new Vector2(0, 200) };
        AddChild(_view);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Text = "Pick a subject and a drill." };
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

    void Toggle()
    {
        if (_trainer != null) { StopTraining(); return; }
        if (_subject.Selected < 0) return;
        _source = MindLibrary.I.Find(_subject.GetItemId(_subject.Selected))!;
        var kind = Drills[_drill.Selected].kind;
        _trainer = new DrillTrainer(_source.Brain, kind, (int)GD.Randi());
        _baseline = Enumerable.Range(0, 8).Average(s => DrillTrainer.RunDrill(_source.Brain, kind, s));
        _curve.Points.Clear();
        _view.Begin(_trainer.Brain, kind);
        _stop = false;
        _sinceGen = 0f;
        _start.Text = "Stop";
        _keep.Disabled = true;
    }

    void StopTraining()
    {
        _stop = true;
        _running?.Wait();
        _running = null;
        _start.Text = "Start drill";
        _keep.Disabled = _trainer == null || _trainer.Iteration == 0;
    }

    public override void _Process(double delta)
    {
        if (_trainer == null || _stop) return;
        _sinceGen += (float)delta;
        if (_running == null || _running.IsCompleted)
        {
            if (_running != null)
            {
                _curve.Points.Add(_trainer.Curve[^1]);
                _curve.QueueRedraw();
                _view.SetBrain(_trainer.Brain);
                _status.Text = $"{Drills[_drill.Selected].name} drill, generation {_trainer.Iteration}. " +
                               $"Score {_trainer.Curve[^1]:0.0} (started at {_baseline:0.0}).";
                _running = null;
            }
            if (_sinceGen >= 1f / GenerationsPerSecond)
            {
                _sinceGen = 0f;
                var t = _trainer;
                _running = Task.Run(() => t.Step());
            }
        }
    }

    void Keep()
    {
        if (_trainer == null || _source == null) return;
        string drill = Drills[_drill.Selected].name;
        var bp = MindLibrary.I.Make($"{_source.Name}·{drill}", _trainer.Brain, [_source], 1f, [.. _source.Drills, $"{drill} x{_trainer.Iteration}"]);
        MindLibrary.I.Add(bp);
        _status.Text = $"Saved {bp.Name}: {string.Join(", ", bp.Traits.Select(t => t.Name))}";
        _trainer = null;
        _keep.Disabled = true;
    }

    public override void _ExitTree()
    {
        _stop = true;
        _running?.Wait();
    }
}

public partial class LearningCurve : Control
{
    public readonly List<float> Points = new();

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("0d0d12"));
        if (Points.Count < 2) return;
        float lo = Math.Min(0f, Points.Min()), hi = Math.Max(lo + 1f, Points.Max());
        Vector2 At(int i, float v) => new(i / (float)(Points.Count - 1) * Size.X, Size.Y - (v - lo) / (hi - lo) * (Size.Y - 6) - 3);
        var raw = new Vector2[Points.Count];
        var smooth = new Vector2[Points.Count];
        float ema = Points[0];
        for (int i = 0; i < Points.Count; i++)
        {
            ema = i == 0 ? Points[0] : ema * 0.85f + Points[i] * 0.15f;
            raw[i] = At(i, Points[i]);
            smooth[i] = At(i, ema);
        }
        DrawPolyline(raw, new Color(0.5f, 0.94f, 1f, 0.25f), 1f, true);
        DrawPolyline(smooth, new Color("7ff0ff"), 2f, true);
        DrawString(ThemeDB.FallbackFont, new Vector2(4, 12), $"{hi:0.0}", fontSize: 9, modulate: new Color(1, 1, 1, 0.5f));
    }
}

// The small arena next to the Grounds: the current brain replaying the drill on a loop.
public partial class DrillView : Control
{
    Scenario? _sc;
    Brain? _brain;
    MindKind _kind;
    readonly float[] _obs = new float[BrainSpec.In.Count];
    readonly Random _rng = new();
    float _acc;

    public void Begin(Brain brain, MindKind kind)
    {
        _brain = brain;
        _kind = kind;
        Restart();
    }

    public void SetBrain(Brain brain) => _brain = brain;

    void Restart()
    {
        _sc = Scenario.Create(_kind);
        _sc.Reset(_rng.Next());
    }

    public override void _Process(double delta)
    {
        if (_sc == null || _brain == null) return;
        _acc += (float)delta * 3f;
        while (_acc >= Rules.Dt)
        {
            _acc -= Rules.Dt;
            Observer.Build(_sc.W, _sc.Learner, _obs);
            var (m, a) = _brain.Decide(_obs, _rng, false);
            _sc.Step(m, a);
            if (_sc.Done || _sc.W.StepCount >= DrillTrainer.DrillSteps) Restart();
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("0d0d12"));
        if (_sc == null) return;
        var w = _sc.W;
        float s = MathF.Min(Size.X / w.Width, Size.Y / w.Height);
        var off = (Size - new Vector2(w.Width, w.Height) * s) / 2f;
        Vector2 P(System.Numerics.Vector2 p) => off + new Vector2(p.X, p.Y) * s;
        for (int y = 0; y < w.Height; y++)
        for (int x = 0; x < w.Width; x++)
        {
            var t = w.Map[y * w.Width + x];
            if (t == Terrain.Grass) continue;
            var col = t switch { Terrain.Forest => new Color("1f4a2a"), Terrain.Rock => new Color("4a4a52"), Terrain.Farmland => new Color("6b5a2e"), _ => new Color("2a2a30") };
            DrawRect(new Rect2(off + new Vector2(x, y) * s, new Vector2(s, s)), col);
        }
        foreach (var n in w.Nodes)
            if (n.Amount > 0f)
                DrawCircle(P(n.Pos), s * 0.3f, n.Type switch
                {
                    ResourceType.Food => new Color("e8c84a"), ResourceType.Lumen => new Color("7ff0ff"),
                    ResourceType.Timber => new Color("2f9a3a"), _ => new Color("9a9aa8"),
                });
        foreach (var b in w.Buildings)
            DrawRect(new Rect2(P(b.Pos) - new Vector2(s, s), new Vector2(s, s) * 2), b.Team == 0 ? new Color("4f8cff") : new Color("ff5a4f"), false, 1.5f);
        foreach (var u in w.Units)
        {
            if (!u.Active) continue;
            var col = u == _sc.Learner ? Colors.White : u.Team == 0 ? new Color("4f8cff") : u.Team == 1 ? new Color("ff5a4f") : new Color("6fcf6f");
            DrawCircle(P(u.Pos), u == _sc.Learner ? s * 0.5f : s * 0.35f, u.Subdued ? col.Darkened(0.6f) : col);
        }
    }
}
